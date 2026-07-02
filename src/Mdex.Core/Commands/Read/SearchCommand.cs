using System.Text;
using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

public sealed class SearchCommand : ICommand
{
    public string Name => "search";
    public string Usage => "search <terms>... [--match all|any]";
    public string Summary => "Full-text substring search over all files' names and text content (one hit per file).";

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

        // One record per file. The path is part of the haystack so file/folder names match too;
        // the snippet is drawn from the content only (a name-only match falls back to the title).
        void Emit(string rel, string? slug, string title, string content)
        {
            var haystack = rel + "\n" + content;
            bool Hit(string t) => haystack.Contains(t, StringComparison.OrdinalIgnoreCase);
            if (!(matchAny ? terms.Any(Hit) : terms.All(Hit))) return;

            var lines = content.Split('\n');
            var idx = Array.FindIndex(lines, l => terms.Any(t => l.Contains(t, StringComparison.OrdinalIgnoreCase)));
            var line = idx < 0 ? 0 : idx + 1;
            var snippet = idx < 0 ? title : lines[idx].Trim();
            hits.Add(new { path = rel, slug, title, line, snippet });
            human.AppendLine($"{rel}:{line}: {snippet}");
        }

        // Markdown notes carry frontmatter-aware title/slug; every other file is matched by name and,
        // when it is text, by content (binary/oversized files match by name only — content is empty).
        foreach (var note in ctx.Repo.LoadAll())
            Emit(note.RelativePath, note.Slug, note.Title, note.Title + "\n" + note.Body);
        foreach (var (rel, content) in ctx.Repo.LoadNonNoteFiles())
            Emit(rel, null, Path.GetFileName(rel), content ?? "");

        var query = string.Join(' ', terms);
        var text = hits.Count == 0 ? $"No matches for '{query}'." : human.ToString().TrimEnd();
        return CommandResult.Of(new { query, hits }, text);
    }
}
