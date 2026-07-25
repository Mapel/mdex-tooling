using Mdex.Core.Cli;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class VaultResolverTests
{
    [Fact]
    public void Flag_wins_over_env()
    {
        using var v = new TestVault();
        Environment.SetEnvironmentVariable("MDEX_ROOT", Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N")));
        try { Assert.Equal(v.Root, VaultResolver.Resolve(v.Root)); }
        finally { Environment.SetEnvironmentVariable("MDEX_ROOT", null); }
    }

    [Fact]
    public void Env_used_when_no_flag()
    {
        using var v = new TestVault();
        Environment.SetEnvironmentVariable("MDEX_ROOT", v.Root);
        try { Assert.Equal(v.Root, VaultResolver.Resolve(null)); }
        finally { Environment.SetEnvironmentVariable("MDEX_ROOT", null); }
    }

    [Fact]
    public void Throws_when_nothing_resolves()
    {
        Environment.SetEnvironmentVariable("MDEX_ROOT", null);

        // Point HOME at an empty sandbox: the third candidate is ~/.config/mdex/config.json, so on a
        // machine where the developer actually uses mdex the resolver finds that real config and never
        // throws. The assertion only held while that file happened to be absent.
        var home = Environment.GetEnvironmentVariable("HOME");
        var sandbox = Directory.CreateTempSubdirectory("mdex-no-config-");
        Environment.SetEnvironmentVariable("HOME", sandbox.FullName);
        try
        {
            Assert.Throws<CommandException>(() => VaultResolver.Resolve(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N"))));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", home);
            sandbox.Delete(true);
        }
    }
}
