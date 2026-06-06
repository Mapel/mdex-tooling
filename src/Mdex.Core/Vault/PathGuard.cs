using Mdex.Core.Cli;

namespace Mdex.Core.Vault;

public sealed class PathGuard
{
    readonly string _root;

    public PathGuard(string vaultRoot) => _root = Path.GetFullPath(vaultRoot);

    /// <summary>Resolve a vault-relative or absolute path; throw if it is empty, the root itself,
    /// or escapes the vault root. Returns the absolute (lexical) path to operate on.</summary>
    public string ResolveInside(string pathOrRelative)
    {
        if (string.IsNullOrWhiteSpace(pathOrRelative))
            throw new CommandException("Empty path is not allowed.");

        var combined = Path.IsPathRooted(pathOrRelative)
            ? pathOrRelative
            : Path.Combine(_root, pathOrRelative);
        var full = Path.GetFullPath(combined);

        // Containment is checked on the symlink-resolved form (Task 3); the lexical `full`
        // is what we return and operate on (a symlink that stays inside resolves inside anyway).
        var canonical = Canonicalize(full);
        var canonicalRoot = Canonicalize(_root);

        var cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSep = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;

        if (canonical.Equals(canonicalRoot, cmp) || !canonical.StartsWith(rootWithSep, cmp))
            throw new CommandException($"Path escapes the vault root: {pathOrRelative}");

        return full;
    }

    public string ToRelative(string absolute)
        => Path.GetRelativePath(_root, absolute).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Resolve symlinks over the existing prefix of <paramref name="fullPath"/>, segment by segment,
    /// so a symlink component pointing outside the root is reflected in the returned path. Segments
    /// that do not exist yet are appended literally (they cannot be symlinks).
    /// </summary>
    static string Canonicalize(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? "";
        var rest = fullPath[root.Length..];
        var segments = rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        foreach (var seg in segments)
        {
            current = Path.Combine(current, seg);
            FileSystemInfo? info = Directory.Exists(current) ? new DirectoryInfo(current)
                                 : File.Exists(current) ? new FileInfo(current)
                                 : null;
            if (info?.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                current = target.FullName;   // jump to the link's final target, then keep descending
        }
        return current;
    }
}
