using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using gdlchat.Services;
using Microsoft.AspNetCore.Http.Features;

namespace gdlchat.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        app.MapPost("/api/chat/messages", SendAsync)
            .WithName("CreateChatTurn")
            .WithSummary("Submit a message through the protected chat backend")
            .WithDescription("Requires the APIM-injected X-Chat-Backend-Key. Returns a complete, buffered JSON turn.")
            .Accepts<CreateChatTurnRequest>("application/json")
            .Produces<ChatTurnResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(409)
            .ProducesProblem(410)
            .ProducesProblem(413)
            .ProducesProblem(415)
            .ProducesProblem(502)
            .ProducesProblem(503)
            .ProducesProblem(504);
    }

    private static async Task<IResult> SendAsync(
        HttpContext context,
        IConfiguration configuration,
        IChatTurnService turns,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var backendKey = configuration["ChatGateway:BackendKey"];
        if (backendKey is null || backendKey.Length is < 32 or > 256)
        {
            return TypedResults.Problem(statusCode: 503, title: "The chat backend credential is not configured.");
        }

        var supplied = context.Request.Headers["X-Chat-Backend-Key"];
        if (supplied.Count != 1 || supplied[0] is not { Length: >= 32 and <= 256 } provided ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(backendKey)),
                SHA256.HashData(Encoding.UTF8.GetBytes(provided))))
        {
            return TypedResults.Problem(statusCode: 401, title: "The chat backend requires a valid gateway credential.");
        }

        if (!context.Request.HasJsonContentType())
        {
            return TypedResults.Problem(statusCode: 415, title: "Content-Type must be application/json.");
        }
        if (context.Request.ContentLength > 32768)
        {
            return TypedResults.Problem(statusCode: 413, title: "The request body is too large.");
        }
        var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
        {
            bodyLimit.MaxRequestBodySize = 32768;
        }

        CreateChatTurnRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<CreateChatTurnRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.Problem(statusCode: 400, title: "The request must contain a valid chat message.");
        }
        catch (BadHttpRequestException exception)
        {
            return TypedResults.Problem(statusCode: exception.StatusCode, title: "The request body could not be read.");
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000 ||
            request.ConversationId == Guid.Empty)
        {
            return TypedResults.Problem(statusCode: 400, title: "Provide a message of 1 to 4000 characters and a valid conversation handle.");
        }

        try
        {
            var response = await turns.SendAsync(request, cancellationToken);
            context.Response.Headers["X-Conversation-Id"] = response.ConversationId.ToString();
            return TypedResults.Ok(response);
        }
        catch (ChatTurnException exception)
        {
            return TypedResults.Problem(statusCode: exception.StatusCode, title: exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TypedResults.Problem(statusCode: 504, title: "The agent took too long to respond. Start a new conversation.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("ChatBackend").LogError("Chat turn failed with {ExceptionType}.", exception.GetType().Name);
            return TypedResults.Problem(statusCode: 502, title: "The agent request failed. Start a new conversation.");
        }
    }
}