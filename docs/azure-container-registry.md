# Azure Container Registry

Create an Azure Container Registry (ACR) to store your chat service image,
then build and publish the image from your computer.

## Before you begin

- Install Azure CLI and sign in to your Azure subscription.
- Install and start Docker.
- Make sure your Azure account can create a registry and push images to it.

## Create the registry

Follow the [ACR CLI quickstart](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-get-started-azure-cli)
to create a registry. Choose the **Basic** pricing tier and a globally unique
name that contains only letters and numbers.

In your shell, set the following values to match your subscription and registry.
The [Container Apps guide](azure-container-apps.md) uses the same values.

```bash
export SUBSCRIPTION_ID="<subscription-id>"
export ACRGROUP="gdlchat-rg"
export LOCATION="eastus"
export ACR="gdlchatacr1234"
export VERSION="v1"
```

## Build and push the image

From the repository root, run [build.sh](../build.sh):

```bash
ACR="$ACR" ACRGROUP="$ACRGROUP" VERSION="$VERSION" bash ./build.sh
```

The script signs in to your registry, builds the image with Docker, and pushes
it as `<registry-login-server>/copilot-chat:v1`.

Use a new version tag for each release. If you omit `VERSION`, the script uses
`latest`. To publish an update, change the version and run the script again:

```bash
export VERSION="v2"
ACR="$ACR" ACRGROUP="$ACRGROUP" VERSION="$VERSION" bash ./build.sh
```

## Next step

Follow the [Azure Container Apps guide](azure-container-apps.md) to deploy
your image.
