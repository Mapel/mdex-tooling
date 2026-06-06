using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

public sealed class GetByTypeCommand : ICommand
{
    public string Name => "get-by-type";
    public string Usage => "get-by-type <type>";
    public string Summary => "Notes whose frontmatter type matches.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var type = reader.Positional(0) ?? throw new CommandException("get-by-type needs a type");
        var notes = ctx.Repo.LoadAll()
            .Where(n => string.Equals(n.Frontmatter.Type, type, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.ToSummary()).ToList();
        return Summaries.Result(notes);
    }
}
