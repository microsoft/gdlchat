# Build, Run, and Deploy the Chat Service

This guide covers building and running the .NET service, building its container,
and configuring the Azure API Management (APIM) chat gateway. To create
and publish an agent and configure its Direct Line connection, see
[Copilot Studio setup](docs/copilot-studio-setup.md).

## Prerequisites

- The .NET 10 SDK for local development.
- Outbound HTTPS and secure WebSocket access from the .NET server to Power
  Platform and Direct Line.
- Docker, if building and running the container locally.

## Build and Run

From the project directory in WSL:

```bash
dotnet restore
dotnet build
dotnet run --launch-profile http
```

The current [launch profile](Properties/launchSettings.json) runs in Development
at **http://localhost:5093/**. To select a different local port explicitly:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5191
```

Choose a free port. Don't stop an unrelated process to free one up.

The authenticated SDK mode and browser sign-in flow are described under
[Connection](README.md#connection) and
[Developer Sign-In](README.md#developer-sign-in); neither is needed for the
default Direct Line setup.

## Verify the Connection

1. Open the local service URL. The home page should show the native chat controls,
   not an iframe or the hosted Copilot Studio page.
2. Send: `Briefly describe your hiring capabilities. Do not retrieve or modify any records.`
3. Confirm **Waiting for agent...** appears immediately. If the agent sends a
   typing activity, the UI changes to **Agent is typing...** with animated dots.
4. Confirm the reply describes the agent's capabilities rather than repeating
   your outgoing message as an agent reply.
5. Send: `What was my previous question? Do not retrieve or modify any records.`
   Confirm it remembers the first question.
6. Select **Clear conversation**. The next message should start a new conversation.
7. In your local page's browser Network panel, confirm there are no direct requests
   to Power Platform or Direct Line. Those requests belong to the .NET server.

The verification steps confirm that the service returns an agent reply, retains
conversation context, and displays typing activity when the agent sends it.

## How It Works

```text
Browser chat
    | Blazor SignalR connection, normally a WebSocket
    v
.NET server / CopilotChatClient
    | HTTPS: obtain token, create or reconnect conversation, post message
    | Server-side WebSocket: receive replies and typing activities
    v
Regional Direct Line service
    v
Published Copilot Studio agent
```

Relevant code:

- [Components/Pages/Home.razor](Components/Pages/Home.razor): chat UI, typing state, errors, and 90-second request cancellation.
- [Services/CopilotChatClient.cs](Services/CopilotChatClient.cs): selects the Direct Line or authenticated SDK path.
- [Services/DirectLineChatClient.cs](Services/DirectLineChatClient.cs): server-held tokens, HTTPS calls, WebSocket reception, echo filtering, and conversation state.
- [Program.cs](Program.cs): registers the clients and Blazor services.

Each chat has separate conversation state. Tokens stay on the server. Subsequent
turns obtain a fresh stream URL using the previous watermark. There is no
one-second HTTP polling loop for replies.

The chat renders plain-text replies, not rich cards or token-by-token text
streaming. It ends a turn on a recognized completion activity or after three
quiet seconds following bot text; typing events extend the wait. Longer pauses
between multiple bot messages can leave later messages for the next request.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| A Microsoft sign-in or WSL browser-launch error appears | Confirm `ConnectionMode=DirectLine` and `AuthenticationMode=Default`, including environment overrides. Restart after changing them. |
| `AADSTS65002` or missing `CopilotStudio.Copilots.Invoke` | Those errors came from the authenticated SDK/Entra route. The anonymous route uses Direct Line conversation tokens instead. |
| The agent works in Studio but not in the service | Publish the current agent, verify anonymous settings, and recapture both endpoint URLs from the published hosted chat. Check tool connection requirements separately. |
| Your own text appears as an agent reply | Run the current build. The client excludes user echoes by sent activity ID, sender ID, and user role. Clear the conversation after restarting. |
| Many GET requests appear every second | That was the earlier HTTP-polling implementation. Restart with the current WebSocket implementation. HTTPS setup, reconnect, refresh, and message POST requests are still expected. |
| No typing indicator appears | Some agents do not emit typing activities. Waiting for agent remains visible until a response; it does not fabricate typing events. |
| A request times out | The page cancels after 90 seconds. Check agent execution, tool connections, and server outbound WebSocket connectivity. |
| Routine HTTP logs are too verbose | Development settings filter `System.Net.Http.HttpClient.direct-line` to `Warning`. Set that category to `Information` when diagnosing HTTP setup failures. |
| Failed to determine the HTTPS port | The HTTP-only local run has no HTTPS redirect target. This warning is separate from Direct Line authentication or message delivery. |
| Package vulnerability warnings appear during build | These are dependency advisories, not agent authentication failures. This connection setup doesn't resolve them. |

## Build and Push a Container

The [Dockerfile](Dockerfile) builds with the .NET 10 SDK and publishes into an
ASP.NET 10 runtime image. The app runs as the image's non-root user, in Production,
listening on HTTP port `8080`. There is no `.dockerignore` file, so review the
repository build context before building and keep secrets and unrelated files
out of the repository directory.

To build and test locally, from this project directory:

```bash
docker build --tag copilot-chat:latest .
docker run --rm --publish 8080:8080 copilot-chat:latest
```

Open `http://localhost:8080/`. If host port 8080 is occupied, use a different host
port, for example `--publish 8081:8080`. The existing anonymous Direct Line settings
are included through appsettings.json. Use `--env CopilotStudioAgent__TokenEndpoint=...`
and the other configuration variables to point the container at a different agent.
Do not pass secrets as Docker build arguments or put them in the Dockerfile.

