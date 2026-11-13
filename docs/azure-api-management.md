# Azure API Management

Use Azure API Management (APIM) to serve the chat interface and apply policies
to chat messages. 

## Before you begin

- Create an APIM instance that supports WebSocket APIs. Follow the
  [APIM quickstart](https://learn.microsoft.com/en-us/azure/api-management/get-started-create-service-instance).
  The Consumption tier does not support WebSocket APIs.
- Deploy the chat service to Azure Container Apps (ACA). Follow the
  [Container Apps guide](azure-container-apps.md).
- Make sure APIM can reach the app's ingress over HTTPS and WSS on port 443.

## Serve the Blazor UI

Set `useAPIM=true` in the Container App to serve the interface at `/chat/`.

Create the following APIs in APIM. Replace `<aca-host>` with your Container
App's hostname:

| API | API URL suffix | Backend URL |
| --- | --- | --- |
| HTTP | `chat` | `https://<aca-host>/chat` |
| WebSocket | `chat/_blazor` | `wss://<aca-host>/chat/_blazor` |

For the HTTP API, add root `/` and wildcard `/*` operations for `GET`, `POST`,
and `DELETE`. These pass through pages, static assets, and Blazor startup
requests. The WebSocket API uses the automatically created `onHandshake`
operation. See [WebSocket APIs in APIM](https://learn.microsoft.com/en-us/azure/api-management/websocket-api).

Keep query strings, cookies, and the backend `/chat` prefix intact. Turn
**Subscription required** off for both interface APIs to allow anonymous access.
In ACA, set the ingress transport to **Auto** and the target port to `8080`.

Open `https://<apim-host>/chat/`, including the trailing slash. In your browser's
developer tools, check that the Blazor boot script returns HTTP 200 and
`/chat/_blazor` upgrades to a WebSocket with HTTP 101.

If you previously used `chat/socket` as the WebSocket API suffix, change it to
`chat/_blazor`.

Keep one backend replica. The chat-turn backend stores conversations in memory;
Blazor session affinity does not cover the separate HTTP requests through APIM.

## Route chat messages through policies

Each prompt and complete reply passes through APIM as JSON. The browser uses
Blazor WebSockets for the interface.
Agent tokens, subscription keys, and backend credentials stay on the server.
The backend connects directly to the agent.

Keep `ChatGateway:Enabled=false` until you configure and test the API. Once
enabled, gateway errors are shown to the user. The app does not bypass APIM
if a request fails or is rejected.

The message path is:

```text
Blazor server -> APIM POST /chat-api/messages
               -> protected backend POST /chat/api/chat/messages
               -> Copilot Studio
               -> buffered JSON reply -> APIM outbound policies -> Blazor UI
```

### Configure the chat API

1. Import the [OpenAPI definition](../apim/chat-api.openapi.json) as a new
   OpenAPI API. Use API URL suffix `chat-api` and
   backend URL `https://<aca-host>/chat/api/chat`. The import creates
   `POST /messages` and its request and response schemas. See the
   [OpenAPI import guide](https://learn.microsoft.com/en-us/azure/api-management/import-api-from-oas).
2. Leave **Subscription required** on for `chat-api`. Create an active
   subscription scoped to this API for the Blazor server. Chat users do not
   need subscriptions or a sign-in.
3. Generate a separate random backend credential (at least 32 random bytes,
   encoded as 64 hex characters). Store it as the APIM **secret named value**
   `chat-backend-key` and as the Container App secret referenced by
   `ChatGateway__BackendKey`. Keep this credential separate from the subscription
   key. The backend accepts secrets of 32 to 256 characters and checks
   `X-Chat-Backend-Key` before reading the message.
4. Apply [the chat policy](../apim/apim-chat-policy.xml) at the **chat-api API
   scope**. The policy preserves inherited policies, requires a valid application
   subscription, limits that subscription to 30 turns per minute, validates
   input and output JSON, rejects prompts containing `policy-block-test`, and
   sets the backend credential header. The text rule tests policy enforcement;
   it is not a safety classifier.
5. Review the API's effective policies, including inherited authentication.
   Scope any approved authentication exception to this API. Set the backend
   `forward-request` timeout to allow the 85-second agent operation plus
   network and policy overhead. Keep automatic POST retries disabled.
6. Configure `ChatGateway__Endpoint` as
   `https://<apim-host>/chat-api/messages`. Set `ChatGateway__SubscriptionKey`
   and `ChatGateway__BackendKey` to Container App secret references. See
   [Container App configuration](azure-container-apps.md#configure-apim-message-routing).

Store credentials as secrets rather than in
[appsettings.json](../appsettings.json), and rotate them independently.

### Test the policies

In APIM, open **chat-api > Test > POST /messages**. Set
`Content-Type: application/json` and supply the dedicated subscription key in
`Ocp-Apim-Subscription-Key`.

| Request | Expected result |
| --- | --- |
| `{"message":"Hello"}` without a subscription key | 401 |
| `{"message":"policy-block-test"}` with a key | 403; no backend call |
| `{"unexpected":"test"}` with a key | 400; schema rejection |
| `{"message":"Hello. Please briefly introduce yourself."}` with a key | 200; response includes `conversationId` and `messages` |

The portal may add a subscription key automatically. Remove it when testing a
request without a key. Use **Trace** to check policy execution. Keep traces
that contain credentials private.

Use the [request collection](../apim/chat-api.http) for additional tests. It
reads the subscription key from your REST client's process environment. Keep
credential values out of the file.

Check that:

- A blocked request makes no backend call.
- A normal message and a follow-up using its `conversationId` appear as
  separate APIM operations. Both responses pass outbound validation.
- A direct backend request without `X-Chat-Backend-Key` returns 401 after the
  backend secret is configured, or 503 while it is missing. Neither request
  calls the agent.

To add content controls, use `choose` and `return-response`, or call an
inspection service with `send-request` in inbound or outbound policies.
Blocking a request stops it before the agent runs. Blocking a response cannot
undo tool actions or remove a turn already recorded by Copilot Studio.

The app does not automatically retry failed POST requests. Clear the
conversation if a turn expires or you are unsure whether it completed.

### Enable chat routing

1. After the policy tests pass, set `ChatGateway__Enabled=true` in the Container
   App and deploy the revision. Keep one active backend revision with one replica.
2. Open `https://<apim-host>/chat/` and send a message to check the connection.
3. Send a message containing `policy-block-test`. The interface should show
   "The chat gateway blocked this request."

The interface shows **Waiting for agent...** until the complete reply returns.
Typing activities and partial replies are not forwarded through the gateway.

## Proof-of-concept limits

The sample backend stores conversation state in memory:

- Up to 256 conversations, each expiring after 30 idle minutes.
- One active turn per conversation. Overlapping requests return HTTP 409.
- HTTP 410 for expired or unknown conversation handles.
- HTTP 503 when conversation capacity is reached.
- Up to 128 messages and 32,000 total text characters per completed reply.

Clearing a conversation removes its handle from the interface. The old backend
state expires later. Restarts and new revisions reset backend state; clear
existing chats afterward.

The backend timeout is 85 seconds. The interface allows 90 seconds for the
entire request, including moderation calls and policy execution.

### Protect conversation data

Treat conversation handles as sensitive: they grant access to conversations,
but do not identify authenticated users. Rate limits apply to the server's
subscription, not to individual visitors.

Keep credentials, Direct Line tokens, and unredacted personal data out of logs.
APIM diagnostics limit body size and sample requests, so they are not a complete
conversation record. If you need an audit trail, define redaction, retention,
and access controls, and correlate records by conversation and turn.

## References

- [APIM WebSockets](https://learn.microsoft.com/en-us/azure/api-management/websocket-api)
- [Blazor app base paths](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/app-base-path?view=aspnetcore-10.0)
- [Content validation](https://learn.microsoft.com/en-us/azure/api-management/validate-content-policy)
- [Rate limiting](https://learn.microsoft.com/en-us/azure/api-management/rate-limit-by-key-policy)
- [External policy calls](https://learn.microsoft.com/en-us/azure/api-management/send-request-policy)
