using System.Text;
using Mdex.Core.Cli;
using Mdex.Core.Vault;

namespace Mdex.Core.Commands.Write;

/// <summary>
/// Replace a note's body while preserving its frontmatter (and bumping modified). The
/// rewrite path for promote/restructure — not locked to any type, since the librarian
/// principle lives in the approval workflow, not the tool.
/// </summary>
public sealed class ReplaceBodyCommand : ICommand
{
    public string Name => "replace-body";
    public string Usage => "replace-body <target> (--content <text> | stdin)";
    public string Summary => "Replace a note's body, preserving its frontmatter.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("replace-body needs a slug or path");
        var content = reader.Content();
        if (string.IsNullOrWhiteSpace(content)) throw new CommandException("replace-body needs --content or stdin");

        var note = Resolver.Single(ctx, key, $"note not found: {key}");
        var text = File.ReadAllText(note.Path).Replace("\r\n", "\n");

        var frontmatter = "";
        if (text.StartsWith("---\n"))
        {
            var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end >= 0)
            {
                var afterMarker = text.IndexOf('\n', end + 1);
                frontmatter = afterMarker < 0 ? text : text[..(afterMarker + 1)];
            }
        }

        var rebuilt = frontmatter + "\n" + content.TrimEnd('\n') + "\n";
        rebuilt = FrontmatterEditor.SetField(rebuilt, "modified", DateTime.Now.ToString("yyyy-MM-dd"));
        File.WriteAllText(note.Path, rebuilt, new UTF8Encoding(false));

        return CommandResult.Of(new { path = note.RelativePath }, $"✓ replaced body of {note.RelativePath}");
    }
}
