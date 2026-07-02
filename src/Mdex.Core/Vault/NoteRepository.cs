using System.Text.RegularExpressions;
using Mdex.Core.Cli;

namespace Mdex.Core.Vault;

public sealed class NoteRepository(string vaultRoot, PathGuard guard)
{
    static readonly string[] ExcludedDirs =
        { ".git", "resources/templates", "resources/scripts", "resources/assets" };

    // Extensions we treat as searchable/readable text. This is the cheap primary filter (decided from
    // the name, no read). The NUL-byte probe in ReadTextFile is the secondary safeguard: it catches a
    // binary payload wearing a text extension (e.g. an image saved as .md) so we never search or serve
    // its bytes. Anything not listed here is matched by name/path only, never by content.
    static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".txt", ".text", ".rst", ".org", ".adoc", ".tex", ".bib",
        ".html", ".htm", ".xml", ".svg", ".css", ".scss", ".less",
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".vue",
        ".json", ".json5", ".jsonc", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".properties", ".env",
        ".csv", ".tsv", ".log",
        ".sh", ".bash", ".zsh", ".fish", ".ps1", ".bat", ".cmd",
        ".py", ".rb", ".pl", ".php", ".lua", ".r",
        ".c", ".h", ".cpp", ".hpp", ".cc", ".cs", ".java", ".kt", ".go", ".rs", ".swift", ".sql",
        ".gitignore", ".gitattributes", ".editorconfig", ".dockerignore",
    };

    const long MaxTextBytes = 2 * 1024 * 1024;   // read/search content cap; larger files match by name only

    // True if the file *name* looks like text (extension whitelist; extensionless files like LICENSE
    // or Makefile count). Cheap — no I/O; content is only read once this passes.
    static bool HasTextName(string relOrName)
    {
        var ext = Path.GetExtension(Path.GetFileName(relOrName));
        return ext.Length == 0 || TextExtensions.Contains(ext);
    }

    // Read a text-candidate file. (null, true): a NUL byte in the first 8 KB betrays binary content.
    // (null, false): over the size cap (still name-searchable). (content, false): plain text.
    static (string? Content, bool Binary) ReadTextFile(string abs)
    {
        try
        {
            var length = new FileInfo(abs).Length;
            using var fs = File.OpenRead(abs);
            var probeLen = (int)Math.Min(8192, length);
            if (probeLen > 0)
            {
                var probe = new byte[probeLen];
                var read = fs.Read(probe, 0, probeLen);
                if (Array.IndexOf(probe, (byte)0, 0, read) >= 0) return (null, true);
            }
            if (length > MaxTextBytes) return (null, false);
            fs.Position = 0;
            using var sr = new StreamReader(fs);
            return (sr.ReadToEnd(), false);
        }
        catch { return (null, false); }
    }

    /// <summary>Every non-<c>.md</c> file except those under <c>.git</c>, with its text content when the
    /// file looks like text (extension whitelist + NUL-byte safeguard). Content is null for binary or
    /// oversized files; those are still yielded so <c>search</c> can match them by name/path.</summary>
    public IEnumerable<(string RelativePath, string? Content)> LoadNonNoteFiles()
    {
        foreach (var file in Directory.EnumerateFiles(vaultRoot, "*", SearchOption.AllDirectories))
        {
            var rel = guard.ToRelative(file);
            if (rel == ".git" || rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)) continue;
            if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
            string? content = HasTextName(rel) ? ReadTextFile(file).Content : null;
            yield return (rel, content);
        }
    }

    /// <summary>Read an in-vault file by path (any extension) for direct display. False if the path is
    /// empty, escapes the vault, or names no existing file. <paramref name="binary"/> is true when the
    /// file is not safe to show as text (non-text name, NUL byte, or read failure).</summary>
    public bool TryReadFile(string pathArg, out string relativePath, out string? content, out bool binary)
    {
        relativePath = ""; content = null; binary = false;
        string abs;
        try { abs = guard.ResolveInside(pathArg.Replace('/', Path.DirectorySeparatorChar)); }
        catch (CommandException) { return false; }
        if (!File.Exists(abs)) return false;

        relativePath = guard.ToRelative(abs);
        if (!HasTextName(relativePath)) { binary = true; return true; }
        var (text, isBinary) = ReadTextFile(abs);
        if (isBinary) { binary = true; return true; }
        content = text ?? "";   // oversized text → empty content, but not binary
        return true;
    }

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
