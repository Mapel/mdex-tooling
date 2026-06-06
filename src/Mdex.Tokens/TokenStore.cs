using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mdex.Core.Security;

namespace Mdex.Tokens;

/// <summary>
/// In-memory snapshot of the on-disk token file. Stores only hashes. Mutations (Add/Remove) persist
/// immediately (atomic temp+rename, owner-only perms); <see cref="Reload"/> re-reads the file (the
/// gateway's watcher calls it). <see cref="FindRole"/> compares hashes in fixed time.
/// </summary>
public sealed class TokenStore
{
    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    readonly string _path;
    List<TokenEntry> _entries;

    public TokenStore(string path)
    {
        _path = path;
        _entries = Load(_path);
    }

    public IReadOnlyList<TokenEntry> Entries => _entries;

    public void Reload() => _entries = Load(_path);

    public Role? FindRole(string rawToken)
    {
        var hash = TokenHasher.Hash(rawToken);
        // Fixed-time compare: defense-in-depth against timing attacks on the hash check. (Token entropy
        // is 256 bits so a timing oracle on the stored hash is of little practical use, but constant-time
        // comparison is cheap and removes the question.) All SHA-256 hex hashes are 64 chars → equal length.
        foreach (var e in _entries)
            if (FixedTimeEquals(e.Hash, hash) && Roles.TryParse(e.Role, out var role))
                return role;
        return null;
    }

    static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
    }

    public void Add(TokenEntry entry)
    {
        _entries.Add(entry);
        Save();
    }

    public bool Remove(string label)
    {
        var removed = _entries.RemoveAll(e => e.Label == label) > 0;
        if (removed) Save();
        return removed;
    }

    void Save()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(dir);
        HardenPermissions(dir, isDirectory: true);            // 0700 (D-3)

        // Atomic: serialize to a temp file in the same dir, then rename over the target, so the
        // watching gateway never reads a half-written file (D-6).
        var tmp = Path.Combine(dir, Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, Opts));
        HardenPermissions(tmp, isDirectory: false);           // 0600 before it becomes the live file
        File.Move(tmp, _path, overwrite: true);
    }

    static void HardenPermissions(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows()) return;              // POSIX-only; on Windows rely on ACL inheritance
        var mode = isDirectory ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                               : UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try { File.SetUnixFileMode(path, mode); } catch { /* best-effort: FS may not support it */ }
    }

    static List<TokenEntry> Load(string path)
    {
        if (!File.Exists(path)) return new List<TokenEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<TokenEntry>>(File.ReadAllText(path), Opts) ?? new();
        }
        catch
        {
            return new List<TokenEntry>();   // corrupt/partial file → treat as empty → fail-closed (all 401)
        }
    }
}
