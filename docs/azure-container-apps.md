# Azure Container Apps

Deploy your chat service image to Azure Container Apps, configure chat routing,
and publish updates.

This application runs an image from Azure Container Registry (ACR) and connects
anonymously to a **Microsoft Copilot Studio agent** through Direct Line.

> [!IMPORTANT]
> Follow the [Copilot Studio setup guide](copilot-studio-setup.md) to configure
> and publish the agent with anonymous access enabled.

## Prerequisites

- Azure CLI installed and signed in to your Azure subscription.
- Access to the Azure portal with permission to deploy Container Apps.
- A chat service image published to ACR ([build guide](azure-container-registry.md)).

For the CLI commands later in this guide, set these values to match your Azure
resources and image version:

```bash
# Azure CLI deployment settings
export APPGROUP="gdlchat-app-rg"
export APP="gdlchat"

# Container image
export ACRGROUP="gdlchat-rg"
export ACR="gdlchatacr1234"
export VERSION="v1"
```

## Create the Container App

Follow the [Azure portal quickstart](https://learn.microsoft.com/en-us/azure/container-apps/quickstart-portal)
to create the Container App. Use `APPGROUP` for the resource group and `APP` for
the app name.
Create or select a Container Apps environment in the portal.

On the **Container** tab, select **Azure Container Registry** as the image source
and choose your registry, the `copilot-chat` image, and your version tag instead
of the quickstart image. Configure registry authentication using the portal's
[managed identity setup](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity-image-pull?pivots=portal).

Enable ingress, allow traffic from anywhere, and set the target port to `8080`
with transport `auto`.

Set both **minimum replicas** and **maximum replicas** to `1` for the in-memory
conversation backend. Restarting the app or deploying a new revision resets
conversation state.

**TBD:**
If multiple replicas are needed, the conversation backend must be redesigned
to share state and coordinate active conversations across replicas. Blazor
connections also require session affinity so each session stays on the same
replica.

## Set environment variables

The container image includes the settings from `appsettings.json` and is embedded 
into the running application.  If you need to override specific settings, you can 
define them as environment variables. Settings in `appsettings.json` are hierarchical, 
while environment variables are flat name-value pairs. Use `__` to represent each level
of nesting: for example, `ChatGateway.Enabled` in JSON settings maps to
`ChatGateway__Enabled` as the environment variable name.

To update specific settings, use the following command, replacing the example
names and values with the settings you want to override:

```bash
az containerapp update \
  --name "$APP" \
  --resource-group "$APPGROUP" \
  --set-env-vars \
    Setting_1_Name=value \
    Section__Setting_2_Name=value
```

## Configure APIM message routing

As deployed, the chat application connects directly to the **Microsoft Copilot
Studio agent**. To enable routing through APIM, we will configure the APIM
endpoint, secure calls to APIM with a subscription key, and secure calls from
APIM to the chat backend with a shared key stored as an APIM secret named value
and a Container App secret.

### Routing

### Secure

### Named Value

### ACA Secret




- **chat-backend-key:** Allows the APIM to securely call the client.
- **chat-apim-subscription-key:** Allows the client to call the APIM.


- **and so we will need to tell the chat application 2 things

(APIM) routing.

1. Follow the [Azure API Management guide](azure-api-management.md) to create
   the chat API, configure its credentials, and test its policies.
2. In your Container App, open **Secrets** and create the following entries.
   See [Manage secrets in Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets)
   for instructions.

   - `chat-backend-key`: the same backend credential stored in APIM's named value.
   - `chat-apim-subscription-key`: a separate subscription key scoped to the chat API.

3. Replace `<apim-host>` below with your APIM hostname, then run the command to
   enable routing. Keep secret values out of shell history, build arguments,
   and source control; use the secret references shown here.

```bash
export CHAT_GATEWAY_ENDPOINT="https://<apim-host>/chat-api/messages"

az containerapp update \
  --name "$APP" \
  --resource-group "$APPGROUP" \
  --set-env-vars \
    useAPIM=true \
    ChatGateway__Enabled=true \
    ChatGateway__Endpoint="$CHAT_GATEWAY_ENDPOINT" \
    ChatGateway__BackendKey=secretref:chat-backend-key \
    ChatGateway__SubscriptionKey=secretref:chat-apim-subscription-key
```

Keep `ChatGateway__Enabled=false` until the API and policy tests pass.
For other configuration changes, see
[Application settings](appsettings.md#update-azure-container-apps).

## Verify and update the deployment

Run this command to get the app's hostname:

```bash
az containerapp show \
  --name "$APP" \
  --resource-group "$APPGROUP" \
  --query properties.configuration.ingress.fqdn \
  --output tsv
```

The `useAPIM` setting controls the interface path: `true` uses `/chat/`, and
`false` uses `/`.

Open `https://<app-host>/chat/`, replacing `<app-host>` with the returned hostname.
If you set `useAPIM=false`, open `https://<app-host>/` instead. Send a message
to check the agent connection.

To publish an update:

1. Set `VERSION` to a new image tag and follow the
   [ACR build and push steps](azure-container-registry.md#build-and-push-the-image).
2. Update the app to use that image:

```bash
ACR_LOGIN_SERVER=$(az acr show --name "$ACR" --resource-group "$ACRGROUP" --query loginServer --output tsv)
az containerapp update \
  --name "$APP" \
  --resource-group "$APPGROUP" \
  --image "$ACR_LOGIN_SERVER/copilot-chat:$VERSION"
```
