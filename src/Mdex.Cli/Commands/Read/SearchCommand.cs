using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class SearchCommand : ICommand
{
    public string Name => "search";
    public string Usage => "search <query>";
    public string Summary => "Full-text substring search over title + body (one hit per note).";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var query = reader.Positional(0) ?? throw new CommandException("search needs a query: mdext search <query>");

        var hits = new List<object>();
        var human = new System.Text.StringBuilder();
        foreach (var note in ctx.Repo.LoadAll())
        {
            var haystack = note.Title + "\n" + note.Body;
            var lines = haystack.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var snippet = lines[i].Trim();
                    hits.Add(new { path = note.RelativePath, slug = note.Slug, title = note.Title, line = i + 1, snippet });
                    human.AppendLine($"{note.RelativePath}:{i + 1}: {snippet}");
                    break; // one hit per note keeps results readable
                }
            }
        }
        var text = hits.Count == 0 ? $"No matches for '{query}'." : human.ToString().TrimEnd();
        return CommandResult.Of(new { query, hits }, text);
    }
}
