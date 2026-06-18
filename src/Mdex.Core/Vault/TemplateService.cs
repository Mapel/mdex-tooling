using System.Globalization;
using System.Text;
using Mdex.Core.Cli;

namespace Mdex.Core.Vault;

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

        // No implicit body template — the body is opt-in via bodyTemplate, uniform across all types.
        // (KI workflows almost always replace/append the body after create, so a seeded skeleton is
        // usually discarded; templates are instead a convention reference via list-templates.)
        if (includeBody && bodyTemplate is not null)
        {
            var bodyPath = Path.Combine(Dir, $"body-{bodyTemplate}.md");
            if (!File.Exists(bodyPath))
                throw new CommandException(
                    $"unknown body template '{bodyTemplate}'. Available: {string.Join(", ", AvailableBodyTemplates())}");
            sb.Append('\n').Append("# ").Append(TitleCase(slug)).Append("\n\n");
            sb.Append(File.ReadAllText(bodyPath).Replace("\r\n", "\n"));
        }
        return sb.ToString();
    }

    /// <summary>Names of the available body templates (the <c>body-NAME.md</c> files), for
    /// list-templates and for error messages. Empty if the templates dir is absent.</summary>
    public IReadOnlyList<string> AvailableBodyTemplates() =>
        Directory.Exists(Dir)
            ? Directory.GetFiles(Dir, "body-*.md")
                .Select(f => Path.GetFileNameWithoutExtension(f)["body-".Length..])
                .OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : [];

    /// <summary>Reads a body template's content by name, or null if it doesn't exist.</summary>
    public string? ReadBodyTemplate(string name)
    {
        var path = Path.Combine(Dir, $"body-{name}.md");
        return File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : null;
    }

    static string TitleCase(string slug)
    {
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]);
        return string.Join(' ', words);
    }
}
