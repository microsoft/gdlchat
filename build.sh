#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ACR="${ACR:-${ACR_NAME:-}}"
VERSION="${VERSION:-latest}"

if [[ -z "$ACR" || -z "${ACRGROUP:-}" ]]; then
    echo "Error: set ACR to the registry name and ACRGROUP to its resource group." >&2
    echo "Usage: ACR=myregistry ACRGROUP=mygroup VERSION=v1.0.0 bash build.sh" >&2
    exit 1
fi

login_server="$(az acr show --name "$ACR" --resource-group "$ACRGROUP" --query loginServer --output tsv)"
if [[ -z "$login_server" ]]; then
    echo "Error: Azure CLI did not return a registry login server." >&2
    exit 1
fi

image="$login_server/copilot-chat:$VERSION"

echo "========================================"
echo "Building Copilot Chat"
echo "========================================"
echo "ACR: $ACR"
echo "Resource group: $ACRGROUP"
echo "Version: $VERSION"
echo "Image: $image"
echo "========================================"

echo "Logging into ACR..."
az acr login --name "$ACR" --resource-group "$ACRGROUP"

docker build --tag "$image" --file "$SCRIPT_DIR/Dockerfile" "$SCRIPT_DIR"
docker push "$image"

echo "========================================"
echo "Published Copilot Chat image:"
echo "  $image"
echo "========================================"