using System.Text.RegularExpressions;
using Mdex.Core.Cli;
using Mdex.Core.Vault;

namespace Mdex.Core.Commands.Read;

public sealed class FindRelatedCommand : ICommand
{
    public string Name => "find-related";
    public string Usage => "find-related <slug|path> [--depth N|all] [--direction in|out|both]";
    public string Summary => "Notes linked via [[wikilinks]]; default depth 1, both directions.";
    static readonly Regex WikiLink = new(@"\[\[([^\]]+)\]\]");

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("find-related needs a slug or path");
        var cap = ParseDepth(reader.Flag("--depth"));
        var direction = (reader.Flag("--direction") ?? "both").ToLowerInvariant();
        if (direction is not ("in" or "out" or "both"))
            throw new CommandException("--direction must be in, out, or both");
        var wantIn = direction is "in" or "both";
        var wantOut = direction is "out" or "both";

        // Resolve the start note unambiguously; the walk is slug-based (wikilinks reference slugs).
        var slug = Resolver.Single(ctx, key, $"note not found: {key}").Slug;
        var all = ctx.Repo.LoadAll();

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug };
        var directionOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug };

        // BFS. The frontier holds only newly-discovered notes each hop, so every note expands once —
        // cycles terminate and an exhausted component ends the walk early. Each note is direction-
        // labelled by how it was first reached (out = the target links to it; in = it links to target).
        for (var hop = 0; hop < cap && frontier.Count > 0; hop++)
        {
            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var current in frontier)
            {
                var note = all.FirstOrDefault(n => string.Equals(n.Slug, current, StringComparison.OrdinalIgnoreCase));
                if (wantOut && note is not null)
                    foreach (Match m in WikiLink.Matches(note.Body))
                        Discover(m.Groups[1].Value.Trim(), "out", next);

                if (wantIn)
                    foreach (var other in all)
                        if (WikiLink.Matches(other.Body).Any(m => string.Equals(m.Groups[1].Value.Trim(), current, StringComparison.OrdinalIgnoreCase)))
                            Discover(other.Slug, "in", next);
            }
            frontier = next;
        }
        visited.Remove(slug);

        var related = all.Where(n => visited.Contains(n.Slug)).Select(n =>
        {
            var s = n.ToSummary();
            return new
            {
                path = s.Path, slug = s.Slug, title = s.Title, type = s.Type,
                tags = s.Tags, created = s.Created, modified = s.Modified,
                direction = directionOf.TryGetValue(n.Slug, out var d) ? d : direction,
            };
        }).ToList();

        var human = related.Count == 0 ? "No related notes." : string.Join('\n', related.Select(n => $"{n.path}  ({n.direction})  {n.title}"));
        return CommandResult.Of(new { slug, direction, related }, human);

        void Discover(string candidate, string via, HashSet<string> bucket)
        {
            if (visited.Add(candidate))
            {
                directionOf[candidate] = via;
                bucket.Add(candidate);
            }
        }
    }

    static int ParseDepth(string? flag)
    {
        if (flag is null) return 1;
        if (string.Equals(flag, "all", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
        if (int.TryParse(flag, out var n) && n >= 1) return n;
        throw new CommandException("--depth must be a positive integer or 'all'");
    }
}
