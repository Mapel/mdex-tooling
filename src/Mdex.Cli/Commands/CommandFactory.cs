using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Read;

namespace Mdex.Cli.Commands;

public static class CommandFactory
{
    public static CommandRegistry BuildRegistry()
    {
        var r = new CommandRegistry();
        r.Add(new VersionCommand());

        // Read commands
        r.Add(new SearchCommand());
        r.Add(new ReadNoteCommand());
        r.Add(new GetByTagCommand());
        r.Add(new GetByTypeCommand());
        r.Add(new GetByDateRangeCommand());
        r.Add(new ListTagsCommand());
        r.Add(new SearchTagsCommand());
        r.Add(new ListCommand());
        r.Add(new FindRelatedCommand());

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
