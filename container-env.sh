#!/bin/bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    echo "Usage: bash container-env.sh [path/to/appsettings.json]"
    echo "Prints NAME=value lines for Azure Container Apps; does not change any resources."
    exit 0
fi

if [[ $# -gt 1 ]]; then
    echo "Usage: bash container-env.sh [path/to/appsettings.json]" >&2
    exit 1
fi

SETTINGS_FILE="${1:-$SCRIPT_DIR/appsettings.json}"

if ! command -v jq >/dev/null 2>&1; then
    echo "Error: jq is required to read appsettings.json." >&2
    exit 1
fi

if [[ ! -f "$SETTINGS_FILE" ]]; then
    echo "Error: configuration file not found: $SETTINGS_FILE" >&2
    exit 1
fi

jq --raw-output '
    if type != "object" then error("Configuration must be a JSON object") else . end
    | . as $settings
    | [
        "ASPNETCORE_ENVIRONMENT=Production",
        "ASPNETCORE_HTTP_PORTS=8080",
        ($settings
            | paths(type != "object" and type != "array") as $path
            | ($path | map(tostring) | join("__")) + "="
                + (getpath($path) | if . == null then "" else tostring end))
    ]
    | if any(.[]; test("[\r\n]")) then
        error("Multiline settings cannot be printed as one NAME=value line")
      else .[] end
' "$SETTINGS_FILE"