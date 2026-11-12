# Copilot Studio Chat

This repository contains a .NET 10 Blazor Interactive Server chat application
for Copilot Studio agents.

## Architecture

- The browser connects to the Blazor server; it does not connect to Copilot Studio
  directly.
- By default, the server uses Direct Line: it obtains a conversation token and
  sends messages over HTTPS, then receives replies over a server-side WebSocket.
- An optional API Management (APIM) gateway can apply policies to prompts and
  replies. The existing server-side Copilot Studio SDK is also available as an
  alternative connection mode.

## Why add APIM governance?

Direct Line is the simplest way to connect the chat to an agent. Adding APIM
creates a policy enforcement point for complete chat turns before they reach
the agent and for replies before they return to the UI. Depending on the
policies you configure, APIM can:

- Require an application subscription and apply rate limits or quotas to help
  control traffic and agent usage.
- Validate request and response JSON, reject requests that violate your rules,
  or transform messages at the API boundary.
- Keep gateway and backend credentials out of the browser. In this example,
  APIM can inject a separate backend credential that the server validates.
- Centralize routing and policy-based diagnostics for chat API calls.

The sample APIM setup demonstrates subscription validation, rate limiting,
JSON validation, a test blocking rule, and backend-key injection. These controls
do not authenticate individual visitors: the chat UI is anonymous, and a
subscription key identifies the application calling APIM, not the person using
the chat. Request-side blocking happens before the agent runs; response
policies cannot undo agent actions that have already happened. APIM policies
also do not replace agent safety design or downstream data-access controls.

Learn more in Microsoft's [APIM policy overview](https://learn.microsoft.com/en-us/azure/api-management/api-management-howto-policies)
and [complete policy reference](https://learn.microsoft.com/en-us/azure/api-management/api-management-policies),
including policies for [rate limiting](https://learn.microsoft.com/en-us/azure/api-management/rate-limit-by-key-policy),
[content validation](https://learn.microsoft.com/en-us/azure/api-management/validate-content-policy),
and [setting headers](https://learn.microsoft.com/en-us/azure/api-management/set-header-policy).
See the [APIM setup guide](docs/azure-api-management.md) for this project's
example configuration and limitations.

The swim-lane diagram shows the APIM-enabled flow described above. APIM is
optional; without the gateway, the server connects directly to the agent
through Direct Line.

![Swim-lane diagram of the Copilot chat request flow through the browser, APIM, Blazor server, chat API, backend, and Copilot agent](docs/images/copilot-chat-swimlane.png)

## Prerequisites

- The .NET 10 SDK ([prerequisite details](publish.md#prerequisites)).
- A published Copilot Studio agent configured with **No authentication** and
  **Require secured access** turned off for the default Direct Line mode. See
  [agent setup](docs/copilot-studio-setup.md#enable-anonymous-chat).
- Outbound HTTPS and secure WebSocket access from the server to Power Platform
  and Direct Line.

## Installation

1. Capture the agent's anonymous token URL and regional Direct Line API URL,
   then set them as `CopilotStudioAgent:TokenEndpoint` and
   `CopilotStudioAgent:DirectLineEndpoint` in `appsettings.json`, or use the
   equivalent `CopilotStudioAgent__TokenEndpoint` and
   `CopilotStudioAgent__DirectLineEndpoint` environment variables. Follow
   [Copilot Studio setup](docs/copilot-studio-setup.md), including how to
   [capture the endpoints](docs/copilot-studio-setup.md#capture-the-anonymous-endpoints)
   and [configure the chat service](docs/copilot-studio-setup.md#configure-the-chat-service).
2. From the repository root, restore dependencies:

   ```bash
   dotnet restore
   ```

   See [Build and Run](publish.md#build-and-run) for the full setup.

3. Start the app:

   ```bash
   ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5188
   ```

   See [Build and Run](publish.md#build-and-run) for alternative run options.
4. Open <http://localhost:5188> and follow the [connection verification](publish.md#verify-the-connection)
   steps if needed.

## Direct Line behavior

The server's default `CopilotStudioAgent:ConnectionMode` is `DirectLine`. It
obtains an anonymous conversation token from `CopilotStudioAgent:TokenEndpoint`
and communicates with `CopilotStudioAgent:DirectLineEndpoint`. This mode does
not request an Entra token, launch a browser, or require a Direct Line secret in
the app.

Each chat has separate conversation state and a server-held token. Clearing the
conversation starts a new one; unused backend state expires automatically. The
client reconnects subsequent turns using the previous watermark, excludes user
echoes, and refreshes conversation tokens before expiry when possible.

The text-only chat finishes a reply on an accepting-input or end-of-turn activity,
or after three quiet seconds following bot text. Typing events extend the wait.
Long pauses between bot messages can leave later messages for the next request.
The page cancels a request after 90 seconds.

The gateway setting defaults to `true` when omitted, but the checked-in
`appsettings.json` sets it to `false`. When enabled, each prompt and complete
reply pass through APIM as JSON. The UI shows Waiting for agent until the
response returns; typing events and partial replies are not forwarded through
this HTTP boundary. With the gateway disabled, the direct mode can show Agent
is typing when the agent emits a typing activity. This is not token streaming.

There is no repeated HTTP polling for replies. Token acquisition, conversation
creation or reconnection, and message submission still use HTTPS. Development
configuration suppresses routine Direct Line HTTP request logs while retaining
warnings and errors; set
`Logging:LogLevel:System.Net.Http.HttpClient.direct-line` to `Information` to
restore them.

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

Agent text is rendered as plain text, not executable HTML. Request errors are
shown without exposing credentials; detailed errors are logged on the server.
Since the page is anonymous, keep this demo on a trusted network; add access
controls and usage limits before exposing it publicly with a shared application
identity.
