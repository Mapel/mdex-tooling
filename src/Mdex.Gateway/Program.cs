using Mdex.Core.Cli;
using Mdex.Core.Security;
using Mdex.Gateway.Auth;
using Mdex.Mcp.Tools;
using Mdex.Mcp.Tools.Tools;
using Mdex.Tokens;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// --- configuration: vault root + token store path (env wins over appsettings) ---
var vaultRoot = builder.Configuration["Mdex:Root"]
    ?? Environment.GetEnvironmentVariable("MDEX_ROOT")
    ?? throw new InvalidOperationException("Set Mdex:Root in config or MDEX_ROOT env var.");
var tokenPath = builder.Configuration["Mdex:TokenStore"];
if (string.IsNullOrWhiteSpace(tokenPath)) tokenPath = TokenPaths.Default();

// --- shared singletons: engine, in-process backend (server vault root), token store + watcher ---
var engine = CommandEngine.Default();
var tokenStore = new TokenStore(tokenPath);

builder.Services.AddSingleton(engine);
builder.Services.AddSingleton(sp => new MdexBackend(engine, vaultRoot,
    msg => sp.GetRequiredService<ILoggerFactory>().CreateLogger("mdex").LogError("{Detail}", msg)));
builder.Services.AddSingleton(tokenStore);
builder.Services.AddSingleton(new TokenStoreWatcher(tokenStore, tokenPath));

// --- role seam: per-request role from the authenticated claim → shared ToolAuthorizer ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRoleProvider, HttpRoleProvider>();
builder.Services.AddScoped<ToolAuthorizer>();

// --- auth: bearer token → role claim ---
builder.Services.AddAuthentication(BearerAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BearerAuthHandler>(BearerAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

// --- MCP server over HTTP, tools from the SHARED Mdex.Mcp.Tools assembly ---
builder.Services.AddMcpServer()
    .WithHttpTransport(options => { options.EnableLegacySse = true; })
    .WithToolsFromAssembly(typeof(ReadTools).Assembly);

var app = builder.Build();

app.Services.GetRequiredService<TokenStoreWatcher>();   // touch the singleton so watching starts at boot

app.UseAuthentication();
app.UseAuthorization();
app.MapMcp().RequireAuthorization();

app.Run();

public partial class Program { }   // expose entry point to WebApplicationFactory in tests
