using System.Globalization;
using System.Text;
using Mdex.Cli.Cli;

namespace Mdex.Cli.Vault;

public sealed class TemplateService(string vaultRoot)
{
    string Dir => Path.Combine(vaultRoot, "resources", "templates");

    /// <summary>Build full note text for a new note (frontmatter + optional H1 + body template).</summary>
    public string BuildNote(string type, string slug, bool includeBody, string created, string modified, string? bodyTemplate = null)
    {
        var fmPath = Path.Combine(Dir, $"frontmatter-{type}.md");
        if (!File.Exists(fmPath)) throw new CommandException($"frontmatter template missing: {fmPath}");
        var fm = File.ReadAllText(fmPath).Replace("\r\n", "\n")
            .Replace("{{created}}", created).Replace("{{modified}}", modified).TrimEnd('\n');

        var sb = new StringBuilder();
        sb.Append(fm).Append('\n');

        var tpl = bodyTemplate ?? (type == "project" ? "project-default" : null);
        if (includeBody && tpl is not null)
        {
            var bodyPath = Path.Combine(Dir, $"body-{tpl}.md");
            if (!File.Exists(bodyPath)) throw new CommandException($"body template missing: {bodyPath}");
            sb.Append('\n').Append("# ").Append(TitleCase(slug)).Append("\n\n");
            sb.Append(File.ReadAllText(bodyPath).Replace("\r\n", "\n"));
        }
        return sb.ToString();
    }

    static string TitleCase(string slug)
    {
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]);
        return string.Join(' ', words);
    }
}
