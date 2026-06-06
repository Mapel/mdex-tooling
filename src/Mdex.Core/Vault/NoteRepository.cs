using System.Text.RegularExpressions;
using Mdex.Core.Cli;

namespace Mdex.Core.Vault;

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

    /// <summary>
    /// Resolves a slug-or-path argument. A direct path hit is unambiguous (one note);
    /// a bare slug may match several notes in different folders — all are returned so the
    /// caller can detect the ambiguity instead of silently picking the first.
    /// </summary>
    public IReadOnlyList<Note> FindBySlugOrPath(string slugOrPath)
    {
        // direct path hit first — unambiguous, but must stay inside the vault
        if (TryResolveInside(slugOrPath, out var asPath) && File.Exists(asPath))
            return new[] { Build(asPath, guard.ToRelative(asPath)) };

        return LoadAll().Where(n =>
            string.Equals(n.Slug, slugOrPath, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>True only if <paramref name="slugOrPath"/> names a `.md` file inside the vault.
    /// An escaping or empty candidate yields no direct hit (caller falls back to slug search).</summary>
    bool TryResolveInside(string slugOrPath, out string abs)
    {
        abs = "";
        var rel = slugOrPath.Replace('/', Path.DirectorySeparatorChar);
        if (!rel.EndsWith(".md")) rel += ".md";
        try { abs = guard.ResolveInside(rel); return true; }
        catch (CommandException) { return false; }   // outside the vault → not a direct hit
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
