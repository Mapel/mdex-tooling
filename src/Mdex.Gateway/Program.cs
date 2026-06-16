using System.Security.Cryptography.X509Certificates;
using Mdex.Core.Cli;
using Mdex.Core.Security;
using Mdex.Gateway.Auth;
using Mdex.Mcp.Tools;
using Mdex.Mcp.Tools.Tools;
using Mdex.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- configuration: vault root + token store path (env wins over appsettings) ---
var vaultRoot = builder.Configuration["Mdex:Root"];
if (string.IsNullOrWhiteSpace(vaultRoot)) vaultRoot = Environment.GetEnvironmentVariable("MDEX_ROOT");
if (string.IsNullOrWhiteSpace(vaultRoot))
    throw new InvalidOperationException("Set Mdex:Root in config or MDEX_ROOT env var.");
var tokenPath = builder.Configuration["Mdex:TokenStore"];
if (string.IsNullOrWhiteSpace(tokenPath)) tokenPath = TokenPaths.Default();

// OpenIddict store (authorizations + tokens) — persisted in a SQLite file so OAuth sessions and
// refresh tokens survive restarts/redeploys. Defaults next to the token store (same volume).
var openiddictDbPath = builder.Configuration["Mdex:OpenIddictDb"];
if (string.IsNullOrWhiteSpace(openiddictDbPath))
    openiddictDbPath = Environment.GetEnvironmentVariable("MDEX_OPENIDDICT_DB");
if (string.IsNullOrWhiteSpace(openiddictDbPath))
    openiddictDbPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(tokenPath)) ?? ".", "openiddict.db");

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
builder.Services.AddDbContext<DbContext>(o =>
{
    o.UseSqlite($"Data Source={openiddictDbPath}");
    o.UseOpenIddict();
});

builder.Services.AddOpenIddict()
    .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<DbContext>())
    .AddServer(o =>
    {
        o.SetAuthorizationEndpointUris("/connect/authorize");
        o.SetTokenEndpointUris("/connect/token");

        // Refresh tokens let clients (claude.ai/ChatGPT) renew an expired access token silently
        // instead of re-running the consent flow. Granted only when the client requests
        // offline_access (see AuthorizationController). Default lifetimes: 1h access, 14d sliding refresh.
        o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();

        // offline_access must be advertised so clients know to ask for a refresh token.
        o.RegisterScopes("mdex:read", "mdex:write-intent", "mdex:fullwrite", "offline_access");

        // Greedy MCP clients (claude.ai) request every advertised scope. Don't reject when a client
        // asks for more than its tier — AuthorizationController clamps the grant to the client's
        // allowed scopes instead. The seeded per-client scope permissions remain the allow-list.
        o.IgnoreScopePermissions();

        o.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough();   // delegate GET/POST to our controller

        // Signing AND encryption keys: persistent X.509 certificate so tokens survive restarts.
        // Refresh tokens and authorization codes are always encrypted with the encryption key, so it
        // must persist too — otherwise a restart makes stored refresh tokens undecryptable, defeating
        // the SQLite store. The same cert serves both purposes for this single-tenant gateway.
        // Supply via MDEX_SIGNING_CERT_PATH + MDEX_SIGNING_CERT_PASS (or Mdex:SigningCert:* in config).
        // In dev/CI, fall back to ephemeral keys when no cert is configured.
        var certPath = builder.Configuration["Mdex:SigningCert:Path"]
            ?? Environment.GetEnvironmentVariable("MDEX_SIGNING_CERT_PATH");
        var certPass = builder.Configuration["Mdex:SigningCert:Password"]
            ?? Environment.GetEnvironmentVariable("MDEX_SIGNING_CERT_PASS") ?? "";
        if (!string.IsNullOrWhiteSpace(certPath))
        {
            var cert = new X509Certificate2(certPath, certPass);
            o.AddSigningCertificate(cert);
            o.AddEncryptionCertificate(cert);
        }
        else
        {
            o.AddEphemeralSigningKey();       // dev/CI only — tokens invalidated on restart
            o.AddEphemeralEncryptionKey();
        }
        o.DisableAccessTokenEncryption();   // keeps access-token JWTs readable; required by some MCP clients
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

// Create the OpenIddict SQLite schema if absent (before the client seed runs). No migrations —
// the schema is fixed by OpenIddict's EF model.
using (var initScope = app.Services.CreateScope())
    initScope.ServiceProvider.GetRequiredService<DbContext>().Database.EnsureCreated();

var fwdOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
fwdOptions.KnownNetworks.Clear();
fwdOptions.KnownProxies.Clear();
app.UseForwardedHeaders(fwdOptions);

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
