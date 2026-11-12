# Copilot Studio Chat

This project implments a Blazor Interactive Server chat connecting to a chat server
via directline and can be configured to connect to Copiot Studio via an APIM for
policy control.

The server's default `CopilotStudioAgent:ConnectionMode` is `DirectLine` and obtains an anonymous
conversation token from `CopilotStudioAgent:TokenEndpoint`
and communicates with `CopilotStudioAgent:DirectLineEndpoint`. 

Ensure that the copilot agent has been published with No authentication and Require 
secured access off. This mode does not request an Entra token, launch a browser, or 
require a Direct Line secret in the app.

Each chat has a separate conversation state and a server-held token. Clear
conversation starts a new conversation; unused backend state expires automatically.
The agent transport sends messages with HTTPS and receives
replies and typing activities through a server-side Direct Line WebSocket. It
connects before posting a message, reconnects subsequent turns using the previous
watermark, and excludes user echoes by activity ID, sender ID, and user role.
Conversation tokens are refreshed before expiry when possible.
The text-only POC finishes a reply on an accepting-input or end-of-turn activity,
or after three quiet seconds following bot text. Typing events extend the wait.
Long pauses between multiple bot
messages can therefore leave later messages for the next request. The page
cancels a request after 90 seconds.

With `ChatGateway:Enabled=true` (the default), each prompt and complete reply pass
through APIM as JSON. The UI shows Waiting for agent until the approved response
returns; typing events and partial replies are not forwarded through this HTTP
boundary. With the gateway explicitly disabled, the original direct mode can show
Agent is typing when the agent emits a typing activity. This is not token streaming.

There is no repeated HTTP polling for replies. Token acquisition, conversation
creation or reconnection, and message submission still use HTTPS. Development
configuration suppresses routine Direct Line HTTP request logs while retaining
warnings and errors; set `Logging:LogLevel:System.Net.Http.HttpClient.direct-line`
to `Information` to restore them.

## Connection

The existing server-side SDK chat remains available by setting
`CopilotStudioAgent:ConnectionMode` to `Sdk`. The authentication and timeout
instructions below apply only to that mode.

Set `CopilotStudioAgent:EnvironmentId` and `CopilotStudioAgent:SchemaName` in
appsettings.json, or use `CopilotStudioAgent__EnvironmentId` and
`CopilotStudioAgent__SchemaName` environment variables.

By default, authentication uses `Azure.Identity.DefaultAzureCredential`. It can use an
existing developer identity or a managed identity without storing a secret.
Interactive browser authentication is disabled in this mode.

To use a service principal, set these variables in the server's environment
before starting the app:

- `AZURE_TENANT_ID`: the Microsoft Entra tenant ID.
- `AZURE_CLIENT_ID`: the application/client ID.
- `AZURE_CLIENT_SECRET`: the client secret value, provided through your secret manager.

Do not store secrets in appsettings.json or commit them to source control.
The `TenantId` and `AppClientId` settings in `CopilotStudioAgent` are used only by
the optional developer mode below, not by DefaultAzureCredential; use the standard
Azure environment variables above for application credentials.
Restart the server after changing environment variables.

The identity must be authorized to invoke the agent. Application credentials
require a Copilot Studio agent and endpoint that support application permissions;
they do not grant access by themselves. A delegated-only agent requires an
authorized user credential rather than a client secret. The SDK determines the
Power Platform token scope from its connection settings.

## Developer Sign-In

To use delegated permissions with your own app registration, set
`CopilotStudioAgent:AuthenticationMode` to `InteractiveBrowser`. This mode is
allowed only when `ASPNETCORE_ENVIRONMENT=Development`.

Configure a single-tenant Microsoft Entra app registration in the agent's tenant:

1. Under Authentication, add the Mobile and desktop applications platform with
	redirect URI `http://localhost`.
2. Under API permissions, add Power Platform API delegated permission
	`CopilotStudio.Copilots.Invoke` and obtain consent as required by your tenant.
3. Set `CopilotStudioAgent:TenantId` and `CopilotStudioAgent:AppClientId` to the
	tenant ID and your registration's Application (client) ID. Do not use the
	Azure CLI client ID or supply a client secret for this mode.

For example, from WSL Bash:

```bash
export CopilotStudioAgent__AuthenticationMode=InteractiveBrowser
export CopilotStudioAgent__TenantId=e0609078-8b1f-4b6b-a7dc-5928feee8194
export CopilotStudioAgent__AppClientId='<your-app-registration-client-id>'
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5188
```

The first chat request opens the developer's browser for sign-in. The chat page
itself has no login screen; all visitors use the signed-in developer's identity.
Run the server locally with browser access to its localhost callback. In WSL,
the Windows browser must be able to reach the WSL localhost listener, and browser
launching must be configured. Sign-in must finish within the chat's 90-second
request timeout; retry the message if it times out.

