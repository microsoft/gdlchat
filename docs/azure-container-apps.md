# Azure Container Apps

Deploy your chat service image to Azure Container Apps, configure chat routing,
and publish updates.

This setup uses a managed identity to pull the image from Azure Container
Registry (ACR), a single replica to keep conversation state in memory, and
HTTPS ingress.

> [!IMPORTANT]
> The chat allows anonymous access. Restrict network access or add access
> controls before making it available publicly.

## Prerequisites

- Install Azure CLI, sign in, and create a Container Apps environment. Follow
  the [Container Apps CLI tutorial](https://learn.microsoft.com/en-us/azure/container-apps/tutorial-deploy-first-app-cli).
- Build and publish your image to ACR. Follow the
  [Azure Container Registry guide](azure-container-registry.md).
- Create a user-assigned managed identity and give it the `AcrPull` role on
  your registry. Follow the
  [managed identity image-pull guide](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity-image-pull).

Set the following values to match your Azure resources and image version:

```bash
export SUBSCRIPTION_ID="<subscription-id>"
export ACRGROUP="gdlchat-rg"
export LOCATION="eastus"
export ACR="gdlchatacr1234"
export ACA_ENV="gdlchat-env"
export APP="gdlchat"
export IDENTITY="${APP}-acr-pull"
export VERSION="v1"
```

## Create the Container App

From your shell, run the following command to deploy the image. The app listens
on port `8080` and serves the interface at `/chat/`.

```bash
ACR_LOGIN_SERVER=$(az acr show --name "$ACR" --resource-group "$ACRGROUP" --query loginServer --output tsv)
IDENTITY_ID=$(az identity show --name "$IDENTITY" --resource-group "$ACRGROUP" --query id --output tsv)

az containerapp create \
  --name "$APP" \
  --resource-group "$ACRGROUP" \
  --environment "$ACA_ENV" \
  --image "$ACR_LOGIN_SERVER/copilot-chat:$VERSION" \
  --user-assigned "$IDENTITY_ID" \
  --registry-server "$ACR_LOGIN_SERVER" \
  --registry-identity "$IDENTITY_ID" \
  --ingress external \
  --target-port 8080 \
  --transport auto \
  --min-replicas 1 \
  --max-replicas 1 \
  --env-vars \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    useAPIM=true \
    ChatGateway__Enabled=false
```

The `useAPIM` setting controls the interface path: `true` uses `/chat/`, and
`false` uses `/`. Chat messages connect directly to the agent until you enable
Azure API Management (APIM) routing below.

Keep one replica for the in-memory conversation backend. Restarting the app
or deploying a new revision resets conversation state.

## Configure APIM message routing

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
  --resource-group "$ACRGROUP" \
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
  --resource-group "$ACRGROUP" \
  --query properties.configuration.ingress.fqdn \
  --output tsv
```

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
  --resource-group "$ACRGROUP" \
  --image "$ACR_LOGIN_SERVER/copilot-chat:$VERSION"
```
