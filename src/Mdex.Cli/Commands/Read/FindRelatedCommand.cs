using System.Text.RegularExpressions;
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Read;

public sealed class FindRelatedCommand : ICommand
{
    public string Name => "find-related";
    static readonly Regex WikiLink = new(@"\[\[([^\]]+)\]\]");

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var slug = reader.Positional(0) ?? throw new CommandException("find-related needs a slug");
        var hops = int.TryParse(reader.Flag("--hops"), out var h) ? Math.Max(1, h) : 1;

        var all = ctx.Repo.LoadAll();
        if (all.All(n => !string.Equals(n.Slug, slug, StringComparison.OrdinalIgnoreCase)))
            throw new CommandException($"note not found: {slug}");

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug };
        var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug };

        for (var hop = 0; hop < hops; hop++)
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
            foreach (var s in next) if (visited.Add(s)) frontier.Add(s);
            frontier = next;
        }
        visited.Remove(slug);

        var related = all.Where(n => visited.Contains(n.Slug)).Select(n => n.ToSummary()).ToList();
        var human = related.Count == 0 ? "No related notes." : string.Join('\n', related.Select(n => $"{n.Path}  {n.Title}"));
        return CommandResult.Of(new { slug, related }, human);
    }
}
