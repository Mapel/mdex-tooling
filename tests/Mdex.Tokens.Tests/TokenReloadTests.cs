using Mdex.Core.Security;
using Mdex.Tokens;
namespace Mdex.Tokens.Tests;

public class TokenReloadTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), "mdex-tokens-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void Reload_reflects_external_revoke()
    {
        var server = new TokenStore(_path);
        var admin = new TokenStore(_path);                 // a second handle = mdex-token
        admin.Add(new TokenEntry(TokenHasher.Hash("mdex_x"), Roles.ToWire(Role.Read), "x", "2026-06-05"));

        server.Reload();
        Assert.Equal(Role.Read, server.FindRole("mdex_x"));

        admin.Remove("x");                                 // revoke happens out-of-process
        server.Reload();
        Assert.Null(server.FindRole("mdex_x"));            // server no longer honours it
    }
}
