using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace gdlchat.Services;

internal sealed class DirectLineChatClient(IConfiguration configuration, IHttpClientFactory httpClientFactory)
{
    private readonly string userId = $"dl_{Guid.NewGuid():N}";
    private string? conversationId;
    private string? token;
    private string? watermark;
    private DateTimeOffset refreshAt;

    public void ResetConversation()
    {
        conversationId = null;
        token = null;
        watermark = null;
    }

    public async IAsyncEnumerable<string> SendAsync(string message,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Func<bool, Task>? onTyping = null)
    {
        var endpoint = configuration["CopilotStudioAgent:DirectLineEndpoint"]
            ?? throw new InvalidOperationException("Configure CopilotStudioAgent:DirectLineEndpoint.");
        var baseUrl = endpoint.TrimEnd('/');
        string streamUrl;

        if (conversationId is null)
        {
            var tokenEndpoint = configuration["CopilotStudioAgent:TokenEndpoint"]
                ?? throw new InvalidOperationException("Configure CopilotStudioAgent:TokenEndpoint.");
            using var tokenResponse = await RequestAsync(HttpMethod.Get, tokenEndpoint, null, false, cancellationToken);
            token = tokenResponse.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("The anonymous endpoint did not return a token.");
            using var conversation = await RequestAsync(HttpMethod.Post, $"{baseUrl}/conversations", null, true, cancellationToken);
            conversationId = conversation.RootElement.GetProperty("conversationId").GetString()
                ?? throw new InvalidOperationException("Direct Line did not return a conversation ID.");
            SetToken(conversation.RootElement);
            streamUrl = conversation.RootElement.GetProperty("streamUrl").GetString()
                ?? throw new InvalidOperationException("Direct Line did not return a stream URL.");
        }
        else
        {
            if (DateTimeOffset.UtcNow >= refreshAt)
            {
                using var refreshed = await RequestAsync(HttpMethod.Post, $"{baseUrl}/tokens/refresh", null, true, cancellationToken);
                SetToken(refreshed.RootElement);
            }
            var reconnectUrl = $"{baseUrl}/conversations/{Uri.EscapeDataString(conversationId)}";
            if (watermark is not null)
            {
                reconnectUrl += $"?watermark={Uri.EscapeDataString(watermark)}";
            }
            using var reconnected = await RequestAsync(HttpMethod.Get, reconnectUrl, null, true, cancellationToken);
            streamUrl = reconnected.RootElement.GetProperty("streamUrl").GetString()
                ?? throw new InvalidOperationException("Direct Line did not return a reconnect stream URL.");
        }

        var streamUri = new UriBuilder(streamUrl);
        streamUri.Scheme = streamUri.Scheme switch
        {
            "https" => "wss",
            "http" => "ws",
            "wss" => "wss",
            "ws" => "ws",
            _ => throw new InvalidOperationException("Direct Line returned an invalid stream URL.")
        };
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(streamUri.Uri, cancellationToken);

        var activitiesUrl = $"{baseUrl}/conversations/{Uri.EscapeDataString(conversationId)}/activities";
        using var sent = await RequestAsync(HttpMethod.Post, activitiesUrl,
            new { type = "message", from = new { id = userId, role = "user" }, text = message, locale = "en-US" },
            true, cancellationToken);
        var activityId = sent.RootElement.GetProperty("id").GetString();
        DateTimeOffset? quietDeadline = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (quietDeadline is { } deadline)
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    yield break;
                }
                receiveTimeout.CancelAfter(remaining);
            }
            JsonDocument? received;
            try
            {
                received = await ReceiveAsync(socket, receiveTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            using var batch = received;
            if (batch is null || !batch.RootElement.TryGetProperty("activities", out var activities))
            {
                continue;
            }
            var completed = false;
            foreach (var activity in activities.EnumerateArray())
            {
                var type = ReadString(activity, "type");
                var sender = activity.TryGetProperty("from", out var from) ? ReadString(from, "id") : null;
                var role = from.ValueKind == JsonValueKind.Object ? ReadString(from, "role") : null;
                if (ReadString(activity, "id") == activityId || sender == userId ||
                    string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var replyToId = ReadString(activity, "replyToId");
                if (replyToId is not null && replyToId != activityId)
                {
                    continue;
                }

                if (type == "typing")
                {
                    quietDeadline = null;
                    if (onTyping is not null)
                    {
                        await onTyping(true);
                    }
                }
                else if (type == "message" && ReadString(activity, "text") is { Length: > 0 } text)
                {
                    quietDeadline = DateTimeOffset.UtcNow.AddSeconds(3);
                    if (onTyping is not null)
                    {
                        await onTyping(false);
                    }
                    yield return text;
                    completed |= ReadString(activity, "inputHint") == "acceptingInput";
                }
                completed |= type == "endOfConversation" || (type == "event" && ReadString(activity, "name") == "endOfTurn");
            }

            watermark = ReadString(batch.RootElement, "watermark") ?? watermark;
            if (completed)
            {
                yield break;
            }
        }
    }

    private static async Task<JsonDocument?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var content = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new IOException("The agent connection closed before the reply completed. Please retry.");
            }
            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidOperationException("Direct Line returned an unsupported stream message.");
            }
            if (content.Length + result.Count > 1024 * 1024)
            {
                throw new InvalidOperationException("The agent stream message exceeded the supported size.");
            }
            await content.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
        } while (!result.EndOfMessage);

        return content.Length == 0 ? null : JsonDocument.Parse(content.ToArray());
    }

    private void SetToken(JsonElement response)
    {
        token = response.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("Direct Line did not return a conversation token.");
        refreshAt = DateTimeOffset.UtcNow.AddSeconds(response.GetProperty("expires_in").GetInt32() - 60);
    }

    private async Task<JsonDocument> RequestAsync(HttpMethod method, string url, object? body,
        bool authenticated, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("direct-line");
        using var request = new HttpRequestMessage(method, url);
        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}