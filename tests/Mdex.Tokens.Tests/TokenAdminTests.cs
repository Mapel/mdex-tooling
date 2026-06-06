using Mdex.Core.Security;
using Mdex.Tokens;
namespace Mdex.Tokens.Tests;

public class TokenAdminTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), "mdex-tokens-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void New_returns_raw_token_that_authenticates_with_its_role()
    {
        var store = new TokenStore(_path);
        var admin = new TokenAdmin(store);
        var raw = admin.New(Role.FullWrite, "server");
        Assert.StartsWith("mdex_", raw);
        Assert.Equal(Role.FullWrite, store.FindRole(raw));
        Assert.Single(admin.List());
    }

    [Fact]
    public void Revoke_removes_by_label()
    {
        var admin = new TokenAdmin(new TokenStore(_path));
        admin.New(Role.Read, "tmp");
        Assert.True(admin.Revoke("tmp"));
        Assert.Empty(admin.List());
    }
}
