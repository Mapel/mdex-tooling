using System.Text;
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Write;

public sealed class CreateCommand : ICommand
{
    public string Name => "create";
    public string Usage => "create <type> <name> [--no-body] [--body <tpl>]";
    public string Summary => "Create a note (type: zettel|project|area|reference).";

    static readonly Dictionary<string, string> BaseDirs = new()
    {
        ["zettel"] = "zettel",
        ["project"] = "projects",
        ["area"] = "areas",
        ["reference"] = "resources/references",
    };

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var type = reader.Positional(0) ?? throw new CommandException("create needs a type");
        if (type == "inbox") throw new CommandException("use capture-inbox for inbox entries");
        if (!BaseDirs.TryGetValue(type, out var baseDir))
            throw new CommandException($"unknown type '{type}'. Valid: zettel | project | area | reference");

        var name = reader.Positional(1) ?? throw new CommandException($"create {type} needs a name");
        var noBody = reader.Has("--no-body");
        var bodyTpl = reader.Flag("--body");

        var (relDir, slug) = ResolveTarget(baseDir, name, allowSubpath: type != "zettel");
        var rel = $"{relDir}/{slug}.md";
        var abs = ctx.Guard.ResolveInside(rel);
        if (File.Exists(abs)) throw new CommandException($"already exists: {rel}");
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);

        var now = DateTime.Now;
        var text = ctx.Templates.BuildNote(type, slug, includeBody: !noBody,
            created: now.ToString("yyyy-MM-ddTHH:mm:ss"), modified: now.ToString("yyyy-MM-dd"), bodyTemplate: bodyTpl);
        File.WriteAllText(abs, text, new UTF8Encoding(false));

        return CommandResult.Of(new { path = rel, slug, type }, $"✓ {rel}");
    }

    static (string relDir, string slug) ResolveTarget(string baseDir, string name, bool allowSubpath)
    {
        name = name.EndsWith(".md") ? name[..^3] : name;
        if (allowSubpath && name.Contains('/'))
        {
            var sub = name[..name.LastIndexOf('/')];
            var file = name[(name.LastIndexOf('/') + 1)..];
            return ($"{baseDir}/{sub}", Slugifier.Slugify(file));
        }
        return (baseDir, Slugifier.Slugify(name));
    }
}
