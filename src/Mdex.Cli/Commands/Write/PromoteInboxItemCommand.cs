using System.Text;
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Write;

public sealed class PromoteInboxItemCommand : ICommand
{
    public string Name => "promote-inbox-item";

    static readonly Dictionary<string, string> BaseDirs = new()
    {
        ["zettel"] = "zettel", ["project"] = "projects",
        ["area"] = "areas", ["reference"] = "resources/references",
    };

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var sourceKey = reader.Positional(0) ?? throw new CommandException("promote-inbox-item needs an inbox slug");
        var targetType = reader.Positional(1) ?? throw new CommandException("promote-inbox-item needs a target type");
        if (!BaseDirs.TryGetValue(targetType, out var baseDir))
            throw new CommandException($"unknown target type '{targetType}'");

        var source = ctx.Repo.FindBySlugOrPath(sourceKey) ?? throw new CommandException($"inbox item not found: {sourceKey}");
        if (source.Frontmatter.Type != "inbox")
            throw new CommandException($"not an inbox item: {sourceKey}");

        var name = reader.Flag("--name") ?? source.Slug;
        var slug = Slugifier.Slugify(name);
        var rel = $"{baseDir}/{slug}.md";
        var abs = ctx.Guard.ResolveInside(rel);
        if (File.Exists(abs)) throw new CommandException($"target already exists: {rel}");
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);

        var created = source.Frontmatter.Created ?? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var modified = DateTime.Now.ToString("yyyy-MM-dd");
        var header = ctx.Templates.BuildNote(targetType, slug, includeBody: false, created: created, modified: modified);
        var text = header + "\n" + source.Body.TrimEnd('\n') + "\n";
        File.WriteAllText(abs, text, new UTF8Encoding(false));

        File.Delete(source.Path);   // original inbox entry removed (revertible via git history)

        return CommandResult.Of(new { from = source.RelativePath, to = rel },
            $"✓ {source.RelativePath} → {rel}");
    }
}
