# Azure Container Registry

Create an Azure Container Registry (ACR) to store your chat service image,
then build and publish the image in Azure.

## Before you begin

- Install Azure CLI and sign in to your Azure subscription.
- Make sure your Azure account can create a registry and run ACR builds.

## Create the registry

Follow the [ACR CLI quickstart](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-get-started-azure-cli)
to create a registry. Choose the **Basic** pricing tier and a globally unique
name that contains only letters and numbers.

In your shell, set the following values to match your subscription and registry.

```bash
export SUBSCRIPTION_ID="<subscription-id>"
export ACRGROUP="gdlchat-rg"
export LOCATION="eastus"
export ACR="gdlchatacr1234"
export VERSION="v1"
```

## Build and push the image

From the repository root, run:

```bash
az acr build \
  --registry "$ACR" \
  --resource-group "$ACRGROUP" \
  --image "copilot-chat:$VERSION" \
  .
```

ACR uploads the source from the current directory, builds the image using the
Dockerfile, and pushes it as `<registry-login-server>/copilot-chat:v1`.

Use a new version tag for each release. To publish an update, change the version
and run the build command again:

```bash
export VERSION="v2"
az acr build \
  --registry "$ACR" \
  --resource-group "$ACRGROUP" \
  --image "copilot-chat:$VERSION" \
  .
```

The image is now available in your registry as `<registry-login-server>/copilot-chat:<version>` and can be deployed to an **Azure Container App**.