The singleton credential caches tokens in memory for the server process; a server
restart may require sign-in again. `AZURE_TOKEN_CREDENTIALS` and the service
principal environment variables do not select the identity in this mode. Azure
CLI login cannot request this permission because its Microsoft-owned client
requires API-owner preauthorization (`AADSTS65002`).

Keep this developer mode local and trusted. For production, return to `Default`
and configure an authorized application or managed identity on a supported agent
endpoint. A client secret cannot provide delegated user permissions.

## APIM Hosting

The `useAPIM` boolean is configured in [appsettings.json](appsettings.json).
Set it to `true`, or set the Container App environment variable `useAPIM=true`,
to host this app at `/chat/`, through APIM or directly. Configuration changes require an app
restart. With the switch off or absent, the app retains its root base path and
default Blazor connection setup.

When enabled, the server uses `/chat` as its path base, the page emits
`<base href="/chat/" />`, and Blazor connects to `/chat/_blazor` using WebSockets
without HTTP negotiation. There is no long-polling fallback in this mode.
The outbound server-to-agent Direct Line connection is unchanged.

Configure these APIM APIs, replacing `<aca-host>` with the Container App hostname:

| API | API URL suffix | Backend URL |
| --- | --- | --- |
| HTTP | `chat` | `https://<aca-host>/chat` |
| WebSocket | `chat/_blazor` | `wss://<aca-host>/chat/_blazor` |

The HTTP API needs root `/` and wildcard `/*` operations for GET, POST, and DELETE
to pass pages, static assets, and Blazor initialization requests. The WebSocket
API uses its automatic `onHandshake` operation. Preserve query strings and
cookies; do not strip the backend `/chat` prefix again. For the existing anonymous
POC, turn off Subscription required on both APIs. The APIM tier must support
WebSocket APIs; Consumption does not.

Keep Container Apps HTTP ingress on transport Auto with target port `8080`,
and `ASPNETCORE_HTTP_PORTS=8080`. APIM connects to ACA on HTTPS/WSS port 443.
Use one replica with the current in-memory chat-turn backend. APIM must have
network access to the ACA ingress. Blazor session affinity alone does not provide
affinity for the separate server-to-APIM HTTP chat requests.

Open `https://<apim-host>/chat/`, including the trailing slash. Check for HTTP 200
on the Blazor boot script and a 101 upgrade on `/chat/_blazor`. With `useAPIM=true`,
direct ACA access at `https://<aca-host>/chat/` uses the same UI endpoint.
Direct local access likewise uses `http://localhost:<port>/chat/`. In either case,
chat turns still go through APIM when `ChatGateway:Enabled=true`.
Use `useAPIM=false` to host at `/` instead. If APIM previously used the
`chat/socket` WebSocket API suffix, update it to `chat/_blazor`.

