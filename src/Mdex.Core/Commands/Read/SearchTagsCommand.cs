using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

public sealed class SearchTagsCommand : ICommand
{
    public string Name => "search-tags";
    public string Usage => "search-tags <pattern>";
    public string Summary => "Tags containing the given substring.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var pattern = reader.Positional(0) ?? throw new CommandException("search-tags needs a pattern");
        var tags = ctx.Repo.LoadAll()
            .SelectMany(n => n.Frontmatter.Tags)
            .Where(t => t.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        return CommandResult.Of(new { pattern, tags }, string.Join('\n', tags));
    }
}
