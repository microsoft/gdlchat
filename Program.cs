using Azure.Core;
using Azure.Identity;
using gdlchat.Components;
using gdlchat.Endpoints;
using gdlchat.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var authenticationMode = builder.Configuration["CopilotStudioAgent:AuthenticationMode"] ?? "Default";
TokenCredential credential;
if (string.Equals(authenticationMode, "InteractiveBrowser", StringComparison.OrdinalIgnoreCase))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("InteractiveBrowser authentication is only available in Development.");
    }

    var tenantId = builder.Configuration["CopilotStudioAgent:TenantId"];
    var clientId = builder.Configuration["CopilotStudioAgent:AppClientId"];
    if (!Guid.TryParse(tenantId, out _) || !Guid.TryParse(clientId, out _))
    {
        throw new InvalidOperationException(
            "InteractiveBrowser authentication requires valid CopilotStudioAgent:TenantId and CopilotStudioAgent:AppClientId GUIDs.");
    }

    credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
    {
        TenantId = tenantId,
        ClientId = clientId,
        RedirectUri = new Uri("http://localhost")
    });
}
else if (string.Equals(authenticationMode, "Default", StringComparison.OrdinalIgnoreCase))
{
    credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true
    });
}
else
{
    throw new InvalidOperationException(
        "CopilotStudioAgent:AuthenticationMode must be Default or InteractiveBrowser.");
}

builder.Services.AddSingleton<TokenCredential>(credential);
builder.Services.AddTransient<CopilotStudioTokenHandler>();
builder.Services.AddHttpClient("direct-line");
builder.Services.AddHttpClient("copilot-studio")
    .AddHttpMessageHandler<CopilotStudioTokenHandler>();
builder.Services.AddHttpClient("chat-gateway", client =>
{
    client.Timeout = TimeSpan.FromSeconds(95);
    client.MaxResponseContentBufferSize = 256 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false
});
builder.Services.AddTransient<CopilotChatClient>();
builder.Services.AddSingleton<IChatTurnService, ChatTurnService>();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("useAPIM"))
{
    app.UsePathBase("/chat");
    app.UseRouting();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapChatEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
