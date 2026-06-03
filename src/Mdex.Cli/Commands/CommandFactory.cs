using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands;

public static class CommandFactory
{
    public static CommandRegistry BuildRegistry()
    {
        var r = new CommandRegistry();
        r.Add(new VersionCommand());
        // read/write/fs commands are registered here as later tasks add them
        return r;
    }
}

public sealed class VersionCommand : ICommand
{
    public string Name => "version";
    public bool NeedsVault => false;
    public CommandResult Run(string[] args, CliContext ctx)
        => CommandResult.Of(new { version = "0.1.0" }, "mdext 0.1.0");
}
