using Microsoft.Extensions.Caching.Memory;

namespace gdlchat.Services;

/// <summary>Submits one message, optionally continuing an existing conversation.</summary>
public sealed record CreateChatTurnRequest
{
    public Guid? ConversationId { get; init; }
    public required string Message { get; init; }
}

/// <summary>Contains the conversation handle and all completed replies for one turn.</summary>
public sealed record ChatTurnResponse(Guid ConversationId, string[] Messages);

public interface IChatTurnService
{
    Task<ChatTurnResponse> SendAsync(CreateChatTurnRequest request, CancellationToken cancellationToken);
}

public sealed class ChatTurnService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<CopilotChatClient> logger) : IChatTurnService, IDisposable
{
    private readonly MemoryCache conversations = new(new MemoryCacheOptions { SizeLimit = 256 });

    public async Task<ChatTurnResponse> SendAsync(CreateChatTurnRequest request, CancellationToken cancellationToken)
    {
        var conversationId = request.ConversationId ?? Guid.NewGuid();
        Conversation conversation;
        if (request.ConversationId.HasValue)
        {
            if (!conversations.TryGetValue(conversationId, out Conversation? existing) || existing is null)
            {
                throw new ChatTurnException(410, "This conversation expired. Start a new conversation.");
            }
            conversation = existing;
        }
        else
        {
            conversation = new Conversation(new CopilotChatClient(configuration, httpClientFactory, logger));
            conversations.Set(conversationId, conversation, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = TimeSpan.FromMinutes(30),
                Priority = CacheItemPriority.NeverRemove
            });
            if (!conversations.TryGetValue(conversationId, out Conversation? stored) || !ReferenceEquals(stored, conversation))
            {
                throw new ChatTurnException(503, "The chat backend is at conversation capacity. Please try later.");
            }
        }

        if (!await conversation.Gate.WaitAsync(0, cancellationToken))
        {
            throw new ChatTurnException(409, "Another message is already running for this conversation.");
        }

        try
        {
            if (!conversations.TryGetValue(conversationId, out Conversation? current) || !ReferenceEquals(current, conversation))
            {
                throw new ChatTurnException(410, "This conversation expired. Start a new conversation.");
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(85));
            var replies = new List<string>();
            long replyLength = 0;
            await foreach (var reply in conversation.Client.SendToAgentAsync(request.Message, timeout.Token))
            {
                replyLength += reply.Length;
                if (replyLength > 32000 || replies.Count >= 128)
                {
                    throw new ChatTurnException(502, "The agent reply exceeded the supported size.");
                }
                replies.Add(reply);
            }
            if (replies.Count == 0)
            {
                throw new ChatTurnException(502, "The agent did not return a text reply.");
            }
            return new ChatTurnResponse(conversationId, replies.ToArray());
        }
        catch
        {
            conversations.Remove(conversationId);
            throw;
        }
        finally
        {
            conversation.Gate.Release();
        }
    }

    public void Dispose() => conversations.Dispose();

    private sealed record Conversation(CopilotChatClient Client)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}

public sealed class ChatTurnException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}