using System.Text;
using Mdex.Core.Cli;
using Mdex.Core.Vault;

namespace Mdex.Core.Commands.Write;

/// <summary>
/// The single KI-facing path for mutating a note's <c>tags</c> (the system owns the rest of
/// the frontmatter). Exactly one mode is required: add (union), remove (subtract), or set
/// (replace all). Tags are passed as positionals after the target.
/// </summary>
public sealed class SetTagsCommand : ICommand
{
    public string Name => "set-tags";
    public string Usage => "set-tags <target> (--add | --remove | --set) <tag>...";
    public string Summary => "Mutate a note's tags (one mode required: --add, --remove, or --set).";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("set-tags needs a slug or path");

        var modes = new[] { "--add", "--remove", "--set" }.Where(reader.Has).ToArray();
        if (modes.Length == 0) throw new CommandException("set-tags needs one of --add, --remove, or --set");
        if (modes.Length > 1) throw new CommandException("set-tags takes exactly one of --add, --remove, or --set");
        var mode = modes[0];

        var values = reader.Positionals.Skip(1).ToList();
        if (values.Count == 0) throw new CommandException($"set-tags {mode} needs at least one tag");

        var note = Resolver.Single(ctx, key, $"note not found: {key}");
        var existing = note.Frontmatter.Tags;

        List<string> result = mode switch
        {
            "--set" => Dedup(values),
            "--add" => Dedup(existing.Concat(values)),
            "--remove" => existing.Where(t => !values.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList(),
            _ => throw new CommandException("unreachable"),
        };

        var text = File.ReadAllText(note.Path).Replace("\r\n", "\n");
        text = FrontmatterEditor.SetTags(text, result);
        text = FrontmatterEditor.SetField(text, "modified", DateTime.Now.ToString("yyyy-MM-dd"));
        File.WriteAllText(note.Path, text, new UTF8Encoding(false));

        return CommandResult.Of(new { path = note.RelativePath, tags = result },
            $"✓ {note.RelativePath}  tags: [{string.Join(", ", result)}]");
    }

    static List<string> Dedup(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var t in tags)
            if (!string.IsNullOrWhiteSpace(t) && seen.Add(t)) ordered.Add(t);
        return ordered;
    }
}
