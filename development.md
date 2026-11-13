# Develop the Chat Service Locally

Build and run the chat service on your computer, connect it to your Copilot
Studio agent, and customize the Blazor interface.

Your browser connects to the local Blazor server, which connects directly to
the agent. This setup bypasses Azure API Management (APIM), so APIM policies
do not apply.

## Prerequisites

- Install the .NET 10 SDK.
- A network connection that allows the app to reach Power Platform and Direct
  Line over HTTPS and secure WebSockets (WSS).

## Build and Run

> [!NOTE]
> Run the commands in this guide in Windows Subsystem for Linux (WSL).

Run the app in the `Development` environment. Three files control its settings
and startup:

- [appsettings.json](appsettings.json): default application settings in the repository root.
- [appsettings.Development.json](appsettings.Development.json): settings that
  override the defaults in `Development`, also in the repository root.
- [launchSettings.json](Properties/launchSettings.json): startup profiles for
  local runs with `dotnet run` or your IDE. The `http` profile sets the environment
  to `Development` and the URL to `http://localhost:5093/`.

To change the port, edit `applicationUrl` in the launch profile. Learn more
about [launch settings and environments](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments?view=aspnetcore-10.0#development-and-launchsettingsjson).

Update [appsettings.Development.json](appsettings.Development.json) with the
following settings to connect directly to your agent:

```jsonc
{
  "useAPIM": false,
  "ChatGateway": {
    "Enabled": false    // Disables APIM turn mode
  },
  "CopilotStudioAgent": {
    "ConnectionMode": "DirectLine",
    "AuthenticationMode": "Default",
    "TokenEndpoint": "<your-agent-anonymous-token-url>",
    "DirectLineEndpoint": "https://<your-region>.directline.botframework.com/v3/directline"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "System.Net.Http.HttpClient.direct-line": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

Replace the endpoint placeholders with your agent's URLs. To find them, follow the
[Copilot Studio setup guide](docs/copilot-studio-setup.md).

From the repository root, restore dependencies, build the app, and start it:

```bash
dotnet restore
dotnet build
dotnet run --launch-profile http
```
## Verify the Connection

1. Open `http://localhost:5093/`, or the URL you configured. The chat interface
   appears.
2. Send: `Briefly describe what you can do. Do not retrieve or modify any records.`
3. Check that **Waiting for agent...** appears. If the agent sends a typing
   activity, the status changes to **Agent is typing...**.
4. Check that the reply describes the agent's capabilities. Your message
   should appear as **You**, and the reply as **Agent**.
5. Send: `What was my previous question? Do not retrieve or modify any records.`
   Check that the agent remembers your first question.
6. Select **Clear conversation**, then send another message to start a new chat.
7. Open your browser's developer tools and select **Network**. Check that the
   browser makes no requests directly to Power Platform or Direct Line. The
   local server handles those connections.

## How It Works

The server manages the agent connection and keeps conversation tokens out of
the browser.

```text
Browser chat
    | Blazor SignalR connection, normally a WebSocket
    v
.NET server / CopilotChatClient
    | HTTPS: obtain token, create or reconnect conversation, post message
    | Server-side WebSocket: receive replies and typing activities
    v
Regional Direct Line service
    v
Published Copilot Studio agent
```

### Customizing the UI

Use the Blazor components and CSS files to customize the interface:

- Edit [Home.razor](Components/Pages/Home.razor) to change the title, welcome
  message, input placeholder, button labels, and message layout.
- Edit [Home.razor.css](Components/Pages/Home.razor.css) to change chat colors,
  spacing, and sizing. The `--chat-accent` variable controls the accent color.
- Edit [MainLayout.razor](Components/Layout/MainLayout.razor) and
  [NavMenu.razor](Components/Layout/NavMenu.razor) to change the surrounding
  layout and navigation.

To preview changes with hot reload, run this command from the repository root:

```bash
dotnet watch --launch-profile http
```

After making changes, test sending messages, receiving replies, and clearing
the conversation. Preserve input labels, screen-reader status announcements,
and keyboard controls.

### Connection behavior

The following files handle the chat connection:

- [Components/Pages/Home.razor](Components/Pages/Home.razor): chat UI, typing state, errors, and 90-second request cancellation.
- [Services/CopilotChatClient.cs](Services/CopilotChatClient.cs): selects the Direct Line or authenticated SDK path.
- [Services/DirectLineChatClient.cs](Services/DirectLineChatClient.cs): server-held tokens, HTTPS calls, WebSocket reception, echo filtering, and conversation state.
- [Program.cs](Program.cs): registers the clients and Blazor services.

The default connection mode is `DirectLine`. The server gets a conversation
token from `CopilotStudioAgent:TokenEndpoint` and sends Direct Line requests to
`CopilotStudioAgent:DirectLineEndpoint`. This connection uses anonymous access;
you do not need an Entra sign-in or a Direct Line secret.

Each chat has its own conversation state and server-held token. The server
refreshes tokens before they expire when possible. For each follow-up message,
it reconnects using the previous watermark, which tracks the last received
activity. It filters out copies of your outgoing messages. Clearing the
conversation starts a new chat.

The server uses HTTPS to get tokens, start or reconnect conversations, and send
messages. It receives replies over a WebSocket rather than repeatedly polling
for updates.

Replies appear as plain text. Rich cards and token-by-token streaming are not
supported. A turn ends when the agent signals completion or three seconds after
the last reply text. Typing activities extend the wait. If the agent pauses
longer between messages, later messages may appear on the next turn. Requests
time out after 90 seconds.

The interface displays request errors without exposing credentials. Detailed
errors are logged on the server. Agent text is displayed as text, not executed
as HTML.

## SDK connection

To use the authenticated SDK connection, update the `CopilotStudioAgent`
section in [appsettings.Development.json](appsettings.Development.json) as shown
below. Replace the placeholders with your agent's environment ID and schema
name. Keep the logging settings, `useAPIM=false`, and `ChatGateway:Enabled=false`.

```json
{
  "CopilotStudioAgent": {
    "ConnectionMode": "Sdk",
    "AuthenticationMode": "Default",
    "EnvironmentId": "<your-environment-id>",
    "SchemaName": "<your-agent-schema-name>"
  }
}
```

These settings replace the Direct Line connection for local development.
Update the development settings file rather than the defaults in
[appsettings.json](appsettings.json).

You can also set `CopilotStudioAgent__ConnectionMode`,
`CopilotStudioAgent__AuthenticationMode`, `CopilotStudioAgent__EnvironmentId`,
and `CopilotStudioAgent__SchemaName` environment variables. Environment variables
override both files. Remove conflicting values and restart the app after
changing settings. The authentication options below apply to SDK mode.

SDK mode uses `Azure.Identity.DefaultAzureCredential` by default. This can use
your existing developer identity without storing a secret in the app.
Browser sign-in is disabled unless you enable it as described below.

To use a service principal, set these environment variables before starting
the app:

- `AZURE_TENANT_ID`: the Microsoft Entra tenant ID.
- `AZURE_CLIENT_ID`: the application/client ID.
- `AZURE_CLIENT_SECRET`: the client secret value, provided through your secret manager.

Keep secrets out of settings files and source control. Use the Azure environment
variables above for service principal credentials. The `CopilotStudioAgent`
settings `TenantId` and `AppClientId` apply only to browser sign-in.

Your identity must have permission to invoke the agent. Service principal
credentials work only with agents and endpoints that support application
permissions. If your agent requires delegated permissions, use an authorized
user identity instead. The SDK determines the Power Platform token scope from
the connection settings.

## Developer Sign-In

To sign in with your own user account in SDK mode, use `InteractiveBrowser`
authentication. This option requires an app registration and the `Development`
environment.

Configure a single-tenant Microsoft Entra app registration in the agent's tenant:

1. Under Authentication, add the Mobile and desktop applications platform with
   redirect URI `http://localhost`.
2. Under API permissions, add Power Platform API delegated permission
   `CopilotStudio.Copilots.Invoke` and obtain consent as required by your tenant.
3. Set `CopilotStudioAgent:TenantId` and `CopilotStudioAgent:AppClientId` to the
   tenant ID and your registration's Application (client) ID. Do not use the
   Azure CLI client ID or supply a client secret for this mode.

In WSL, set the following variables and start the app:

```bash
export useAPIM=false
export ChatGateway__Enabled=false
export CopilotStudioAgent__ConnectionMode=Sdk
export CopilotStudioAgent__AuthenticationMode=InteractiveBrowser
export CopilotStudioAgent__TenantId='<your-tenant-id>'
export CopilotStudioAgent__AppClientId='<your-app-registration-client-id>'
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5188
```

Send a message to open the browser sign-in page. Everyone using the chat shares
the signed-in account; the chat page has no separate sign-in screen.

Your Windows browser must be able to open the WSL localhost callback. Configure
browser launching in WSL before you start. Complete sign-in within the
90-second request timeout, or send the message again after signing in.

Tokens are cached in memory, so restarting the app may require another sign-in.
In this mode, the browser account determines the identity.
`AZURE_TOKEN_CREDENTIALS` and service principal variables do not select it.
Azure CLI sign-in cannot request this permission without API-owner
preauthorization for its Microsoft-owned client (`AADSTS65002`).

Use browser sign-in only on a trusted local machine. A client secret cannot
replace delegated user permissions.

To switch back to Direct Line, restore the settings in
[Build and Run](#build-and-run) and unset the `CopilotStudioAgent__*` environment
variables you exported above.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| An unexpected sign-in prompt or browser-launch error appears | For Direct Line, set `ConnectionMode=DirectLine` and `AuthenticationMode=Default`. Check environment overrides, then restart the app. |
| `AADSTS65002` or a missing `CopilotStudio.Copilots.Invoke` permission | Check the SDK sign-in configuration. Direct Line uses conversation tokens and does not require this permission. |
| The agent works in Studio but not in the app | Publish the agent, check its anonymous access settings, and verify both endpoint URLs. Check authentication for any tools the agent uses. |
| Your message appears as an agent reply | Rebuild and restart the app, then clear the conversation. The current client filters out outgoing messages. |
| GET requests repeat every second | Rebuild and restart the app to use WebSocket reception. HTTPS requests for setup, reconnection, token refresh, and sending messages are expected. |
| No typing indicator appears | Some agents do not send typing activities. The app shows **Waiting for agent...** until a reply arrives. |
| A request times out | Requests time out after 90 seconds. Check the agent, its tool connections, and the app's network access to secure WebSockets. |
| HTTP logs are too verbose | Set `Logging:LogLevel:System.Net.Http.HttpClient.direct-line` to `Warning`. Use `Information` to diagnose HTTP requests. |
| "Failed to determine the HTTPS port" appears | The HTTP launch profile has no HTTPS redirect target. This warning does not indicate an agent authentication failure. |
| Package vulnerability warnings appear during build | Review the dependency advisories separately from agent connection settings. |

## Application settings

See [Application settings](docs/appsettings.md) for field descriptions,
configuration overrides, and secret handling.

## References

- [Copilot Studio user authentication](https://learn.microsoft.com/en-us/microsoft-copilot-studio/configuration-end-user-authentication)
- [Direct Line WebSocket activities and typing support](https://learn.microsoft.com/en-us/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-receive-activities?view=azure-bot-service-4.0)
- [Direct Line conversation reconnection](https://learn.microsoft.com/en-us/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-reconnect-to-conversation?view=azure-bot-service-4.0)
