using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Azure.Core;
using Microsoft.Agents.CopilotStudio.Client;

namespace gdlchat.Services;

public sealed class CopilotChatClient(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<CopilotChatClient> logger)
{
    private CopilotClient? client;
    private readonly DirectLineChatClient directLine = new(configuration, httpClientFactory);
    private bool conversationStarted;
    private Guid? gatewayConversationId;

    public void ResetConversation()
    {
        client = null;
        directLine.ResetConversation();
        conversationStarted = false;
        gatewayConversationId = null;
    }

    public async IAsyncEnumerable<string> SendAsync(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        Func<bool, Task>? onTyping = null)
    {
        if (configuration.GetValue<bool>("ChatGateway:Enabled", true))
        {
            var turn = await SendViaGatewayAsync(message, cancellationToken);
            foreach (var reply in turn.Messages)
            {
                yield return reply;
            }
            yield break;
        }

        await foreach (var reply in SendToAgentAsync(message, cancellationToken, onTyping))
        {
            yield return reply;
        }
    }

    private async Task<ChatTurnResponse> SendViaGatewayAsync(string message, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(configuration["ChatGateway:Endpoint"], UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ChatGatewayException("Configure ChatGateway:Endpoint with the HTTPS APIM messages URL.");
        }

        var subscriptionKey = configuration["ChatGateway:SubscriptionKey"];
        if (string.IsNullOrWhiteSpace(subscriptionKey))
        {
            throw new ChatGatewayException("Configure the server-side ChatGateway:SubscriptionKey credential.");
        }

        using var gateway = httpClientFactory.CreateClient("chat-gateway");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new CreateChatTurnRequest
            {
                ConversationId = gatewayConversationId,
                Message = message
            })
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
        using var response = await gateway.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = (int)response.StatusCode switch
            {
                401 => "The chat gateway rejected the server credentials.",
                403 => "The chat gateway blocked this request.",
                409 => "Another message is already running for this conversation.",
                410 => "This conversation expired. Clear the conversation to start again.",
                429 => "The chat rate limit was reached. Please wait before sending again.",
                _ => $"The chat gateway could not complete the request (HTTP {(int)response.StatusCode})."
            };
            throw new ChatGatewayException(detail);
        }

        ChatTurnResponse? turn;
        try
        {
            turn = await response.Content.ReadFromJsonAsync<ChatTurnResponse>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ChatGatewayException("The chat gateway returned an invalid response.");
        }
        if (turn is null || turn.ConversationId == Guid.Empty || turn.Messages is not { Length: > 0 and <= 128 } ||
            turn.Messages.Any(string.IsNullOrWhiteSpace) || turn.Messages.Sum(reply => (long)reply.Length) > 32000)
        {
            throw new ChatGatewayException("The chat gateway returned an invalid response.");
        }

        gatewayConversationId = turn.ConversationId;
        return turn;
    }

    internal async IAsyncEnumerable<string> SendToAgentAsync(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Func<bool, Task>? onTyping = null)
    {
        if (string.Equals(configuration["CopilotStudioAgent:ConnectionMode"], "DirectLine", StringComparison.OrdinalIgnoreCase))
        {
            await foreach (var reply in directLine.SendAsync(message, cancellationToken, onTyping))
            {
                yield return reply;
            }
            yield break;
        }

        if (client is null)
        {
            var settings = new ConnectionSettings(configuration.GetSection("CopilotStudioAgent"));
            if (string.IsNullOrWhiteSpace(settings.DirectConnectUrl) &&
                (string.IsNullOrWhiteSpace(settings.EnvironmentId) || string.IsNullOrWhiteSpace(settings.SchemaName)))
            {
                throw new InvalidOperationException("Configure CopilotStudioAgent:EnvironmentId and CopilotStudioAgent:SchemaName.");
            }

            client = new CopilotClient(settings, httpClientFactory, logger, "copilot-studio");
        }

        if (!conversationStarted)
        {
            await foreach (var activity in client.StartConversationAsync(false, cancellationToken))
            {
                if (activity.Type == "message" && !string.IsNullOrWhiteSpace(activity.Text))
                {
                    yield return activity.Text;
                }
            }

            conversationStarted = true;
        }

        await foreach (var activity in client.AskQuestionAsync(message, null, cancellationToken))
        {
            if (activity.Type == "message" && !string.IsNullOrWhiteSpace(activity.Text))
            {
                yield return activity.Text;
            }
        }
    }
}

public sealed class ChatGatewayException(string message) : Exception(message);

internal sealed class CopilotStudioTokenHandler(IConfiguration configuration, TokenCredential credential) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = new ConnectionSettings(configuration.GetSection("CopilotStudioAgent"));
        var scope = CopilotClient.ScopeFromSettings(settings);
        if (!scope.EndsWith("/.default", StringComparison.OrdinalIgnoreCase))
        {
            scope = scope.TrimEnd('/') + "/.default";
        }

        var token = await credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return await base.SendAsync(request, cancellationToken);
    }
}