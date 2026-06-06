using Mdex.Core.Security;
using Mdex.Tokens;
namespace Mdex.Tokens.Tests;

public class TokenStoreTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), "mdex-tokens-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void Missing_file_loads_empty()
    {
        var store = new TokenStore(_path);
        Assert.Empty(store.Entries);
        Assert.Null(store.FindRole("mdex_whatever"));
    }

    [Fact]
    public void Add_then_find_by_raw_token_returns_role()
    {
        var store = new TokenStore(_path);
        var raw = "mdex_secret";
        store.Add(new TokenEntry(TokenHasher.Hash(raw), Roles.ToWire(Role.WriteIntent), "langdock", "2026-06-05"));
        Assert.Equal(Role.WriteIntent, store.FindRole(raw));
        Assert.Null(store.FindRole("mdex_wrong"));
    }

    [Fact]
    public void Add_persists_across_reopen_and_stores_only_hash()
    {
        var raw = "mdex_secret";
        new TokenStore(_path).Add(new TokenEntry(TokenHasher.Hash(raw), Roles.ToWire(Role.Read), "claude", "2026-06-05"));
        var text = File.ReadAllText(_path);
        Assert.DoesNotContain(raw, text);                 // raw token never written
        Assert.Equal(Role.Read, new TokenStore(_path).FindRole(raw));
    }

    [Fact]
    public void Remove_by_label_deletes_matching_entry()
    {
        var store = new TokenStore(_path);
        store.Add(new TokenEntry(TokenHasher.Hash("mdex_a"), Roles.ToWire(Role.Read), "a", "2026-06-05"));
        Assert.True(store.Remove("a"));
        Assert.False(store.Remove("a"));
        Assert.Null(store.FindRole("mdex_a"));
    }

    [Fact]
    public void Saved_file_is_owner_only_on_posix()   // D-3
    {
        if (OperatingSystem.IsWindows()) return;
        var store = new TokenStore(_path);
        store.Add(new TokenEntry(TokenHasher.Hash("mdex_x"), Roles.ToWire(Role.Read), "x", "2026-06-05"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_path)); // 0600
    }
}
