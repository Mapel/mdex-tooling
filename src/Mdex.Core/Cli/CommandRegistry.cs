namespace Mdex.Core.Cli;

public sealed class CommandRegistry
{
    readonly Dictionary<string, ICommand> _commands = new(StringComparer.Ordinal);
    readonly List<ICommand> _ordered = new();
    public void Add(ICommand c) { _commands[c.Name] = c; _ordered.Add(c); }
    public ICommand? Get(string name) => _commands.GetValueOrDefault(name);
    public IEnumerable<string> Names => _commands.Keys.OrderBy(n => n);
    public IReadOnlyList<ICommand> All => _ordered;   // registration order, for --help
}
