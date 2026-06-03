using Mdex.Cli.Cli;

namespace Mdex.Cli.Vault;

public sealed class PathGuard(string vaultRoot)
{
    readonly string _root = Path.GetFullPath(vaultRoot);

    /// <summary>Resolve a vault-relative or absolute path; throw if it escapes the vault root.</summary>
    public string ResolveInside(string pathOrRelative)
    {
        var combined = Path.IsPathRooted(pathOrRelative)
            ? pathOrRelative
            : Path.Combine(_root, pathOrRelative);
        var full = Path.GetFullPath(combined);

        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root : _root + Path.DirectorySeparatorChar;
        if (full != _root && !full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new CommandException($"Path escapes the vault root: {pathOrRelative}");
        return full;
    }

    public string ToRelative(string absolute)
        => Path.GetRelativePath(_root, absolute).Replace(Path.DirectorySeparatorChar, '/');
}
