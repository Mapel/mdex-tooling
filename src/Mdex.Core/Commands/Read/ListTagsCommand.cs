using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

public sealed class ListTagsCommand : ICommand
{
    public string Name => "list-tags";
    public string Summary => "All tags with their counts.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var tags = ctx.Repo.LoadAll()
            .SelectMany(n => n.Frontmatter.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { tag = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).ThenBy(x => x.tag, StringComparer.Ordinal)
            .ToList();
        var human = string.Join('\n', tags.Select(t => $"{t.count,4}  {t.tag}"));
        return CommandResult.Of(new { tags }, human);
    }
}
