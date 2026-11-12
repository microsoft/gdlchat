# Build, Publish, and Connect a Copilot Studio Agent

This guide covers how to publish a Copilot Studio agent and connect it to the
.NET 10 Blazor service. The example uses anonymous access. The browser does not
call Copilot Studio directly, and the application does not embed an iframe.

## 1. Prerequisites

- Access to Copilot Studio and the environment where you can create and publish agents.
- Access to any tools, connectors, or Dataverse tables your agent needs.
- The .NET 10 SDK in the environment where you run the service.
- Outbound HTTPS and secure WebSocket access from the .NET server to Power Platform and Direct Line.

Agent authoring requires your Microsoft account. Anonymous access below applies
to people chatting with the published agent, not to the maker portal.

## 2. Build the Agent

1. Open Copilot Studio and select the intended environment.
2. Create an agent. UI labels can differ between authoring experiences and
   releases.
3. Give the agent a name, description, and instructions. Select an available
   model appropriate for the work; this service does not depend on a particular model.
4. Describe what the agent should do, what data it can use, and what it should
   avoid doing.
5. Add the tools and connections required for your scenario. Dataverse MCP
   tools, skills, and connected agents are examples of agent capabilities, not
   requirements of this chat service.
6. Configure each tool's connection and permissions. Anonymous chat does not
   remove authentication requirements on Dataverse or other downstream systems.
   If a tool requires an end-user sign-in, that tool may still prompt or fail;
   verify its behavior separately with the published anonymous agent.
7. Test the agent in Copilot Studio. Start with a simple capability question,
   then test the tools you configured. Save the agent.

Example instructions for a small first test:

```text
You are the Hiring Agent. Help users understand and coordinate recruitment.
Use configured tools when actual records are needed. Do not invent record IDs
or claim that a record was changed unless a tool confirms it. For general
capability questions, answer briefly without retrieving or modifying records.
```

## 3. Enable Anonymous Chat

The settings may appear in different locations depending on the authoring
experience:

1. Open **Agent settings**.
2. Select **Safety & access**.
3. Set **Authentication** to **No authentication**.
4. Open **Web channel security** on the same settings page.
5. Leave **Require secured access** off. You do not need to copy either of the
   displayed Direct Line secrets for the anonymous setup used here.
6. Select **Done** or save changes, then **publish the agent**. If Done is
   disabled because the settings are already saved, close the dialog and publish.

In the standard authoring experience, the authentication settings can instead
appear under **Settings > Security > Authentication**. Use the labels present
in your experience rather than looking for the older navigation in the preview UI.

Authentication changes take effect after publishing. This configuration lets
anyone with access to the agent's anonymous channel interact with it.

## 4. Identify the Agent

From the agent's publishing options, copy its generated website embed code or
hosted webchat URL. Use the URL to identify the environment and agent schema
name. The following URL illustrates the format:

```text
https://copilotstudio.preview.microsoft.com/environments/577b4755-cf12-e3f2-962f-a5528e11fa0c/bots/ppa_hiringagent_yRV7uI/webchat?__version__=2&enableFileAttachment=false&cliAgent=true
```

The URL identifies:

| Field | Example value |
| --- | --- |
| Environment ID | `577b4755-cf12-e3f2-962f-a5528e11fa0c` |
| Agent schema name | `ppa_hiringagent_yRV7uI` |
| Display name | Hiring Agent |

The schema name is not the display name or the Dataverse bot record GUID. Use
the value after `/bots/` in the generated URL.

The hosted webchat is useful for discovering and testing the connection. Do not
paste its iframe into this project: the requirement here is server-side C# access.

## 5. Capture the Anonymous Endpoints

This is the route we used to find the working endpoints without guessing preview
portal navigation or assuming the older SDK endpoint applies:

1. Open the published hosted webchat URL in a browser.
2. Open browser developer tools and select **Network** before reloading the page.
3. Filter requests by `directline/token`. Copy the full **Request URL**, including
   its `api-version` query parameter. This is the service's `TokenEndpoint`.
4. Find the request to `/v3/directline/conversations`. Its origin plus
   `/v3/directline` is the service's `DirectLineEndpoint`. Do not include an
   individual conversation ID, `/activities`, or the WebSocket stream URL.
5. Confirm the hosted chat answers a simple message without a Microsoft sign-in prompt.

For this example agent, the endpoints are:

```text
TokenEndpoint:
https://577b4755cf12e3f2962fa5528e11fa.0c.environment.api.powerplatform.com/copilotstudio/agenticruntime/botsbyschema/ppa_hiringagent_yRV7uI/directline/token?api-version=2022-03-01-preview

DirectLineEndpoint:
https://unitedstates.directline.botframework.com/v3/directline
```

