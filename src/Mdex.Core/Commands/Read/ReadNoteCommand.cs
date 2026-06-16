using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

public sealed class ReadNoteCommand : ICommand
{
    public string Name => "read-note";
    public string Usage => "read-note <target>...";
    public string Summary => "Print one or more notes' frontmatter and body (partial success).";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var targets = reader.Positionals;
        if (targets.Count == 0) throw new CommandException("read-note needs at least one slug or path");

        var notes = new List<object>();
        var errors = new List<object>();
        var bodies = new List<string>();
        var errorLines = new List<string>();
        CommandException? firstFailure = null;

        foreach (var key in targets)
        {
            try
            {
                var note = Resolver.Single(ctx, key, $"note not found: {key}");
                notes.Add(new
                {
                    path = note.RelativePath, slug = note.Slug, title = note.Title,
                    frontmatter = new
                    {
                        type = note.Frontmatter.Type, created = note.Frontmatter.Created,
                        modified = note.Frontmatter.Modified, tags = note.Frontmatter.Tags,
                    },
                    body = note.Body,
                });
                bodies.Add(note.Body);
            }
            catch (CommandException ex)
            {
                firstFailure ??= ex;
                errors.Add(new { target = key, message = ex.Message });
                errorLines.Add($"! {key}: {ex.Message}");
            }
        }

        // Nothing resolved at all → fail like a single read (preserves not-found / ambiguous errors,
        // details and all). Partial success (≥1 found) returns the rest with per-target errors.
        if (notes.Count == 0) throw firstFailure!;

        var human = string.Join("\n\n---\n\n", bodies);
        if (errorLines.Count > 0) human += "\n\n" + string.Join('\n', errorLines);

        return CommandResult.Of(new { notes, errors }, human);
    }
}
