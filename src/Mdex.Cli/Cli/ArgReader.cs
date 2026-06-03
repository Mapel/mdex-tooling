namespace Mdex.Cli.Cli;

/// <summary>Tiny positional + `--flag value` + `--flag` (bool) + stdin reader.</summary>
public sealed class ArgReader
{
    readonly List<string> _positionals = new();
    readonly Dictionary<string, string?> _flags = new(StringComparer.Ordinal);

    public ArgReader(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) { _flags[key] = args[++i]; }
                else _flags[key] = null; // boolean flag
            }
            else _positionals.Add(args[i]);
        }
    }

    public string? Positional(int i) => i < _positionals.Count ? _positionals[i] : null;
    public IReadOnlyList<string> Positionals => _positionals;
    public bool Has(string flag) => _flags.ContainsKey(flag);
    public string? Flag(string flag) => _flags.TryGetValue(flag, out var v) ? v : null;

    /// <summary>Content from --content, else from stdin if piped.</summary>
    public string Content()
    {
        var c = Flag("--content");
        if (c is not null) return c;
        if (Console.IsInputRedirected) return Console.In.ReadToEnd();
        return "";
    }
}
