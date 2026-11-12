# Copilot Studio Chat

Copilot Studio Chat is a Blazor Interactive Server application that connects to
Copilot Studio through Direct Line. An optional API Management (APIM) gateway
can apply policies to chat messages.

The server's default `CopilotStudioAgent:ConnectionMode` is `DirectLine` and obtains an anonymous
conversation token from `CopilotStudioAgent:TokenEndpoint`
and communicates with `CopilotStudioAgent:DirectLineEndpoint`. 

Ensure that the copilot agent has been published with No authentication and Require 
secured access off. This mode does not request an Entra token, launch a browser, or 
require a Direct Line secret in the app.

Each chat has a separate conversation state and a server-held token. Clear
conversation starts a new conversation; unused backend state expires automatically.
The agent transport sends messages with HTTPS and receives
replies and typing activities through a server-side Direct Line WebSocket. It
connects before posting a message, reconnects subsequent turns using the previous
watermark, and excludes user echoes by activity ID, sender ID, and user role.
Conversation tokens are refreshed before expiry when possible.
The text-only POC finishes a reply on an accepting-input or end-of-turn activity,
or after three quiet seconds following bot text. Typing events extend the wait.
Long pauses between multiple bot
messages can therefore leave later messages for the next request. The page
cancels a request after 90 seconds.

The gateway setting defaults to `true` when omitted, but the checked-in
`appsettings.json` sets it to `false`. When enabled, each prompt and complete
reply pass through APIM as JSON. The UI shows Waiting for agent until the
approved response returns; typing events and partial replies are not forwarded
through this HTTP boundary. With the gateway disabled, the original direct mode
can show Agent is typing when the agent emits a typing activity. This is not
token streaming.

There is no repeated HTTP polling for replies. Token acquisition, conversation
creation or reconnection, and message submission still use HTTPS. Development
configuration suppresses routine Direct Line HTTP request logs while retaining
warnings and errors; set `Logging:LogLevel:System.Net.Http.HttpClient.direct-line`
to `Information` to restore them.

## Connection

The existing server-side SDK chat remains available by setting
`CopilotStudioAgent:ConnectionMode` to `Sdk`. The authentication and timeout
instructions below apply only to that mode.

Set `CopilotStudioAgent:EnvironmentId` and `CopilotStudioAgent:SchemaName` in
appsettings.json, or use `CopilotStudioAgent__EnvironmentId` and
`CopilotStudioAgent__SchemaName` environment variables.

By default, authentication uses `Azure.Identity.DefaultAzureCredential`. It can use an
existing developer identity or a managed identity without storing a secret.
Interactive browser authentication is disabled in this mode.

To use a service principal, set these variables in the server's environment
before starting the app:

- `AZURE_TENANT_ID`: the Microsoft Entra tenant ID.
- `AZURE_CLIENT_ID`: the application/client ID.
- `AZURE_CLIENT_SECRET`: the client secret value, provided through your secret manager.

Do not store secrets in appsettings.json or commit them to source control.
The `TenantId` and `AppClientId` settings in `CopilotStudioAgent` are used only by
the optional developer mode below, not by DefaultAzureCredential; use the standard
Azure environment variables above for application credentials.
Restart the server after changing environment variables.

The identity must be authorized to invoke the agent. Application credentials
require a Copilot Studio agent and endpoint that support application permissions;
they do not grant access by themselves. A delegated-only agent requires an
authorized user credential rather than a client secret. The SDK determines the
Power Platform token scope from its connection settings.

## Developer Sign-In

To use delegated permissions with your own app registration, set
`CopilotStudioAgent:AuthenticationMode` to `InteractiveBrowser`. This mode is
allowed only when `ASPNETCORE_ENVIRONMENT=Development`.

Configure a single-tenant Microsoft Entra app registration in the agent's tenant:

1. Under Authentication, add the Mobile and desktop applications platform with
	redirect URI `http://localhost`.
2. Under API permissions, add Power Platform API delegated permission
	`CopilotStudio.Copilots.Invoke` and obtain consent as required by your tenant.
3. Set `CopilotStudioAgent:TenantId` and `CopilotStudioAgent:AppClientId` to the
	tenant ID and your registration's Application (client) ID. Do not use the
	Azure CLI client ID or supply a client secret for this mode.

For example, from WSL Bash:

```bash
export CopilotStudioAgent__AuthenticationMode=InteractiveBrowser
export CopilotStudioAgent__TenantId=e0609078-8b1f-4b6b-a7dc-5928feee8194
export CopilotStudioAgent__AppClientId='<your-app-registration-client-id>'
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5188
```

The first chat request opens the developer's browser for sign-in. The chat page
itself has no login screen; all visitors use the signed-in developer's identity.
Run the server locally with browser access to its localhost callback. In WSL,
the Windows browser must be able to reach the WSL localhost listener, and browser
launching must be configured. Sign-in must finish within the chat's 90-second
request timeout; retry the message if it times out.

The singleton credential caches tokens in memory for the server process; a server
restart may require sign-in again. `AZURE_TOKEN_CREDENTIALS` and the service
principal environment variables do not select the identity in this mode. Azure
CLI login cannot request this permission because its Microsoft-owned client
requires API-owner preauthorization (`AADSTS65002`).

Keep this developer mode local and trusted. For production, return to `Default`
and configure an authorized application or managed identity on a supported agent
endpoint. A client secret cannot provide delegated user permissions.

## Azure deployment

Use the service guides below to create and deploy the Azure resources:

1. [Azure Container Registry](docs/azure-container-registry.md): create a registry and build and push the image with `build.sh`.
2. [Azure Container Apps](docs/azure-container-apps.md): configure registry access, deploy the image, and set application configuration.
3. [Azure API Management](docs/azure-api-management.md): optionally host the UI and route chat messages through APIM policies.

## Run

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5188
```

Agent text is rendered as plain text, not executable HTML. Request errors are
shown without exposing credentials; detailed errors are logged on the server.
There is a 90-second request timeout. Since the page is anonymous, keep this
demo on a trusted network; add access controls and usage limits before exposing
it publicly with a shared application identity.
