using System.Text.RegularExpressions;

namespace Mdex.Cli.Vault;

public sealed class NoteRepository(string vaultRoot, PathGuard guard)
{
    static readonly string[] ExcludedDirs =
        { ".git", "resources/templates", "resources/scripts", "resources/assets" };

    public IReadOnlyList<Note> LoadAll()
    {
        var notes = new List<Note>();
        foreach (var file in Directory.EnumerateFiles(vaultRoot, "*.md", SearchOption.AllDirectories))
        {
            var rel = guard.ToRelative(file);
            if (ExcludedDirs.Any(d => rel.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)))
                continue;
            notes.Add(Build(file, rel));
        }
        return notes;
    }

    public Note? FindBySlugOrPath(string slugOrPath)
    {
        // direct path hit first
        var asPath = Path.Combine(vaultRoot, slugOrPath.Replace('/', Path.DirectorySeparatorChar));
        if (!asPath.EndsWith(".md")) asPath += ".md";
        if (File.Exists(asPath))
            return Build(asPath, guard.ToRelative(asPath));

        return LoadAll().FirstOrDefault(n =>
            string.Equals(n.Slug, slugOrPath, StringComparison.OrdinalIgnoreCase));
    }

    static readonly Regex H1 = new(@"^#\s+(.+)$", RegexOptions.Multiline);

    Note Build(string absolute, string relative)
    {
        var text = File.ReadAllText(absolute);
        var parsed = FrontmatterParser.Parse(text);
        var slug = Path.GetFileNameWithoutExtension(absolute);
        var m = H1.Match(parsed.Body);
        var title = m.Success ? m.Groups[1].Value.Trim() : slug;
        return new Note
        {
            Path = absolute, RelativePath = relative, Slug = slug,
            Title = title, Frontmatter = parsed.Frontmatter, Body = parsed.Body,
        };
    }
}
