using System.Text.RegularExpressions;
using Mdex.Core.Cli;
using Mdex.Core.Vault;

namespace Mdex.Core.Commands.Read;

public sealed class FindRelatedCommand : ICommand
{
    public string Name => "find-related";
    public string Usage => "find-related <slug|path> [--maxhops N] [--recursive]";
    public string Summary => "Notes linked via [[wikilinks]] (both directions); depth 1 by default.";
    static readonly Regex WikiLink = new(@"\[\[([^\]]+)\]\]");

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("find-related needs a slug or path");

        // --maxhops caps the depth; --recursive walks until no new notes appear. Given both,
        // --maxhops wins as the ceiling (recursive, but at most N levels). Default is 1.
        var recursive = reader.Has("--recursive");
        var cap = int.TryParse(reader.Flag("--maxhops"), out var n) ? Math.Max(1, n)
                : recursive ? int.MaxValue
                : 1;

        // Resolve the start note unambiguously (path or unique slug); the graph walk
        // below is slug-based because that is how wikilinks reference notes.
        var slug = Resolver.Single(ctx, key, $"note not found: {key}").Slug;

        var all = ctx.Repo.LoadAll();

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug };
        var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug };

        // BFS. The frontier holds only newly-discovered notes each hop, so every note is
        // expanded once — cycles terminate and an exhausted component ends the walk early.
        for (var hop = 0; hop < cap && frontier.Count > 0; hop++)
        {
            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var current in frontier)
            {
                var note = all.FirstOrDefault(n => string.Equals(n.Slug, current, StringComparison.OrdinalIgnoreCase));
                if (note is not null)
                    foreach (Match m in WikiLink.Matches(note.Body))   // outgoing
                        next.Add(m.Groups[1].Value.Trim());

                foreach (var other in all)                              // incoming
                    if (WikiLink.Matches(other.Body).Any(m => string.Equals(m.Groups[1].Value.Trim(), current, StringComparison.OrdinalIgnoreCase)))
                        next.Add(other.Slug);
            }
            frontier = next.Where(visited.Add).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        visited.Remove(slug);

        var related = all.Where(n => visited.Contains(n.Slug)).Select(n => n.ToSummary()).ToList();
        var human = related.Count == 0 ? "No related notes." : string.Join('\n', related.Select(n => $"{n.Path}  {n.Title}"));
        return CommandResult.Of(new { slug, related }, human);
    }
}
