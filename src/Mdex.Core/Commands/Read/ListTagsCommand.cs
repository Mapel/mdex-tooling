using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

public sealed class ListTagsCommand : ICommand
{
    public string Name => "list-tags";
    public string Usage => "list-tags [pattern] [--top N]";
    public string Summary => "All tags with their counts (sorted by frequency); optional pattern filter + top-N.";

    sealed record TagCount(string Tag, int Count);

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var pattern = reader.Positional(0);                 // absorbs the former search-tags
        var top = int.TryParse(reader.Flag("--top"), out var n) && n > 0 ? n : (int?)null;

        var query = ctx.Repo.LoadAll()
            .SelectMany(note => note.Frontmatter.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TagCount(g.Key, g.Count()))
            .Where(x => pattern is null || x.Tag.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Tag, StringComparer.Ordinal);

        var list = (top is not null ? query.Take(top.Value) : query)
            .Select(x => new { tag = x.Tag, count = x.Count }).ToList();
        var human = string.Join('\n', list.Select(t => $"{t.count,4}  {t.tag}"));
        return CommandResult.Of(new { tags = list }, human);
    }
}
