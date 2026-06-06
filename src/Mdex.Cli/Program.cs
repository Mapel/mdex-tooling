using Mdex.Core.Cli;
using Mdex.Core.Commands;
using Mdex.Core.Vault;

var registry = CommandFactory.BuildRegistry();

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine("mdext <command> [args] [--root <path>] [--json]");
    Console.WriteLine();
    Console.WriteLine("Global flags:");
    Console.WriteLine("  --root <path>   vault root (else $MDEX_ROOT, else config)");
    Console.WriteLine("  --json          emit the stable JSON envelope instead of human text");
    Console.WriteLine();

    static string GroupOf(ICommand c) => c.GetType().Namespace switch
    {
        "Mdex.Core.Commands.Read" => "Read",
        "Mdex.Core.Commands.Write" => "Write",
        "Mdex.Core.Commands.Fs" => "Raw FS (guarded to vault root)",
        _ => "Meta",
    };

    // Align summaries in a column, but cap the width so one long usage line
    // doesn't push every description off to the right; longer ones wrap instead.
    var width = Math.Min(registry.All.Max(c => c.Usage.Length), 44);
    foreach (var group in registry.All.GroupBy(GroupOf))
    {
        Console.WriteLine(group.Key + ":");
        foreach (var c in group)
        {
            if (c.Usage.Length <= width)
                Console.WriteLine($"  {c.Usage.PadRight(width)}  {c.Summary}");
            else
            {
                Console.WriteLine($"  {c.Usage}");
                Console.WriteLine($"  {new string(' ', width)}  {c.Summary}");
            }
        }
        Console.WriteLine();
    }
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
    Console.Error.WriteLine(json ? JsonEnvelope.Err(verb, ex.Message, ex.Details) : $"Error: {ex.Message}");
    return 1;
}
