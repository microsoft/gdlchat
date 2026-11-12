# Azure Container Registry

This guide creates an Azure Container Registry (ACR) and builds and pushes the
Copilot Studio Chat image. For deployment to Azure Container Apps, see
[Azure Container Apps](azure-container-apps.md).

## Create the registry

For Azure CLI installation and registry provisioning, follow Microsoft's
[ACR CLI quickstart](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-get-started-azure-cli).
Use a globally unique registry name containing only letters and numbers.
This project uses the Basic SKU. Docker must be installed and running to build
the image locally.

Use these values for this deployment and in the
[Container Apps guide](azure-container-apps.md):

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
ACR="$ACR" ACRGROUP="$ACRGROUP" VERSION="$VERSION" bash ./build.sh
```

The script signs in to ACR, builds the `copilot-chat:$VERSION` image, and pushes
it to the registry. It requires the registry and resource group to exist. The
script uses `latest` if `VERSION` is not set; prefer a new version tag for each
release.

For a later release, set a new tag and run the script again:

```bash
export VERSION="v2"
ACR="$ACR" ACRGROUP="$ACRGROUP" VERSION="$VERSION" bash ./build.sh
```

Continue with [Azure Container Apps](azure-container-apps.md) to deploy the
image.
