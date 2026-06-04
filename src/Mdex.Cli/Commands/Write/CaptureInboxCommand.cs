using System.Text;
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Write;

public sealed class CaptureInboxCommand : ICommand
{
    public string Name => "capture-inbox";
    public string Usage => "capture-inbox (--content <text> | stdin)";
    public string Summary => "Append a new timestamped inbox entry.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var content = reader.Content();
        if (string.IsNullOrWhiteSpace(content)) throw new CommandException("capture-inbox needs --content or piped stdin");

        var now = DateTime.Now;
        var slug = NextInboxSlug(ctx.VaultRoot, now);
        var rel = $"inbox/{slug}.md";
        var abs = ctx.Guard.ResolveInside(rel);

        var fm = ctx.Templates.BuildNote("inbox", slug, includeBody: false,
            created: now.ToString("yyyy-MM-ddTHH:mm:ss"), modified: now.ToString("yyyy-MM-dd"));
        File.WriteAllText(abs, fm + "\n" + content.TrimEnd() + "\n", new UTF8Encoding(false));

        return CommandResult.Of(new { path = rel, slug }, $"✓ {rel}");
    }

    static string NextInboxSlug(string root, DateTime now)
    {
        var prefix = now.ToString("yyyyMMdd");
        var dir = Path.Combine(root, "inbox");
        Directory.CreateDirectory(dir);
        for (var n = 1; ; n++)
        {
            var name = $"{prefix}-{n:D2}";
            if (!File.Exists(Path.Combine(dir, name + ".md"))) return name;
        }
    }
}
