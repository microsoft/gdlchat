# Azure Container Apps

Deploy your chat service image to Azure Container Apps, configure chat routing,
and publish updates.

This application runs an image from Azure Container Registry (ACR) and connects
anonymously to a **Microsoft Copilot Studio agent** through Direct Line.

> [!IMPORTANT]
> Complete the [Copilot Studio setup guide](copilot-studio-setup.md) before
> starting this guide.

## Prerequisites

- Azure CLI installed and signed in to your Azure subscription.
- Access to the Azure portal with permission to deploy Container Apps.
- A chat service image published to ACR ([build guide](azure-container-registry.md)).
- A published Copilot Studio agent with anonymous access enabled, and its
  `TokenEndpoint` and `DirectLineEndpoint` values recorded
  ([setup guide](copilot-studio-setup.md)).

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

## Connect the Container App to Copilot Studio

The [Copilot Studio setup guide](copilot-studio-setup.md) should already be
complete. If it isn't, complete it now, then record the two endpoint URLs from
its [Capture the anonymous endpoints](copilot-studio-setup.md#capture-the-anonymous-endpoints)
section.

Update the Container App to override the two endpoint settings with your
agent's URLs. The command below uses the example agent's URLs. Replace them
with the URLs from your own agent, including the regional Direct Line URL.

```bash
az containerapp update \
  --name "$APP" \
  --resource-group "$APPGROUP" \
  --set-env-vars \
    CopilotStudioAgent__TokenEndpoint='<!-- token endpoint -->' \
    CopilotStudioAgent__DirectLineEndpoint='<!-- direct line endpoint -->'
```

Environment variable changes create a new revision and retarts the **ACA**.

For the full list of settings, see [appsettings.md](appsettings.md).

## Verify and update the deployment

Run this command to get the app's hostname:

```bash
az containerapp show \
  --name "$APP" \
  --resource-group "$APPGROUP" \
  --query properties.configuration.ingress.fqdn \
  --output tsv
```

## Next steps

The base image is deployed. Before you configure the **ACA** app for APIM
routing, complete the APIM configuration steps in the
[Azure API Management guide](azure-api-management.md). Then continue onto the [APIM chat routing](configure-apim-chat-routing.md) steps.

