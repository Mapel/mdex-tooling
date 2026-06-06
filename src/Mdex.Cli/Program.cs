using Mdex.Core.Cli;

var engine = CommandEngine.Default();

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine("mdex-cli <command> [args] [--root <path>] [--json]");
    Console.WriteLine();
    Console.WriteLine("Global flags:");
    Console.WriteLine("  --root <path>   vault root (else $MDEX_ROOT, else config)");
    Console.WriteLine("  --json          emit the JSON envelope instead of human text");
    Console.WriteLine();

    static string GroupOf(ICommand c) => c.GetType().Namespace switch
    {
        "Mdex.Core.Commands.Read" => "Read",
        "Mdex.Core.Commands.Write" => "Write",
        "Mdex.Core.Commands.Fs" => "Raw FS (guarded to vault root)",
        _ => "Meta",
    };

    var width = Math.Min(engine.Commands.Max(c => c.Usage.Length), 44);
    foreach (var group in engine.Commands.GroupBy(GroupOf))
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

try
{
    var result = engine.Run(verb, cleaned.ToArray(), rootFlag);
    Console.WriteLine(json ? JsonEnvelope.Ok(verb, result.Data) : result.HumanText);
    return 0;
}
catch (CommandException ex)
{
    Console.Error.WriteLine(json ? JsonEnvelope.Err(verb, ex.Message, ex.Details) : $"Error: {ex.Message}");
    return 1;
}