The hosted page also requested `/powervirtualagents/regionalchannelsettings` on
the environment host. Its `channelUrlsById.directline` value identifies the
regional Direct Line base. Use the actual region returned for your agent; do not
assume every agent uses `unitedstates`.

Copy endpoint URLs, not the token returned in a response. The .NET service obtains
fresh conversation tokens itself. Do not hard-code a captured token or a
preauthorized WebSocket stream URL. Likewise, do not construct the environment
API hostname by simply removing hyphens from the environment ID; use the observed host.

## 6. Configure This Service

Update the `CopilotStudioAgent` section in [appsettings.json](appsettings.json).
The following configuration shows example connection values:

```json
{
  "CopilotStudioAgent": {
    "ConnectionMode": "DirectLine",
    "TokenEndpoint": "https://577b4755cf12e3f2962fa5528e11fa.0c.environment.api.powerplatform.com/copilotstudio/agenticruntime/botsbyschema/ppa_hiringagent_yRV7uI/directline/token?api-version=2022-03-01-preview",
    "DirectLineEndpoint": "https://unitedstates.directline.botframework.com/v3/directline",
    "EnvironmentId": "577b4755-cf12-e3f2-962f-a5528e11fa0c",
    "SchemaName": "ppa_hiringagent_yRV7uI",
    "AuthenticationMode": "Default"
  }
}
```

Merge this section into the existing file; keep its other settings. For another
agent, replace the endpoint URLs and identifiers with the values you captured.

In `DirectLine` mode, `TokenEndpoint` and `DirectLineEndpoint` drive the connection.
`EnvironmentId` and `SchemaName` are retained for the optional authenticated SDK
path; they do not automatically update the anonymous endpoints.

Keep `AuthenticationMode` set to `Default` so startup does not select the earlier
development browser-sign-in option. `TenantId`, `AppClientId`, Azure CLI login,
and `AZURE_CLIENT_SECRET` are not required for this anonymous Direct Line path.
Existing tenant/client ID settings may remain; Direct Line does not use them.

You can override the connection in the server's environment instead. For WSL Bash:

```bash
export CopilotStudioAgent__ConnectionMode=DirectLine
export CopilotStudioAgent__AuthenticationMode=Default
export CopilotStudioAgent__TokenEndpoint='<captured anonymous token endpoint URL>'
export CopilotStudioAgent__DirectLineEndpoint='<captured regional base ending in /v3/directline>'
```

Environment variables override the JSON configuration. Check for values left over
from an earlier experiment, and restart the service after changing configuration.

## 7. Build and Run

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

Use a free port; do not stop an unrelated process to reuse one. The SDK and
interactive-browser authentication setup documented in [README.md](README.md)
is an alternative mode, not a prerequisite for this anonymous connection.

## 8. Verify the Connection

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

## 9. How It Works

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

The POC renders plain-text replies, not rich cards or token-by-token text
streaming. It ends a turn on a recognized completion activity or after three
quiet seconds following bot text; typing events extend the wait. Longer pauses
between multiple bot messages can leave later messages for the next request.

## 10. Troubleshooting

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
| Package vulnerability warnings appear during build | They are dependency advisories, not agent-authentication failures. The existing warnings were not resolved as part of this connection setup. |

## 11. Build and Push a Container

The [Dockerfile](Dockerfile) builds with the .NET 10 SDK and publishes into an
ASP.NET 10 runtime image. The app runs as the image's non-root user, in Production,
listening on HTTP port `8080`. [.dockerignore](.dockerignore) excludes local build
output, development settings, and common secret files from the build context.

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

To build and push in Azure Container Registry without requiring local Docker,
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

## 12. Configure the APIM chat gateway

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

Confirm that `POST /messages` appears. Leave `chat-web` and `chatsocket`
unchanged.

### 2. Create the backend secret

Generate a random 64-character hexadecimal secret with a password or secret
manager. In APIM, add a secret named value called `chat-backend-key`. In the
Container App, create a secret with the same name and value. Do not paste the
secret into documentation or commit it to source control.

### 3. Configure the subscription

In APIM, create an active subscription scoped to the `chat-api` API. Store its
primary key as a separate Container App secret named
`chat-apim-subscription-key`. Do not reuse the backend secret.

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
“The chat gateway blocked this request.” Typing indicators are not displayed in
gateway mode because APIM returns completed replies before the UI displays them.

## References

- [Copilot Studio user authentication](https://learn.microsoft.com/en-us/microsoft-copilot-studio/configuration-end-user-authentication)
- [Direct Line WebSocket activities and typing support](https://learn.microsoft.com/en-us/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-receive-activities?view=azure-bot-service-4.0)
- [Direct Line conversation reconnection](https://learn.microsoft.com/en-us/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-reconnect-to-conversation?view=azure-bot-service-4.0)
