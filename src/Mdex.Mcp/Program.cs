using Mdex.Core.Cli;
using Mdex.Core.Security;
using Mdex.Core.Vault;
using Mdex.Mcp;
using Mdex.Mcp.Tools;
using Mdex.Mcp.Tools.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// --- fail-closed launch flags (no --role ⇒ refuse to start) ---
LaunchOptions opts;
try
{
    opts = LaunchOptions.Parse(args);
}
catch (LaunchOptionsException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine("Usage: mdex-mcp --role <read|write-intent|fullwrite> [--root <vault>]");
    return 1;
}

// --- pin the vault root (flag → $MDEX_ROOT → config), shared by every call this session ---
var engine = CommandEngine.Default();
var vaultRoot = VaultResolver.Resolve(opts.Root);

var builder = Host.CreateApplicationBuilder();   // no args: we parse our own flags

// stdout is the MCP transport — send every log line to stderr instead.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(engine);
builder.Services.AddSingleton(new MdexBackend(engine, vaultRoot,
    msg => Console.Error.WriteLine(msg)));
builder.Services.AddSingleton<IRoleProvider>(new FixedRoleProvider(opts.Role));
builder.Services.AddScoped<ToolAuthorizer>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(ReadTools).Assembly);

await builder.Build().RunAsync();
return 0;
