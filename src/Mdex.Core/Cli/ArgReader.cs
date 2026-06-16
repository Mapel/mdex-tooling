namespace Mdex.Core.Cli;

/// <summary>Tiny positional + `--flag=value` + `--flag value` + `--flag` (bool) + `--` end-of-options + stdin reader.</summary>
public sealed class ArgReader
{
    readonly List<string> _positionals = new();
    readonly Dictionary<string, string?> _flags = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<string>> _multi = new(StringComparer.Ordinal);

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
                if (eq > 2) Record(arg[..eq], arg[(eq + 1)..]);                          // --key=value (key non-empty)
                else if (eq < 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    Record(arg, args[++i]);                                              // --key value
                else if (eq < 0) _flags[arg] = null;                                      // boolean --key
                else _positionals.Add(arg);                                               // malformed "--=…" → positional
            }
            else _positionals.Add(arg);
        }
    }

    // Last value wins for single-value lookups; every value is also kept for Multi().
    void Record(string key, string value)
    {
        _flags[key] = value;
        if (!_multi.TryGetValue(key, out var list)) _multi[key] = list = new List<string>();
        list.Add(value);
    }

    public string? Positional(int i) => i < _positionals.Count ? _positionals[i] : null;
    public IReadOnlyList<string> Positionals => _positionals;
    public bool Has(string flag) => _flags.ContainsKey(flag);
    public string? Flag(string flag) => _flags.TryGetValue(flag, out var v) ? v : null;

    /// <summary>All values for a repeated flag (e.g. <c>--tag a --tag b</c>); empty if absent.</summary>
    public IReadOnlyList<string> Multi(string flag) =>
        _multi.TryGetValue(flag, out var list) ? list : Array.Empty<string>();

    /// <summary>Content from --content, else from stdin if piped.</summary>
    public string Content()
    {
        var c = Flag("--content");
        if (c is not null) return c;
        if (Console.IsInputRedirected) return Console.In.ReadToEnd();
        return "";
    }
}
