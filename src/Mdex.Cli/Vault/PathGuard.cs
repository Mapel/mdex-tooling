using Mdex.Cli.Cli;

namespace Mdex.Cli.Vault;

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

    // Replaced with real symlink resolution in Task 3.
    static string Canonicalize(string path) => path;
}
