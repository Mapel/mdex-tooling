namespace Mdex.Cli.Cli;

/// <summary>Tiny positional + `--flag value` + `--flag` (bool) + stdin reader.</summary>
public sealed class ArgReader
{
    readonly List<string> _positionals = new();
    readonly Dictionary<string, string?> _flags = new(StringComparer.Ordinal);

    public ArgReader(string[] args)
    {
        var positionalsOnly = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (!positionalsOnly && arg == "--") { positionalsOnly = true; continue; }  // end-of-options

            if (!positionalsOnly && arg.StartsWith("--"))
            {
                var eq = arg.IndexOf('=');
                if (eq >= 0) _flags[arg[..eq]] = arg[(eq + 1)..];                         // --key=value
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    _flags[arg] = args[++i];                                              // --key value
                else _flags[arg] = null;                                                 // boolean --key
            }
            else _positionals.Add(arg);
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
