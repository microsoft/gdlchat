# Configure a Copilot Studio Agent

Create and publish a Copilot Studio agent, enable anonymous chat, and connect
it to the chat service through Direct Line. The service manages the connection
on the server, keeping agent tokens out of the browser.

## Prerequisites

- Access to Copilot Studio and an environment where you can create and publish
  agents.
- Access to any external data or tools your agent needs.
- A network connection that allows the chat server to reach Power Platform and
  Direct Line over HTTPS and secure WebSockets (WSS).

Sign in to Copilot Studio with your Microsoft account to create the agent.
People using the published chat can connect anonymously. To set up the local
chat service, follow [Build and Run](../development.md#build-and-run).

## Create the agent

1. Open Copilot Studio and select your environment.
2. Create an agent.
3. Add a name, description, and instructions. Choose a model suited to your task.
4. In the instructions, describe what the agent should do, which data it can
   use, and what it should avoid.
5. Add the tools and connections your agent needs, and configure their permissions.
6. Test the agent. Start by asking what it can do, then test each tool.
7. Save the agent.

Tool connections may require sign-in even when chat is anonymous. Test each
tool again after publishing to check that it works for anonymous users.

For an initial test, you can use instructions like these:

```text
You are the Hiring Agent. Help users understand and coordinate recruitment.
Use configured tools when actual records are needed. Do not invent record IDs
or claim that a record was changed unless a tool confirms it. For general
capability questions, answer briefly without retrieving or modifying records.
```

## Enable anonymous chat

In the agent settings:

1. Open **Agent settings**.
2. Select **Safety & access**.
3. Set **Authentication** to **No authentication**.
4. Open **Web channel security** on the same settings page.
5. Leave **Require secured access** off. This setup uses conversation tokens,
   so you don't need to copy the Direct Line secrets.
6. Save your changes and publish the agent. If **Done** is disabled because
   the settings are already saved, close the dialog and publish.

Depending on your version of Copilot Studio, authentication settings may appear
under **Settings > Security > Authentication**.

> [!IMPORTANT]
> Authentication changes take effect when you publish. Anyone who can reach the
> agent's anonymous channel can then chat with it.

## Identify the agent

Open the agent's publishing options and copy the hosted webchat URL from the
generated website embed code or chat link. The URL contains the environment ID
and agent schema name:

```text
https://copilotstudio.preview.microsoft.com/environments/577b4755-cf12-e3f2-962f-a5528e11fa0c/bots/ppa_hiringagent_yRV7uI/webchat?__version__=2&enableFileAttachment=false&cliAgent=true
```

In this example:

| Field | Example value |
| --- | --- |
| Environment ID | `577b4755-cf12-e3f2-962f-a5528e11fa0c` |
| Agent schema name | `ppa_hiringagent_yRV7uI` |

Use the value after `/bots/` as the schema name. This is different from the
agent's display name and bot record ID.

Use the hosted chat to test the agent and find its connection URLs. The chat
service connects from the server, so you don't need to embed the hosted chat.

## Capture the anonymous endpoints

Get the two connection URLs from your published chat:

1. Open the published hosted webchat URL in a browser.
2. Open your browser's developer tools, select **Network**, and reload the page.
3. Filter requests by `directline/token`. Copy the full **Request URL**,
   including `api-version`. Use this as `TokenEndpoint`.
4. Find the request to `/v3/directline/conversations`. Copy the URL through
   `/v3/directline`, leaving out `/conversations` and everything after it.
   Use this as `DirectLineEndpoint`.
5. Send a test message and check that the agent replies without asking you to
   sign in.

The example agent uses these URLs:

```text
TokenEndpoint:
https://577b4755cf12e3f2962fa5528e11fa.0c.environment.api.powerplatform.com/copilotstudio/agenticruntime/botsbyschema/ppa_hiringagent_yRV7uI/directline/token?api-version=2022-03-01-preview

DirectLineEndpoint:
https://unitedstates.directline.botframework.com/v3/directline
```

You can also find the regional Direct Line base URL in the response to the
hosted page's `/powervirtualagents/regionalchannelsettings` request. Look for
`channelUrlsById.directline`. Use the region returned for your agent; it may
differ from `unitedstates`.

Copy the endpoint URLs, not response tokens or conversation-specific WebSocket
URLs. The service gets fresh tokens and stream URLs as needed. Use the
environment hostname from the request rather than constructing one from the
environment ID.

## Configure the chat service

Use the URLs you copied to set these application settings:

- [TokenEndpoint](appsettings.md#tokenendpoint): your agent's anonymous token URL.
- [DirectLineEndpoint](appsettings.md#directlineendpoint): your agent's regional
  Direct Line API URL.

For the local configuration example, see [Build and Run](../development.md#build-and-run).
