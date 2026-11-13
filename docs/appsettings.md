# Application settings

[appsettings.json](../appsettings.json) contains the default settings. Use
[appsettings.Development.json](../appsettings.Development.json) for local overrides.
Both files are in the repository root.

To learn how settings files, environment variables, user secrets, and command-line
arguments work together, see
[Configuration in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0).

## Settings reference

| Setting | Purpose |
| --- | --- |
| `useAPIM` | Selects the UI base path: `/chat/` when `true`, or `/` when `false`. It does not enable APIM message routing. |
| `ChatGateway:Enabled` | Routes chat turns through APIM when `true`, or directly to the agent when `false`. |
| `ChatGateway:Endpoint` | The HTTPS APIM messages URL used when gateway routing is enabled. |
| `ChatGateway:SubscriptionKey` | Authorizes the server's requests to APIM. |
| `ChatGateway:BackendKey` | Protects the backend chat endpoint. It must match the key APIM sends. |
| `CopilotStudioAgent:ConnectionMode` | Selects the default `DirectLine` connection or the optional `Sdk` connection. |
| `CopilotStudioAgent:EnvironmentId` / `SchemaName` | Identify the agent in SDK mode. |
| `CopilotStudioAgent:AuthenticationMode` | Selects `Default` Azure credentials or development-only `InteractiveBrowser` sign-in for SDK mode. |
| `CopilotStudioAgent:TenantId` / `AppClientId` | Identify the tenant and app registration for interactive browser sign-in. |
| `Logging:LogLevel` | Controls log verbosity, including overrides for individual categories. |
| `AllowedHosts` | Restricts accepted HTTP hostnames. `*` allows all hosts. |

## Agent endpoints

Set these fields in the `CopilotStudioAgent` section of your settings file.
For local development, use
[appsettings.Development.json](../appsettings.Development.json).

### TokenEndpoint

Set `CopilotStudioAgent:TokenEndpoint` to your published agent's anonymous token
URL, including the `api-version` query parameter. The server uses this URL to
obtain conversation tokens.

### DirectLineEndpoint

Set `CopilotStudioAgent:DirectLineEndpoint` to your agent's regional Direct Line
API URL, ending in `/v3/directline`. Leave out conversation IDs, `/activities`,
and WebSocket stream URLs.

## Override settings

Environment variables take precedence over both settings files. Replace `:`
with `__` in variable names, for example `ChatGateway__Enabled=false`.

For local development, set `useAPIM` and `ChatGateway:Enabled` to `false`, as
shown in [Build and Run](../development.md#build-and-run). See the
[APIM guide](azure-api-management.md#configure-the-chat-api) for gateway
settings. Store local client secrets with .NET user secrets, not in committed
configuration. Use secret references for gateway credentials in Azure.

### Converting appsettings.json to environment variables

Use [container-env.sh](../container-env.sh) to convert settings into `NAME=value`
lines for Azure Container Apps. The script requires `jq`. From the repository
root, run:

```bash
bash ./container-env.sh
```

By default, the script reads [appsettings.json](../appsettings.json). To use a
different JSON file, pass its path:

```bash
bash ./container-env.sh path/to/appsettings.json
```

The script converts nested names to double-underscore names, such as
`CopilotStudioAgent__TokenEndpoint`. It also adds
`ASPNETCORE_ENVIRONMENT=Production` and `ASPNETCORE_HTTP_PORTS=8080`.
It reads only the selected file, not merged settings or environment overrides.
The input must be valid JSON without comments.

The output is configuration data, not a shell script. Running the converter
does not export variables or update Azure resources.

### Update Azure Container Apps

1. Save the converted settings to a file:

```bash
bash ./container-env.sh > container-env.txt
```

2. Review the file before applying it. Replace credential values with
   `secretref:<secret-name>` references to existing Container App secrets.
   Remove blank credential entries if you want to keep their deployed values.
   Keep this file out of source control.
3. Sign in with Azure CLI and select the subscription that contains your app.
   In WSL or Bash, load the lines into an array and apply them:

```bash
mapfile -t settings < container-env.txt
az containerapp update \
  --name "<container-app-name>" \
  --resource-group "<resource-group>" \
  --set-env-vars "${settings[@]}"
```

This updates the listed settings while preserving other environment variables.
Environment variable changes create a new revision. If multiple revisions are
active, route traffic to the new revision. For this sample's in-memory chat
backend, keep one active revision with one replica.

You can also enter the generated names and values in the Azure portal: open
your Container App, select **Containers > Edit and deploy**, and update the
environment variables. Save and deploy the revision.

Open the chat page, clear the conversation, and send a message to verify the
connection. See [Azure Container Apps](azure-container-apps.md) for deployment
and gateway settings.
