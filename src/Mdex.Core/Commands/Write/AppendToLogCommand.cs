using System.Text;
using Mdex.Core.Cli;
using Mdex.Core.Vault;

namespace Mdex.Core.Commands.Write;

public sealed class AppendToLogCommand : ICommand
{
    public string Name => "append-to-log";
    public string Usage => "append-to-log <slug|path> (--content <text> | stdin)";
    public string Summary => "Add a dated bullet under the note's ## Log section.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("append-to-log needs a project slug or path");
        var content = reader.Content();
        if (string.IsNullOrWhiteSpace(content)) throw new CommandException("append-to-log needs --content or stdin");

        var note = Resolver.Single(ctx, key, $"note not found: {key}");
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var entry = "- " + content.Trim();

        var lines = File.ReadAllText(note.Path).Replace("\r\n", "\n").Split('\n').ToList();
        var logIdx = lines.FindIndex(l => l.TrimEnd() == "## Log");
        if (logIdx < 0)
        {
            // append a fresh Log section at the end
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
            lines.Add("## Log");
            lines.Add($"### {today}");
            lines.Add(entry);
        }
        else
        {
            var dateIdx = lines.FindIndex(logIdx + 1, l => l.TrimEnd() == $"### {today}");
            if (dateIdx < 0)
            {
                lines.Insert(logIdx + 1, entry);
                lines.Insert(logIdx + 1, $"### {today}");
            }
            else
            {
                // insert right after the dated heading
                lines.Insert(dateIdx + 1, entry);
            }
        }

        var text = FrontmatterEditor.SetField(string.Join('\n', lines), "modified", today);
        if (!text.EndsWith("\n")) text += "\n";
        File.WriteAllText(note.Path, text, new UTF8Encoding(false));

        return CommandResult.Of(new { path = note.RelativePath }, $"✓ logged to {note.RelativePath}");
    }
}
