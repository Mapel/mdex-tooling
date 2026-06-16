using System.Text;
using Mdex.Core.Cli;
using Mdex.Core.Vault;

namespace Mdex.Core.Commands.Write;

/// <summary>
/// Convention-aware move/rename (pulled up from fullwrite). Frontmatter survives, path-less
/// wikilinks survive, and the <c>type</c> is re-derived from the destination folder (the archive
/// keeps its type). Covers rename + move-to-archive + general relocation — no separate tool needed.
/// Parent dirs are created; existing targets are never overwritten.
/// </summary>
public sealed class MoveCommand : ICommand
{
    public string Name => "move";
    public string Usage => "move <from> <to>";
    public string Summary => "Move/rename a file or directory; re-derive type from the destination folder.";

    // Destination prefix → frontmatter type. Anything else (e.g. resources/archive/…) keeps the existing type.
    static readonly (string Prefix, string Type)[] TypeByDir =
    {
        ("zettel/", "zettel"),
        ("projects/", "project"),
        ("areas/", "area"),
        ("resources/references/", "reference"),
        ("inbox/", "inbox"),
    };

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var from = reader.Positional(0) ?? throw new CommandException("move needs <from> <to>");
        var to = reader.Positional(1) ?? throw new CommandException("move needs <from> <to>");

        var fromAbs = ctx.Guard.ResolveInside(from);
        var toAbs = ctx.Guard.ResolveInside(to);
        if (!File.Exists(fromAbs) && !Directory.Exists(fromAbs)) throw new CommandException($"not found: {from}");
        if (File.Exists(toAbs) || Directory.Exists(toAbs)) throw new CommandException($"target already exists: {to}");
        Directory.CreateDirectory(Path.GetDirectoryName(toAbs)!);

        var isDir = Directory.Exists(fromAbs);
        if (isDir) Directory.Move(fromAbs, toAbs);
        else File.Move(fromAbs, toAbs, overwrite: false);

        var relTo = ctx.Guard.ToRelative(toAbs);

        // For a single markdown note, keep frontmatter intact but re-derive type + bump modified.
        if (!isDir && relTo.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var derived = TypeByDir.FirstOrDefault(t => relTo.StartsWith(t.Prefix, StringComparison.OrdinalIgnoreCase)).Type;
            var text = File.ReadAllText(toAbs).Replace("\r\n", "\n");
            if (derived is not null) text = FrontmatterEditor.SetField(text, "type", derived);
            text = FrontmatterEditor.SetField(text, "modified", DateTime.Now.ToString("yyyy-MM-dd"));
            File.WriteAllText(toAbs, text, new UTF8Encoding(false));
        }

        return CommandResult.Of(new { from = ctx.Guard.ToRelative(fromAbs), to = relTo },
            $"✓ {from} → {to}");
    }
}

/// <summary>
/// Create a directory (pulled up from fullwrite into write-intent). For deliberate empty-folder
/// scaffolding; create/move already auto-create parent dirs on their own.
/// </summary>
public sealed class MkdirCommand : ICommand
{
    public string Name => "mkdir";
    public string Usage => "mkdir <path>";
    public string Summary => "Create a directory at a vault-relative path.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var path = reader.Positional(0) ?? throw new CommandException("mkdir needs a path");
        var abs = ctx.Guard.ResolveInside(path);
        Directory.CreateDirectory(abs);
        return CommandResult.Of(new { path = ctx.Guard.ToRelative(abs) }, $"✓ mkdir {path}");
    }
}