References: [APIM WebSockets](https://learn.microsoft.com/en-us/azure/api-management/websocket-api)
and [Blazor app base paths](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/app-base-path?view=aspnetcore-10.0).

## APIM Chat Policies

`useAPIM` only selects the UI base path. The independent `ChatGateway:Enabled`
setting selects message routing and defaults to `true`, even if omitted. There is
no fallback to the agent when the gateway is unconfigured, rejects a request, or
fails. Explicitly setting `ChatGateway:Enabled=false` restores the old direct
agent mode and bypasses message policies; do not disable it where enforcement is
required.

The enforced message path is:

```text
Blazor server -> APIM POST /chat-api/messages
				 -> protected backend POST /chat/api/chat/messages
				 -> Copilot Studio
				 -> buffered JSON reply -> APIM outbound policies -> Blazor UI
```

The browser still uses Blazor WebSockets for the UI. No agent tokens, subscription
keys, or backend credentials are sent to the browser. The backend deliberately
uses the local agent transport rather than calling APIM recursively.

### Configure APIM

1. Import [apim/chat-api.openapi.json](apim/chat-api.openapi.json) as a **new OpenAPI API**,
	separate from `chat-web` and `chatsocket`. Use API URL suffix `chat-api`.
	Confirm the backend Web service URL is
	`https://sas-chat.bluewater-93a859da.northcentralusstage.azurecontainerapps.io/chat/api/chat`.
	The import creates **POST /messages** and its request/response schemas.
2. Leave **Subscription required on for this new API**. Create a dedicated,
	active API-scoped subscription for the Blazor server. Keep subscription
	requirements off on the public UI APIs as before. No chat-user login is needed.
3. Generate a separate random backend credential using a password or secret
	manager (at least 32 random bytes, encoded as 64 hex characters). Store it as
	the APIM **secret named value** `chat-backend-key` and as the Container App
	secret referenced by `ChatGateway__BackendKey`. Do not reuse the subscription
	key. The backend accepts secrets of 32 to 256 characters and rejects requests
	without the correct `X-Chat-Backend-Key` before reading the message.
4. Apply [apim/apim-chat-policy.xml](apim/apim-chat-policy.xml) at the **chat-api API scope**.
	It preserves inherited policies, requires a valid application subscription,
	limits that subscription to 30 turns per minute, validates input/output JSON,
	rejects prompts containing `policy-block-test`, and overwrites the backend
	credential header. The text rule is a test control, not a safety classifier.
5. Inspect effective inherited policies for this API, too. Existing global
	authentication can still reject the call; scope any approved exception to
	this API rather than disabling authentication across the APIM service.
	Ensure the effective backend forward-request timeout allows the 85-second
	agent operation plus network/policy overhead; avoid automatic POST retries.
6. Configure these server environment variables using Container App secret
	references for both credentials, then rebuild/redeploy the application:

| Environment Variable | Value |
| --- | --- |
| `ChatGateway__Enabled` | `true` |
| `ChatGateway__Endpoint` | `https://nvmtr2apim.azure-api.net/chat-api/messages` |
| `ChatGateway__SubscriptionKey` | The dedicated APIM subscription key, from a secret reference |
| `ChatGateway__BackendKey` | The same secret used by APIM's `chat-backend-key` named value |

These settings are intentionally not populated with secrets in
[appsettings.json](appsettings.json). Until configured, chat fails closed with a
configuration or authentication error. Rotate the two credentials independently;
managed identity is a future alternative to this server-side subscription key.
For `useAPIM=false`, the backend URL loses its `/chat` prefix; update the APIM
backend URL accordingly. A local UI calls the configured APIM endpoint, not an
automatic local substitute.

### Policy Checks

[apim/chat-api.http](apim/chat-api.http) contains manual requests for authentication,
blocking, JSON validation, a two-turn conversation, expiration, and direct backend
denial. It reads the subscription key from the REST client's process environment;
never commit credential values to this file. Configure and deploy first, then:

1. Confirm a request without a subscription returns 401.
2. Confirm `policy-block-test` returns 403 and an APIM trace shows no backend call.
3. Confirm an invalid request returns 400 from content validation.
4. Send a normal message, then reuse the returned `conversationId` for a follow-up.
	Both must appear as separate APIM operations with structured request/response
	bodies. Confirm the response passes outbound validation before display.
5. Confirm direct backend POST without `X-Chat-Backend-Key` returns 401 after the
	backend secret is configured (503 while it is missing), with no agent call.

For content controls, add `choose`/`return-response` or call an inspection service
using `send-request` in inbound/outbound policies. Request-side blocking happens
before the agent runs; response-side blocking cannot undo agent tool actions or
remove a turn already recorded by Copilot Studio. The app does not automatically
retry failed POSTs. Clear the conversation after an uncertain or expired turn.

### POC Limits

The backend holds at most 256 conversations, expires them after 30 idle minutes,
and rejects overlapping turns for the same handle with 409. It returns 410 for an
expired/unknown handle and 503 at capacity. Clear forgets the UI's handle; its old
backend state expires rather than being deleted immediately. Restarts and new
revisions lose backend conversation state, so existing chats need to be cleared.

**Use a single backend replica.** Durable conversation ownership/routing is needed
before scaling this implementation across replicas; Blazor affinity is not enough.
Completed replies are limited to 128 messages and 32,000 total text characters.
The backend timeout is 85 seconds; the UI still has a 90-second overall timeout.
Extra moderation calls count against that overall budget.

Conversation handles are sensitive capabilities, not authenticated user identities.
The sample rate limit applies to the server's subscription, not to individual
anonymous visitors. Do not log either credential, Direct Line tokens, or unredacted
resumes/candidate details. APIM diagnostics have body-size and sampling limits and
are not a complete transcript store. Any audit pipeline needs explicit redaction,
retention, access controls, and correlation by conversation/turn.

References: [Content validation](https://learn.microsoft.com/en-us/azure/api-management/validate-content-policy),
[rate limiting](https://learn.microsoft.com/en-us/azure/api-management/rate-limit-by-key-policy),
and [external policy calls](https://learn.microsoft.com/en-us/azure/api-management/send-request-policy).

## Run

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5188
```

Agent text is rendered as plain text, not executable HTML. Request errors are
shown without exposing credentials; detailed errors are logged on the server.
There is a 90-second request timeout. Since the page is anonymous, keep this
demo on a trusted network; add access controls and usage limits before exposing
it publicly with a shared application identity.