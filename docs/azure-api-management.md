# Azure API Management

APIM can serve the Blazor UI and route chat messages through policies. These
roles use separate APIs and settings. For the Container App ingress and
environment variables, see
[Azure Container Apps](azure-container-apps.md).

If you do not already have an APIM instance, follow Microsoft's
[APIM instance quickstart](https://learn.microsoft.com/en-us/azure/api-management/get-started-create-service-instance).

## Serve the Blazor UI

The `useAPIM` setting selects the app's UI base path. Set it to `true` to serve
the app at `/chat/`; set it to `false` to use `/`. This setting does not enable
APIM message routing.

Configure these APIs in APIM, replacing `<aca-host>` with the Container App
hostname:

| API | API URL suffix | Backend URL |
| --- | --- | --- |
| HTTP | `chat` | `https://<aca-host>/chat` |
| WebSocket | `chat/_blazor` | `wss://<aca-host>/chat/_blazor` |

The HTTP API needs root `/` and wildcard `/*` operations for `GET`, `POST`, and
`DELETE` to pass pages, static assets, and Blazor initialization requests. The
WebSocket API uses its automatic `onHandshake` operation. See Microsoft's
[WebSocket API guide](https://learn.microsoft.com/en-us/azure/api-management/websocket-api).
Preserve query
strings and cookies; do not strip the backend `/chat` prefix. For anonymous UI
access, turn **Subscription required** off on both APIs. The APIM tier must
support WebSocket APIs; Consumption does not.

Keep ACA HTTP ingress on transport Auto with target port `8080`. APIM connects
to ACA on HTTPS/WSS port 443, so it must have network access to the ingress.
Open `https://<apim-host>/chat/`, including the trailing slash. Check for HTTP
200 on the Blazor boot script and a 101 upgrade on `/chat/_blazor`. With
`useAPIM=true`, direct ACA access at `https://<aca-host>/chat/` uses the same UI
endpoint. If APIM previously used the `chat/socket` WebSocket API suffix, update
it to `chat/_blazor`.

The current chat-turn backend stores state in memory and requires one replica.
Blazor session affinity alone does not provide affinity for the separate
server-to-APIM HTTP chat requests.

## Route chat messages through policies

`ChatGateway:Enabled` selects message routing and is independent of `useAPIM`.
When enabled, each prompt and complete reply pass through APIM as JSON. The
browser still uses Blazor WebSockets for the UI; agent tokens, subscription
keys, and backend credentials are not sent to the browser. The backend uses
the local agent transport rather than calling APIM recursively.

There is no fallback to the agent when the gateway is unconfigured, rejects a
request, or fails. Keep the gateway disabled until its API and credentials are
configured. The app's current `appsettings.json` has
`ChatGateway.Enabled=false`; set it to `true` in the Container App only after
the policy setup and tests are complete.

The message path is:

```text
Blazor server -> APIM POST /chat-api/messages
               -> protected backend POST /chat/api/chat/messages
               -> Copilot Studio
               -> buffered JSON reply -> APIM outbound policies -> Blazor UI
```

### Configure the chat API

1. Import [the OpenAPI definition](../apim/chat-api.openapi.json) as a **new
   OpenAPI API**, separate from `chat-web` and `chatsocket`. Use API URL suffix
   `chat-api` and backend URL `https://<aca-host>/chat/api/chat`. The import
   creates `POST /messages` and its request/response schemas. For the APIM
   import workflow, see Microsoft's [OpenAPI import guide](https://learn.microsoft.com/en-us/azure/api-management/import-api-from-oas).
2. Leave **Subscription required** on. Create a dedicated, active API-scoped
   subscription for the Blazor server. Keep subscription requirements off on
   the public UI APIs. No chat-user login is needed.
3. Generate a separate random backend credential (at least 32 random bytes,
   encoded as 64 hex characters). Store it as the APIM **secret named value**
   `chat-backend-key` and as the Container App secret referenced by
   `ChatGateway__BackendKey`. Do not reuse the subscription key. The backend
   accepts secrets of 32 to 256 characters and rejects requests without the
   correct `X-Chat-Backend-Key` before reading the message.
4. Apply [the chat policy](../apim/apim-chat-policy.xml) at the **chat-api API
   scope**. It preserves inherited policies, requires a valid application
   subscription, limits that subscription to 30 turns per minute, validates
   input and output JSON, rejects prompts containing `policy-block-test`, and
   overwrites the backend credential header. The text rule is a test control,
   not a safety classifier.
5. Inspect effective inherited policies for this API. Existing global
   authentication can still reject the call; scope any approved exception to
   this API rather than disabling authentication across the APIM service.
   Ensure the effective backend forward-request timeout allows the 85-second
   agent operation plus network and policy overhead; avoid automatic POST
   retries.
6. Configure `ChatGateway__Endpoint` as
   `https://<apim-host>/chat-api/messages`. Set `ChatGateway__SubscriptionKey`
   and `ChatGateway__BackendKey` to Container App secret references. See
   [Container App configuration](azure-container-apps.md#configure-apim-message-routing).

These settings are intentionally not populated with secrets in
[appsettings.json](../appsettings.json). Rotate the two credentials
independently. For `useAPIM=false`, the backend URL loses its `/chat` prefix;
update the APIM backend URL accordingly. A local UI calls the configured APIM
endpoint, not an automatic local substitute.

### Test the policies

[The request collection](../apim/chat-api.http) contains manual requests for
authentication, blocking, JSON validation, a two-turn conversation, expiration,
and direct backend denial. It reads the subscription key from the REST client's
process environment; never commit credential values to this file.

1. Confirm a request without a subscription returns 401.
2. Confirm `policy-block-test` returns 403 and an APIM trace shows no backend
   call.
3. Confirm an invalid request returns 400 from content validation.
4. Send a normal message, then reuse the returned `conversationId` for a
   follow-up. Both must appear as separate APIM operations with structured
   request and response bodies. Confirm the response passes outbound validation.
5. Confirm direct backend POST without `X-Chat-Backend-Key` returns 401 after
   the backend secret is configured (503 while it is missing), with no agent
   call.

For content controls, add `choose`/`return-response` or call an inspection
service using `send-request` in inbound or outbound policies. Request-side
blocking happens before the agent runs; response-side blocking cannot undo
agent tool actions or remove a turn already recorded by Copilot Studio. The app
does not automatically retry failed POSTs. Clear the conversation after an
uncertain or expired turn.

## Proof-of-concept limits

The backend holds at most 256 conversations, expires them after 30 idle minutes,
and rejects overlapping turns for the same handle with 409. It returns 410 for
an expired or unknown handle and 503 at capacity. Clearing a conversation
forgets the UI's handle; the old backend state expires rather than being
deleted immediately. Restarts and new revisions lose backend conversation
state, so existing chats need to be cleared.

Completed replies are limited to 128 messages and 32,000 total text characters.
The backend timeout is 85 seconds; the UI still has a 90-second overall timeout.
Extra moderation calls count against that overall budget.

Conversation handles are sensitive capabilities, not authenticated user
identities. The sample rate limit applies to the server's subscription, not to
individual anonymous visitors. Do not log either credential, Direct Line
tokens, or unredacted resumes or candidate details. APIM diagnostics have
body-size and sampling limits and are not a complete transcript store. Any
audit pipeline needs explicit redaction, retention, access controls, and
correlation by conversation and turn.

References: [APIM WebSockets](https://learn.microsoft.com/en-us/azure/api-management/websocket-api),
[Blazor app base paths](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/app-base-path?view=aspnetcore-10.0),
[Content validation](https://learn.microsoft.com/en-us/azure/api-management/validate-content-policy),
[rate limiting](https://learn.microsoft.com/en-us/azure/api-management/rate-limit-by-key-policy),
and [external policy calls](https://learn.microsoft.com/en-us/azure/api-management/send-request-policy).
