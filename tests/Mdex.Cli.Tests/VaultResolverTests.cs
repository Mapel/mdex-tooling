using Mdex.Cli.Cli;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

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
        Assert.Throws<CommandException>(() => VaultResolver.Resolve(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N"))));
    }
}
