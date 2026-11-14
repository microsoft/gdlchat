# Update Azure Container Apps

Publish a new chat service image and update the existing Container App to use it.

## Prerequisites

- Azure CLI installed and signed in to your Azure subscription.
- An existing Container App and Azure Container Registry (ACR).
- Permission to push images to ACR and update the Container App.

## Set deployment values

Set these values to match your resources. Use a new image version tag:

```bash
export APPGROUP="gdlchat-app-rg"
export APP="gdlchat"
export ACRGROUP="gdlchat-rg"
export ACR="gdlchatacr1234"
export VERSION="v2"
```

## Publish and deploy the update

1. Follow the [ACR build and push steps](azure-container-registry.md#build-and-push-the-image)
   to publish the `copilot-chat` image with the new `VERSION` tag.
2. Update the Container App to use that image:

```bash
ACR_LOGIN_SERVER=$(az acr show --name "$ACR" --resource-group "$ACRGROUP" --query loginServer --output tsv)
az containerapp update \
  --name "$APP" \
  --resource-group "$APPGROUP" \
  --image "$ACR_LOGIN_SERVER/copilot-chat:$VERSION"
```

## Verify the update

Confirm the new revision is healthy and receiving traffic. For APIM routing,
open `https://<apim-host>/chat/` and send a message to check the connection.

Keep one active backend revision with one replica. New revisions reset the
in-memory conversation state; clear existing chats after the update.