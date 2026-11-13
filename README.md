# Copilot Studio Chat

This repository contains a chat client for Copilot Studio agents, deployed with
Azure API Management (APIM) for governance and observability.

## Architecture

- APIM forwards browser requests to the Blazor chat server.
- The server sends chat messages through APIM policies to the protected chat
  backend.
- The backend connects to the Copilot Studio agent through Direct Line.

## Benefits

Copilot Studio supports anonymous access, but it offers limited rate limiting, and
its Direct Line endpoint is visible in browser network traffic. The agent also uses a
more complex Direct Line protocol than typical chat bots, with conversation tokens,
a server-side WebSocket stream, and watermark-based reconnection. This client handles
that protocol on the server, so the browser doesn't need to.

With this architecture, APIM is the entry point, and agent connection details and
conversation tokens stay on the server, out of the browser. APIM also adds governance
in one place, including subscription checks, rate limits, request validation, and
diagnostics. You can extend these controls with more policies without changing the
chat code.

## APIM governance

In this architecture, governance is configured in APIM at both touch points: the request
before it is forwarded to the agent, and the reply before it returns to the page. Policies defined
there apply to all chat traffic without any changes to the chat code. The diagram below
shows where those policies run.

APIM policies cover the most common governance needs for an API. They can limit call rates
and quotas, restrict callers by IP address or identity token, validate request and response
content against a schema, and rewrite headers or bodies. They can also route traffic across
backends, cache responses, and send logs and metrics to monitoring tools. The sample setup
uses a small subset of these policies to show the pattern.

To learn more, read the [APIM policy overview](https://learn.microsoft.com/en-us/azure/api-management/api-management-howto-policies)
and the [policy reference](https://learn.microsoft.com/en-us/azure/api-management/api-management-policies).


![Swim-lane diagram of the Copilot chat request flow through the browser, APIM, Blazor server, chat API, backend, and Copilot agent](docs/images/copilot-chat-swimlane.png)

## Installation

Before you start, you need:

- The .NET 10 SDK ([details](publish.md#prerequisites)).
- An Azure subscription with permission to deploy resources.
- An Azure Container Registry ([setup guide](docs/azure-container-registry.md)).
- An Azure Container Apps environment ([setup guide](docs/azure-container-apps.md)).
- An APIM instance ([setup guide](docs/azure-api-management.md)).
- A published Copilot Studio agent with anonymous access ([agent setup](docs/copilot-studio-setup.md#enable-anonymous-chat)).

Configure each component in the order below.

### Copilot Studio agent

The chat client needs to communicate with the agent through Direct Line. After setup, the
server has two values for that:

- **Anonymous token URL:** the server uses it to get a token for the agent's Direct Line
  channel. The token authorizes the server's requests without an Entra sign-in.
- **Regional Direct Line API URL:** the endpoint the chat client uses to communicate with the
  agent. The server starts conversations, sends messages, and reads replies through it.

To get these values, see [Copilot Studio setup](docs/copilot-studio-setup.md). You'll add
them to the application settings later in these steps.

### Azure API Management

Configure APIM using the [APIM setup guide](docs/azure-api-management.md). Set `useAPIM`
to `true`, and configure `ChatGateway:Endpoint` and the gateway credentials. Keep
`ChatGateway:Enabled` set to `false` until the policy tests pass.

The backend key is a random secret that you generate during setup, separate from the
APIM subscription key. Store the same value in two places: the APIM secret named value
`chat-backend-key`, which the chat policy sends to the server, and the server's
`ChatGateway:BackendKey` setting. The server rejects requests that don't carry it.

### Azure Container Registry and Container Apps

Create or reuse a container registry and a Container Apps environment in the same
subscription. See the [Container Registry guide](docs/azure-container-registry.md) and the
[Container Apps guide](docs/azure-container-apps.md).

### Local app

1. From the repository root, restore dependencies:

   ```bash
   dotnet restore
   ```

   See [Build and Run](publish.md#build-and-run) for the full setup.

2. Start the app:

   ```bash
   ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5188
   ```

   See [Build and Run](publish.md#build-and-run) for alternative run options.

3. Open your configured APIM URL at `https://<apim-host>/chat/` and follow the
   [connection verification](publish.md#verify-the-connection) steps if needed.


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

When the gateway is enabled, each prompt and complete reply passes through APIM as
JSON. The UI shows "Waiting for agent" until the full response returns. Typing
events and partial replies don't cross this HTTP boundary, so the response isn't
streamed.

There is no repeated HTTP polling for replies. Token acquisition, conversation
creation or reconnection, and message submission still use HTTPS. Development
configuration suppresses routine Direct Line HTTP request logs while retaining
warnings and errors; set
`Logging:LogLevel:System.Net.Http.HttpClient.direct-line` to `Information` to
restore them.

## Connection

The existing server-side SDK mode remains available by setting
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
the developer sign-in mode below, not by DefaultAzureCredential; use the standard
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
3. [Azure API Management](docs/azure-api-management.md): host the UI and route chat messages through APIM policies.

Agent text is rendered as plain text, not executable HTML. Request errors are
shown without exposing credentials; detailed errors are logged on the server.
Since the page is anonymous, keep this demo on a trusted network; add access
controls and usage limits before exposing it publicly with a shared application
identity.
