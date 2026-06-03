using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class GetByTypeCommand : ICommand
{
    public string Name => "get-by-type";

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
