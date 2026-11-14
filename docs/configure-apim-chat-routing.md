# Configure APIM Chat Routing

Configure the deployed chat app so each chat message and reply passes through APIM policies

## Prerequisites

- Azure CLI installed and signed in to your Azure subscription.
- Access to the Azure portal with permission to configure APIM and Container Apps.
- A deployed [Container App](azure-container-apps.md) connected to the published Copilot Studio agent.
- The three APIs already created in APIM using the [Azure API Management guide](azure-api-management.md), ready to connect to the deployed Container App.
- An active subscription scoped to the `chat-api` API, with its subscription key copied.
- A separate random backend key generated and copied for storage in APIM and ACA.

## Routing summary

Recall that we have these three API's and they have already been configured in the APIM.

| Route | Protocol | Purpose | Required keys |
| --- | --- | --- | --- |
| `/chat/` and `/chat/*` | HTTPS | Load the chat interface and assets. | None |
| `/chat/_blazor` | WSS | Connect the browser to the Blazor server. | None |
| `/chat-api/messages` | HTTPS | Route chat turns from the Blazor server through APIM to the ACA backend. | **To&nbsp;APIM:**&nbsp;Subscription&nbsp;key<br>**To ACA:**&nbsp;Backend key |


## Configure APIM message routing

Configure the Container App with the APIM credentials and gateway endpoint,
then enable routing after the policy tests pass.

#### Subscription key

Look up the existing API-scoped subscription key created in the APIM guide:

1. In the Azure portal, open your API Management instance and select **Subscriptions**.
2. Find the subscription scoped to the `chat-api` API and confirm its state is **Active**.
3. Open the subscription's **...** menu and select **Show/hide keys**.
4. Copy the **Primary key** value, not the subscription ID. Either primary or secondary key is valid; use one consistently.
5. In your Container App, open **Secrets** and store the copied value as `chat-apim-subscription-key`, as described below. Keep it out of source control and shell history.

#### Backend key

Look up the existing backend key in APIM and store the same value in ACA:

1. In the Azure portal, open your API Management instance and select **Named values**.
2. Open `chat-backend-key` and use **Show value** to reveal and copy the stored secret. Keep this named value marked as **Secret**.
3. In your Container App, open **Secrets** and select **Add**.
4. Enter `chat-backend-key` as the secret name, select the manual secret-value option, and paste the copied backend key as its value.
5. Select **Add**, then **Save** to save the Container App secrets. If `chat-backend-key` already exists, update its value instead of adding another secret.

Store credentials as secrets rather than in [appsettings.json](../appsettings.json),
and rotate them independently. Keep secret values out of shell history, build
arguments, and source control.

> [!NOTE]
> You can also store both keys in **Azure Key Vault** and configure ACA secrets
> as Key Vault references instead of copying their values. APIM can reference
> the backend key through a Key Vault-backed named value. See
> [Key Vault references in Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets#reference-secret-from-key-vault)
> and [Key Vault-backed named values in APIM](https://learn.microsoft.com/en-us/azure/api-management/api-management-howto-properties).


#### Routing endpoint

1. In the Azure portal, open your API Management instance and select **Overview**.
2. Copy the **Gateway URL**, for example `https://<apim-name>.azure-api.net`. Use the gateway URL, not the developer portal URL or the ACA backend URL.
3. Remove any trailing slash and append `/chat-api/messages`. The complete routing endpoint is `https://<apim-name>.azure-api.net/chat-api/messages`.
4. Use this complete URL for `ChatGateway__Endpoint` in the Container App. In the commands below, `<apim-host>` is the hostname only, such as `<apim-name>.azure-api.net`, without `https://` or a path.

#### Update the ACA environment variables

Run the command below to tell the Container App where to send chat messages
and which saved secrets to use. Use the secret names, not the actual keys.
Replace `<apim-host>` with the APIM gateway hostname you found above.
Leave `ChatGateway__Enabled=false` until the policy tests below pass.

For general environment-variable overrides, see
[Application settings](appsettings.md#update-azure-container-apps).

```bash
# Azure CLI deployment settings
export APPGROUP="gdlchat-app-rg"
export APP="gdlchat"

# ACA Updates
export CHAT_GATEWAY_ENDPOINT="https://<apim-host>/chat-api/messages"

az containerapp update \
  --name "$APP" \
  --resource-group "$APPGROUP" \
  --set-env-vars \
    useAPIM=true \
    ChatGateway__Enabled=false \
    ChatGateway__Endpoint="$CHAT_GATEWAY_ENDPOINT" \
    ChatGateway__BackendKey=secretref:chat-backend-key \
    ChatGateway__SubscriptionKey=secretref:chat-apim-subscription-key
```

> [!NOTE]
> For other configuration changes, see
> [Application settings](appsettings.md#update-azure-container-apps).

### Test the policies

Before testing, complete the [chat API policy setup](azure-api-management.md#api-3-chat-apimessages)
in the APIM guide.

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

After the policy tests pass, enable routing and deploy the revision:

```bash
az containerapp update \
  --name "$APP" \
  --resource-group "$APPGROUP" \
  --set-env-vars ChatGateway__Enabled=true
```

Once enabled, gateway errors are shown to the user. The app does not bypass
APIM if a request fails or is rejected.

## Verify the deployment

The configuration above sets `useAPIM=true` to serve the interface at `/chat/`.
Use the existing `chat-web` and `chat-websocket` APIs; do not create them again.

Keep query strings, cookies, and the backend `/chat` prefix intact. Keep
**Subscription required** off for both interface APIs to allow anonymous access.
In ACA, set the ingress transport to **Auto** and the target port to `8080`.

Keep one active backend revision with both minimum and maximum replicas set
to `1`. The chat-turn backend stores conversations in memory; Blazor session
affinity does not cover the separate HTTP requests through APIM. Multiple
replicas require shared conversation state and coordination of active turns.

1. Open `https://<apim-host>/chat/`, including the trailing slash. Use the APIM
  gateway hostname found above, not the ACA hostname, to verify routing through APIM.
2. In your browser's developer tools, check that the Blazor boot script returns
  HTTP 200 and `/chat/_blazor` upgrades to a WebSocket with HTTP 101.
3. Send a normal message and a follow-up to check the agent connection.
4. Send a message containing `policy-block-test`. The interface should show
  "The chat gateway blocked this request."

If you previously used `chat/socket` as the WebSocket API suffix, change it to
`chat/_blazor`.

The interface shows **Waiting for agent...** until the complete reply returns.
Typing activities and partial replies are not forwarded through the gateway.

To publish a newer image, follow the [Container App update guide](azure-container-apps-update.md).

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
