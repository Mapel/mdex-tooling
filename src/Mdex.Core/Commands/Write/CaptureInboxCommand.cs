using System.Text;
using Mdex.Core.Cli;
using Mdex.Core.Vault;

namespace Mdex.Core.Commands.Write;

public sealed class CaptureInboxCommand : ICommand
{
    public string Name => "capture-inbox";
    public string Usage => "capture-inbox --title <t> (--content <text> | stdin)";
    public string Summary => "Create a new inbox entry (title sets the H1 and derives the filename).";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var title = reader.Flag("--title");
        if (string.IsNullOrWhiteSpace(title)) throw new CommandException("capture-inbox needs --title");
        var content = reader.Content();
        if (string.IsNullOrWhiteSpace(content)) throw new CommandException("capture-inbox needs --content or piped stdin");

        var now = DateTime.Now;
        var slug = NextInboxSlug(ctx.VaultRoot, now, title!);
        var rel = $"inbox/{slug}.md";
        var abs = ctx.Guard.ResolveInside(rel);

        var fm = ctx.Templates.BuildNote("inbox", slug, includeBody: false,
            created: now.ToString("yyyy-MM-ddTHH:mm:ss"), modified: now.ToString("yyyy-MM-dd"));
        var body = $"# {title!.Trim()}\n\n{content.TrimEnd()}\n";
        File.WriteAllText(abs, fm + "\n" + body, new UTF8Encoding(false));

        return CommandResult.Of(new { path = rel, slug }, $"✓ {rel}");
    }

    // Filename scheme: YYYYMMDD-<title-slug>.md — a tooling convention for sorting/dedup, not a
    // vault law (mobile captures without this scheme stay valid). Collisions get a -2, -3 … suffix.
    static string NextInboxSlug(string root, DateTime now, string title)
    {
        var prefix = now.ToString("yyyyMMdd");
        var titleSlug = Slugifier.Slugify(title);
        if (titleSlug.Length == 0) titleSlug = "untitled";
        var dir = Path.Combine(root, "inbox");
        Directory.CreateDirectory(dir);

        var baseName = $"{prefix}-{titleSlug}";
        if (!File.Exists(Path.Combine(dir, baseName + ".md"))) return baseName;
        for (var n = 2; ; n++)
        {
            var name = $"{baseName}-{n}";
            if (!File.Exists(Path.Combine(dir, name + ".md"))) return name;
        }
    }
}
