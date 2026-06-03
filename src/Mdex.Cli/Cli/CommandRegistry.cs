namespace Mdex.Cli.Cli;

public sealed class CommandRegistry
{
    readonly Dictionary<string, ICommand> _commands = new(StringComparer.Ordinal);
    public void Add(ICommand c) => _commands[c.Name] = c;
    public ICommand? Get(string name) => _commands.GetValueOrDefault(name);
    public IEnumerable<string> Names => _commands.Keys.OrderBy(n => n);
}
