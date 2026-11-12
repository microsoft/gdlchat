# Configure a Copilot Studio Agent

This guide covers creating and publishing an agent for this chat app's default
anonymous Direct Line connection. Direct Line carries chat messages between the
app's server and the agent; the browser does not call Copilot Studio directly
or embed the hosted webchat.

## Prerequisites

- Access to Copilot Studio and the environment where you can create and publish
  agents.
- Access to any external data or tools your agent needs.
- A .NET server with outbound HTTPS and secure WebSocket access to Power
  Platform and Direct Line. For local service prerequisites and run instructions,
  see [Build and run the chat service](../publish.md#build-and-run).

Agent authoring requires your Microsoft account. Anonymous access below applies
to people chatting with the published agent, not to the maker portal.

## Create the agent

1. Open Copilot Studio and select the intended environment.
2. Create an agent. UI labels can differ between authoring experiences and
   releases.
3. Give the agent a name, description, and instructions. Select an available
   model appropriate for the work; this service does not depend on a particular
   model.
4. Describe what the agent should do, what data it can use, and what it should
   avoid doing.
5. Add any tools or connections required for your scenario. These are agent
   capabilities, not requirements of this chat service.
6. Configure each tool's connection and permissions. Anonymous chat does not
   remove authentication requirements for external data or services. A tool may
   still prompt for end-user sign-in or fail; verify its behavior separately
   with the published anonymous agent.
7. Test the agent in Copilot Studio. Start with a simple capability question,
   then test the tools you configured. Save the agent.

Example instructions for a small first test:

```text
You are the Hiring Agent. Help users understand and coordinate recruitment.
Use configured tools when actual records are needed. Do not invent record IDs
or claim that a record was changed unless a tool confirms it. For general
capability questions, answer briefly without retrieving or modifying records.
```

## Enable anonymous chat

The settings may appear in different locations depending on the authoring
experience:

1. Open **Agent settings**.
2. Select **Safety & access**.
3. Set **Authentication** to **No authentication**.
4. Open **Web channel security** on the same settings page.
5. Leave **Require secured access** off. You do not need to copy either of the
   displayed Direct Line secrets for the anonymous setup used here.
6. Select **Done** or save changes, then **publish the agent**. If Done is
   disabled because the settings are already saved, close the dialog and publish.

In the standard authoring experience, the authentication settings can instead
appear under **Settings > Security > Authentication**. Use the labels present
in your experience rather than looking for the older navigation in the preview
UI.

Authentication changes take effect after publishing. This configuration lets
anyone with access to the agent's anonymous channel interact with it.

## Identify the agent

From the agent's publishing options, copy its generated website embed code or
hosted webchat URL. Use the URL to identify the environment and agent schema
name. The following URL illustrates the format:

```text
https://copilotstudio.preview.microsoft.com/environments/577b4755-cf12-e3f2-962f-a5528e11fa0c/bots/ppa_hiringagent_yRV7uI/webchat?__version__=2&enableFileAttachment=false&cliAgent=true
```

The URL identifies:

| Field | Example value |
| --- | --- |
| Environment ID | `577b4755-cf12-e3f2-962f-a5528e11fa0c` |
| Agent schema name | `ppa_hiringagent_yRV7uI` |
| Display name | Hiring Agent |

The schema name is distinct from the display name and bot record ID. Use the
value after `/bots/` in the generated URL.

The hosted webchat is useful for discovering and testing the connection. Do not
paste its iframe into this project: the requirement here is server-side C#
access.

## Capture the anonymous endpoints

Use the URLs observed from the published chat rather than guessing hostnames:

1. Open the published hosted webchat URL in a browser.
2. Open browser developer tools and select **Network** before reloading the page.
3. Filter requests by `directline/token`. Copy the full **Request URL**, including
   its `api-version` query parameter. This is the service's `TokenEndpoint`.
4. Find the request to `/v3/directline/conversations`. Its origin plus
   `/v3/directline` is the service's `DirectLineEndpoint`. Do not include an
   individual conversation ID, `/activities`, or the WebSocket stream URL.
5. Confirm the hosted chat answers a simple message without a Microsoft
   sign-in prompt.

For this example agent, the endpoints are:

```text
TokenEndpoint:
https://577b4755cf12e3f2962fa5528e11fa.0c.environment.api.powerplatform.com/copilotstudio/agenticruntime/botsbyschema/ppa_hiringagent_yRV7uI/directline/token?api-version=2022-03-01-preview

DirectLineEndpoint:
https://unitedstates.directline.botframework.com/v3/directline
```

To find the correct regional Direct Line base, inspect the hosted page's request
to `/powervirtualagents/regionalchannelsettings` on the environment host. Its
`channelUrlsById.directline` value identifies the base URL. Use the region
returned for your agent; do not assume every agent uses `unitedstates`.

Copy endpoint URLs, not the token returned in a response. The .NET service
obtains fresh conversation tokens and WebSocket URLs itself. Do not hard-code
captured tokens or conversation-specific WebSocket URLs. Likewise, do not
construct the environment API hostname by simply removing hyphens from the
environment ID; use the observed host.

## Configure the chat service

Update the `CopilotStudioAgent` section in
[appsettings.json](../appsettings.json). The following configuration shows
example connection values:

```json
{
  "CopilotStudioAgent": {
    "ConnectionMode": "DirectLine",
    "TokenEndpoint": "https://577b4755cf12e3f2962fa5528e11fa.0c.environment.api.powerplatform.com/copilotstudio/agenticruntime/botsbyschema/ppa_hiringagent_yRV7uI/directline/token?api-version=2022-03-01-preview",
    "DirectLineEndpoint": "https://unitedstates.directline.botframework.com/v3/directline"
  }
}
```

Merge these connection settings into the existing file; keep its other settings.
For another agent, replace the endpoint URLs with the values you captured.

With `ConnectionMode` set to `DirectLine`, `TokenEndpoint` tells the service
where to request conversation tokens, and `DirectLineEndpoint` tells it where
to send conversation requests. No Direct Line secret or end-user sign-in is
required for this anonymous setup.

You can override the connection in the server's environment instead. For WSL
Bash:

```bash
export CopilotStudioAgent__TokenEndpoint='<captured anonymous token endpoint URL>'
export CopilotStudioAgent__DirectLineEndpoint='<captured regional base ending in /v3/directline>'
```

Environment variables override the JSON configuration. Check for values left
over from an earlier experiment, and restart the service after changing
configuration.