To build and push in Azure Container Registry (ACR) without requiring local Docker,
sign in with Azure CLI and select the subscription containing your existing
registry. Set `ACR` to the registry resource name, not its login-server hostname,
and `ACRGROUP` to its resource group. Then run:

```bash
az acr build \
   --registry "$ACR" \
   --resource-group "$ACRGROUP" \
   --image copilot-chat:latest \
   --platform linux/amd64 \
   .
```

This uploads the current build context, builds the image in ACR, and pushes it to
the registry. It does not create the registry or deploy a running service. Your
Azure identity needs permission to run builds and publish images in that registry.

For deployment, configure your container host to target port `8080`, terminate
HTTPS at its ingress, and support WebSockets for Blazor SignalR. The container
also needs outbound HTTPS and WSS access to the agent services. Conversation
state is held in the app process, so a restart starts fresh chats; multi-instance
Blazor hosting requires session affinity. Hosting infrastructure is not created
by this Dockerfile or the registry build command.

## Configure the APIM chat gateway

Replace `<aca-host>` and `<apim-host>` with the hostnames for your Container
App and APIM instance.

### 1. Import the chat API

In APIM, select **APIs > Add API > OpenAPI** and import
`apim/chat-api.openapi.json`.

| Field | Value |
| --- | --- |
| Name | `chat-api` |
| API URL suffix | `chat-api` |
| Subscription required | On |
| Web service URL | `https://<aca-host>/chat/api/chat` |

Confirm that `POST /messages` appears. This API handles chat messages. UI
routing is configured separately.

### 2. Create the backend secret

Generate a random 64-character hexadecimal secret with a password or secret
manager. In APIM, add a secret named value called `chat-backend-key`. In the
Container App, create a secret with the same name and value. Do not paste the
secret into documentation or commit it to source control.

### 3. Configure the subscription

In APIM, create an active subscription scoped to the `chat-api` API. Store its
primary key as a separate Container App secret named
`chat-apim-subscription-key`. Do not reuse the backend secret. Chat users do not
need APIM subscriptions.

### 4. Apply the policy

In APIM, open **chat-api > Design > All operations > policy editor** and apply
`apim/apim-chat-policy.xml`. The policy validates requests and responses,
limits subscriptions to 30 turns per minute, and rejects requests containing
`policy-block-test`.

If inherited authentication rejects requests, inspect the effective policy.
Do not disable authentication globally.

### 5. Configure the Container App

Set these Container App environment variables:

| Variable | Value |
| --- | --- |
| `ChatGateway__BackendKey` | Secret reference: `chat-backend-key` |
| `ChatGateway__SubscriptionKey` | Secret reference: `chat-apim-subscription-key` |
| `ChatGateway__Endpoint` | `https://<apim-host>/chat-api/messages` |
| `ChatGateway__Enabled` | `false` initially |

Apply the revision. Keep `useAPIM=true` and use one active backend revision with
one replica for this proof of concept.

### 6. Test the API policies

In APIM, open **chat-api > Test > POST /messages**. Set
`Content-Type: application/json` and use the dedicated subscription key in
`Ocp-Apim-Subscription-Key`.

| Request | Expected result |
| --- | --- |
| `{"message":"Hello"}` without a subscription key | 401 |
| `{"message":"policy-block-test"}` with a key | 403; no backend call |
| `{"unexpected":"test"}` with a key | 400; schema rejection |
| `{"message":"Hello. Please briefly introduce yourself."}` with a key | 200; response includes `conversationId` and `messages` |

The portal may add a subscription key automatically. Remove it when testing
the unauthenticated request. For the blocked request, select **Trace** and
confirm that APIM does not call the backend. For a normal request, confirm
that outbound validation runs. Do not share traces that contain credentials.

Reuse the returned `conversationId` in another request to test conversation
continuity. Additional requests are in `apim/chat-api.http`.

### 7. Enable chat routing

After the policy tests pass, set `ChatGateway__Enabled=true` in the Container
App and apply the new revision. Refresh the chat page and send a normal message
to verify the response. A message containing `policy-block-test` should display
“The chat gateway blocked this request.” Typing indicators don't appear in
gateway mode, because APIM returns each reply only after it's complete.

## References

- [Copilot Studio user authentication](https://learn.microsoft.com/en-us/microsoft-copilot-studio/configuration-end-user-authentication)
- [Direct Line WebSocket activities and typing support](https://learn.microsoft.com/en-us/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-receive-activities?view=azure-bot-service-4.0)
- [Direct Line conversation reconnection](https://learn.microsoft.com/en-us/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-reconnect-to-conversation?view=azure-bot-service-4.0)
