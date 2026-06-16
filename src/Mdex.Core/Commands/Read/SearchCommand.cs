using System.Text;
using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

public sealed class SearchCommand : ICommand
{
    public string Name => "search";
    public string Usage => "search <terms>... [--match all|any]";
    public string Summary => "Full-text substring search over title + body (one hit per note).";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        // Whitespace tokenises into AND-terms by default; multiple positionals are also terms.
        var terms = reader.Positionals
            .SelectMany(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        if (terms.Count == 0) throw new CommandException("search needs a query: mdex-cli search <terms>...");
        var matchAny = string.Equals(reader.Flag("--match"), "any", StringComparison.OrdinalIgnoreCase);

        var hits = new List<object>();
        var human = new StringBuilder();
        foreach (var note in ctx.Repo.LoadAll())
        {
            var haystack = note.Title + "\n" + note.Body;
            bool Hit(string t) => haystack.Contains(t, StringComparison.OrdinalIgnoreCase);
            if (!(matchAny ? terms.Any(Hit) : terms.All(Hit))) continue;

            // Snippet: the first line carrying any term keeps results readable (one hit per note).
            var lines = haystack.Split('\n');
            var idx = Array.FindIndex(lines, l => terms.Any(t => l.Contains(t, StringComparison.OrdinalIgnoreCase)));
            var line = idx < 0 ? 0 : idx + 1;
            var snippet = idx < 0 ? note.Title : lines[idx].Trim();
            hits.Add(new { path = note.RelativePath, slug = note.Slug, title = note.Title, line, snippet });
            human.AppendLine($"{note.RelativePath}:{line}: {snippet}");
        }

        var query = string.Join(' ', terms);
        var text = hits.Count == 0 ? $"No matches for '{query}'." : human.ToString().TrimEnd();
        return CommandResult.Of(new { query, hits }, text);
    }
}
