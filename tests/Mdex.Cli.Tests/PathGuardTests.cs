using Mdex.Cli.Cli;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class PathGuardTests
{
    [Fact]
    public void Resolves_relative_inside_root()
    {
        using var v = new TestVault();
        var g = new PathGuard(v.Root);
        Assert.Equal(Path.Combine(v.Root, "zettel", "x.md"), g.ResolveInside("zettel/x.md"));
    }

    [Fact]
    public void Rejects_escape_with_dotdot()
    {
        using var v = new TestVault();
        var g = new PathGuard(v.Root);
        Assert.Throws<CommandException>(() => g.ResolveInside("../outside.md"));
    }

    [Fact]
    public void Rejects_absolute_outside_root()
    {
        using var v = new TestVault();
        var g = new PathGuard(v.Root);
        var outside = OperatingSystem.IsWindows() ? @"C:\Windows\system32\x" : "/etc/passwd";
        Assert.Throws<CommandException>(() => g.ResolveInside(outside));
    }

    [Fact]
    public void ToRelative_uses_forward_slashes()
    {
        using var v = new TestVault();
        var g = new PathGuard(v.Root);
        Assert.Equal("zettel/x.md", g.ToRelative(Path.Combine(v.Root, "zettel", "x.md")));
    }
}
