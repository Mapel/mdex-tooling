using Mdex.Cli.Cli;
using Mdex.Cli.Commands;
using Mdex.Cli.Vault;

var registry = CommandFactory.BuildRegistry();

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine("mdext <command> [args] [--root <path>] [--json]");
    Console.WriteLine("Commands: " + string.Join(", ", registry.Names));
    return 0;
}

var verb = args[0];
var rest = args[1..];

// pull global flags out of the tail
var json = rest.Contains("--json");
string? rootFlag = null;
var cleaned = new List<string>();
for (var i = 0; i < rest.Length; i++)
{
    if (rest[i] == "--json") continue;
    if (rest[i] == "--root") { rootFlag = i + 1 < rest.Length ? rest[++i] : null; continue; }
    cleaned.Add(rest[i]);
}

var command = registry.Get(verb);
if (command is null)
{
    Console.Error.WriteLine(json ? JsonEnvelope.Err(verb, $"unknown command '{verb}'")
                                  : $"mdext: unknown command '{verb}'");
    return 1;
}

try
{
    CliContext ctx;
    if (command.NeedsVault)
    {
        var root = VaultResolver.Resolve(rootFlag);
        var guard = new PathGuard(root);
        ctx = new CliContext
        {
            VaultRoot = root, Json = json,
            Repo = new NoteRepository(root, guard),
            Guard = guard,
            Templates = new TemplateService(root),
        };
    }
    else
    {
        // commands that don't touch the vault still get a context shell
        ctx = new CliContext
        {
            VaultRoot = "", Json = json,
            Repo = null!, Guard = null!, Templates = null!,
        };
    }

    var result = command.Run(cleaned.ToArray(), ctx);
    Console.WriteLine(json ? JsonEnvelope.Ok(verb, result.Data) : result.HumanText);
    return 0;
}
catch (CommandException ex)
{
    Console.Error.WriteLine(json ? JsonEnvelope.Err(verb, ex.Message) : $"Error: {ex.Message}");
    return 1;
}
