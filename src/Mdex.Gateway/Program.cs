using Mdex.Core.Cli;
using Mdex.Core.Security;
using Mdex.Gateway.Auth;
using Mdex.Mcp.Tools;
using Mdex.Mcp.Tools.Tools;
using Mdex.Tokens;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- configuration: vault root + token store path (env wins over appsettings) ---
var vaultRoot = builder.Configuration["Mdex:Root"];
if (string.IsNullOrWhiteSpace(vaultRoot)) vaultRoot = Environment.GetEnvironmentVariable("MDEX_ROOT");
if (string.IsNullOrWhiteSpace(vaultRoot))
    throw new InvalidOperationException("Set Mdex:Root in config or MDEX_ROOT env var.");
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

// --- OAuth client seed ---
builder.Services.Configure<List<OAuthClientConfig>>(
    builder.Configuration.GetSection("Mdex:OAuthClients"));
builder.Services.AddHostedService<OAuthClientSeed>();

// --- OpenIddict: AS (authorization server) + Validation (resource server) ---
builder.Services.AddOpenIddict()
    .AddCore(o => o.UseInMemoryStores())   // no DB needed: clients seeded from config, codes/tokens are JWTs
    .AddServer(o =>
    {
        o.SetAuthorizationEndpointUris("/connect/authorize");
        o.SetTokenEndpointUris("/connect/token");

        o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();

        o.RegisterScopes("mdex:read", "mdex:write-intent", "mdex:fullwrite");

        o.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough();   // delegate GET/POST to our controller

        // Ephemeral keys — replace with a persistent X.509 certificate before go-live
        o.AddEphemeralEncryptionKey();
        o.AddEphemeralSigningKey();
        o.DisableAccessTokenEncryption();   // keeps JWTs readable; required by some MCP clients
    })
    .AddValidation(o =>
    {
        o.UseLocalServer();   // share keys with the collocated AS — no HTTP round-trip
        o.UseAspNetCore();
    });

// --- auth: hybrid — custom Bearer (scripts/Langdock) + OpenIddict JWT (claude.ai/ChatGPT) ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = BearerAuthHandler.SchemeName;
    options.DefaultChallengeScheme = BearerAuthHandler.SchemeName;
})
.AddScheme<AuthenticationSchemeOptions, BearerAuthHandler>(BearerAuthHandler.SchemeName, _ => { });
// OpenIddict Validation registers its own scheme via o.UseAspNetCore() above

// Maps OAuth scopes from validated JWTs to the mdex-role claim that HttpRoleProvider reads
builder.Services.AddSingleton<IClaimsTransformation, OAuthScopeClaimsTransformer>();

// MCP endpoint accepts either custom Bearer or OpenIddict JWT
builder.Services.AddAuthorization(options =>
    options.AddPolicy("MdexAccess", p => p
        .AddAuthenticationSchemes(
            BearerAuthHandler.SchemeName,
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()));

builder.Services.AddControllers();   // for AuthorizationController (consent page)

// --- MCP server over HTTP, tools from the SHARED Mdex.Mcp.Tools assembly ---
builder.Services.AddMcpServer()
    .WithHttpTransport(options => { options.EnableLegacySse = true; })
    .WithToolsFromAssembly(typeof(ReadTools).Assembly);

// --- visibility: hide tools above the caller's (per-token) role from tools/list ---
builder.Services.Configure<ModelContextProtocol.Server.McpServerOptions>(o =>
    o.Filters.Request.ListToolsFilters.Add(RoleToolVisibility.ListToolsFilter));

var app = builder.Build();

app.Services.GetRequiredService<TokenStoreWatcher>();   // touch singleton so watching starts at boot

app.UseAuthentication();
app.UseAuthorization();

// RFC 9728 — Protected Resource Metadata: tells MCP clients where the AS lives
app.MapGet("/.well-known/oauth-protected-resource", (HttpContext ctx) =>
{
    var issuer = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    return Results.Json(new
    {
        resource = issuer,
        authorization_servers = new[] { issuer },
        bearer_methods_supported = new[] { "header" },
        scopes_supported = new[] { "mdex:read", "mdex:write-intent", "mdex:fullwrite" },
    });
});

app.MapControllers();                              // /connect/authorize (consent controller)
app.MapMcp().RequireAuthorization("MdexAccess");   // named policy: accepts Bearer or OAuth JWT

app.Run();

public partial class Program { }   // expose entry point to WebApplicationFactory in tests
