# Build and Run the Chat Container

Build a container image for the chat service, test it on your computer, and
publish it to Azure Container Registry (ACR).

## Prerequisites

- Docker to build and run the container on your computer.
- Azure CLI and an existing registry to publish images to ACR.
- Your published agent's endpoint URLs. Get them from the
  [Copilot Studio setup guide](copilot-studio-setup.md).
- A network connection that allows the container to reach Power Platform and
  Direct Line over HTTPS and secure WebSockets (WSS).

## Build and run locally

The [Dockerfile](../Dockerfile) uses the .NET 10 SDK to build the app and an
ASP.NET 10 image to run it. The container runs as a non-root user in the
`Production` environment and listens on HTTP port `8080`.

Before building, check the repository for secrets and unrelated files. There
is no `.dockerignore` file, so the repository contents are included in the
build context. Keep secrets out of the Dockerfile and build arguments.

1. From the repository root, build the image:

```bash
docker build --tag copilot-chat:latest .
```

2. Replace the endpoint placeholders below with your agent's URLs, then start
   the container. The regional Direct Line URL must end in `/v3/directline`.

```bash
docker run --rm --publish 8080:8080 \
  --env useAPIM=false \
  --env ChatGateway__Enabled=false \
  --env CopilotStudioAgent__ConnectionMode=DirectLine \
  --env CopilotStudioAgent__AuthenticationMode=Default \
  --env CopilotStudioAgent__TokenEndpoint='<your-agent-anonymous-token-url>' \
  --env CopilotStudioAgent__DirectLineEndpoint='<your-regional-direct-line-api-url>' \
  copilot-chat:latest
```

The environment variables configure a direct connection to your agent,
bypassing Azure API Management (APIM). They also provide the agent URLs because
the `Production` environment does not load
[appsettings.Development.json](../appsettings.Development.json).

3. Open `http://localhost:8080/` and follow the
   [connection verification steps](../development.md#verify-the-connection).

If port 8080 is already in use, change the host port to `--publish 8081:8080`
and open `http://localhost:8081/`.

## Publish to ACR

Use [build.sh](../build.sh) to build the image on your computer and push it to
ACR. The script signs in to the registry, tags the image, builds it with Docker,
and pushes it.

1. Make sure Docker is running. Sign in with Azure CLI and select the
   subscription that contains your registry.
2. From the repository root, run the following command, replacing the registry
   name, resource group, and version:

```bash
ACR="<registry-name>" ACRGROUP="<resource-group>" VERSION="v1" bash ./build.sh
```

The image is published as `<registry-login-server>/copilot-chat:v1`. Use a new
version tag for each release. For registry setup, see the
[Azure Container Registry guide](azure-container-registry.md).

### Build in Azure without local Docker

ACR can build and store the image without Docker installed on your computer.
Your Azure account needs permission to run builds and publish images in the
registry.

1. Sign in with Azure CLI and select the subscription that contains your registry.
2. Set `ACR` to the registry resource name, not its login-server hostname.
   Set `ACRGROUP` to the registry's resource group.
3. From the repository root, run:

```bash
az acr build \
   --registry "$ACR" \
   --resource-group "$ACRGROUP" \
   --image copilot-chat:latest \
   --platform linux/amd64 \
   .
```

The command uploads the build context, builds the image, and stores it in your
registry. To create a registry, follow the
[Azure Container Registry guide](azure-container-registry.md).

## Deployment requirements

Configure your container host to:

- Route requests to port `8080`.
- Accept HTTPS connections at its ingress.
- Support WebSockets for Blazor SignalR.
- Allow HTTPS and WSS connections to Power Platform and Direct Line.

Conversation state stays in the app's memory. Restarting the container starts
fresh chats. If you run multiple Blazor instances, configure session affinity.
The chat-turn backend used with APIM requires a single replica.

To deploy the image, follow the [Azure Container Apps guide](azure-container-apps.md).
To route chat through APIM, follow the [APIM guide](azure-api-management.md).
