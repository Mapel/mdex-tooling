using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class GetByTagCommand : ICommand
{
    public string Name => "get-by-tag";
    public string Usage => "get-by-tag <tag>...";
    public string Summary => "Notes carrying all of the given tags.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var tags = reader.Positionals;
        if (tags.Count == 0) throw new CommandException("get-by-tag needs at least one tag");

        var notes = ctx.Repo.LoadAll()
            .Where(n => tags.All(t => n.Frontmatter.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            .Select(n => n.ToSummary()).ToList();
        return Summaries.Result(notes);
    }
}
