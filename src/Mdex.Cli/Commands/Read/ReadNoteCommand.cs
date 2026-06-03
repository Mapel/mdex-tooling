using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class ReadNoteCommand : ICommand
{
    public string Name => "read-note";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("read-note needs a slug or path");
        var note = ctx.Repo.FindBySlugOrPath(key) ?? throw new CommandException($"note not found: {key}");

        var data = new
        {
            path = note.RelativePath, slug = note.Slug, title = note.Title,
            frontmatter = new
            {
                type = note.Frontmatter.Type, created = note.Frontmatter.Created,
                modified = note.Frontmatter.Modified, tags = note.Frontmatter.Tags,
            },
            body = note.Body,
        };
        return CommandResult.Of(data, note.Body);
    }
}
