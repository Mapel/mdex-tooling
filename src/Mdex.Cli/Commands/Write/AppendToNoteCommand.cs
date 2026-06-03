using System.Text;
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Write;

public sealed class AppendToNoteCommand : ICommand
{
    public string Name => "append-to-note";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("append-to-note needs a slug or path");
        var content = reader.Content();
        if (string.IsNullOrWhiteSpace(content)) throw new CommandException("append-to-note needs --content or stdin");

        var note = ctx.Repo.FindBySlugOrPath(key) ?? throw new CommandException($"note not found: {key}");
        var text = File.ReadAllText(note.Path).Replace("\r\n", "\n").TrimEnd('\n');
        text += "\n\n" + content.TrimEnd() + "\n";
        text = FrontmatterEditor.SetField(text, "modified", DateTime.Now.ToString("yyyy-MM-dd"));
        File.WriteAllText(note.Path, text, new UTF8Encoding(false));

        return CommandResult.Of(new { path = note.RelativePath }, $"✓ appended to {note.RelativePath}");
    }
}
