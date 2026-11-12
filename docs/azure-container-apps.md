# Azure Container Apps

This guide deploys the Copilot Studio Chat image to Azure Container Apps. First
create the registry and push an image using
[Azure Container Registry](azure-container-registry.md).

The examples use a user-assigned managed identity for image pulls, one replica
for the in-memory conversation backend, and external HTTPS ingress. Restrict
network access or add access controls before exposing this anonymous app
publicly.

## Prerequisites

Follow Microsoft's [Azure Container Apps CLI tutorial](https://learn.microsoft.com/en-us/azure/container-apps/tutorial-deploy-first-app-cli)
for Azure CLI setup and environment provisioning. For private image
authentication, use the [managed identity image-pull guide](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity-image-pull).
Create a user-assigned identity, grant it the `AcrPull` role on this project's
registry, and use that identity for the registry pull.

Ensure the image is available in ACR. These values must match the registry
deployment:

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

After creating the environment and assigning `AcrPull`, deploy the image with
the identity configured for registry access. The app listens on port `8080`.
`useAPIM=true` configures the app to serve its UI under `/chat/`; it does not
enable APIM message routing.

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

Use `useAPIM=false` to serve the UI at `/` instead. Keep one replica with the
current in-memory conversation backend; restarts and new revisions lose
conversation state.

## Configure APIM message routing

If the chat API and its policies are configured, follow Microsoft's
[Container Apps secrets guide](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets)
to create these secrets in the Container App's **Secrets** page:

- `chat-backend-key`: the same backend credential used by APIM's named value.
- `chat-apim-subscription-key`: a separate, API-scoped APIM subscription key.

Do not put secret values in shell history, image build arguments, or source
control. Set the environment variables using secret references:

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

Replace `<apim-host>` with the APIM gateway hostname. Keep
`ChatGateway__Enabled=false` until the API and policies are ready. See
[Azure API Management](azure-api-management.md) for APIM configuration.

## Verify and update the deployment

Get the app hostname:

```bash
az containerapp show \
  --name "$APP" \
  --resource-group "$ACRGROUP" \
  --query properties.configuration.ingress.fqdn \
  --output tsv
```

Open the hostname with `/chat/` when `useAPIM=true`, or `/` when it is false.
For a new image version, follow the
[ACR build and push steps](azure-container-registry.md), then update the app:

```bash
ACR_LOGIN_SERVER=$(az acr show --name "$ACR" --resource-group "$ACRGROUP" --query loginServer --output tsv)
az containerapp update \
  --name "$APP" \
  --resource-group "$ACRGROUP" \
  --image "$ACR_LOGIN_SERVER/copilot-chat:$VERSION"
```
