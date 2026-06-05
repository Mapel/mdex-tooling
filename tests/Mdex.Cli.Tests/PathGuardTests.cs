// tests/Mdex.Cli.Tests/PathGuardTests.cs
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class PathGuardTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "mdex-guard-" + Guid.NewGuid().ToString("N"));
    public PathGuardTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Inside_relative_path_resolves_under_root()
    {
        var guard = new PathGuard(_root);
        var abs = guard.ResolveInside("inbox/a.md");
        Assert.StartsWith(_root, abs);
        Assert.EndsWith("a.md", abs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_path_is_rejected(string bad)
    {
        var guard = new PathGuard(_root);
        Assert.Throws<CommandException>(() => guard.ResolveInside(bad));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("inbox/..")]
    public void Root_itself_is_rejected_as_a_target(string pointsAtRoot)
    {
        var guard = new PathGuard(_root);
        Assert.Throws<CommandException>(() => guard.ResolveInside(pointsAtRoot));
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("inbox/../../escape.md")]
    public void Relative_traversal_is_rejected(string escaping)
    {
        var guard = new PathGuard(_root);
        Assert.Throws<CommandException>(() => guard.ResolveInside(escaping));
    }

    [Fact]
    public void Different_case_absolute_sibling_is_rejected_on_case_sensitive_fs()
    {
        // Probe the filesystem: only meaningful where casing distinguishes paths.
        var probe = Path.Combine(_root, "Probe");
        Directory.CreateDirectory(probe);
        var caseInsensitive = Directory.Exists(Path.Combine(_root, "probe"));
        if (caseInsensitive) return; // not applicable on this FS (Windows/default macOS)

        // A sibling path with the root's last segment upper-cased is a DIFFERENT directory.
        var parent = Path.GetDirectoryName(_root)!;
        var flipped = Path.Combine(parent, Path.GetFileName(_root).ToUpperInvariant(), "x.md");
        var guard = new PathGuard(_root);
        Assert.Throws<CommandException>(() => guard.ResolveInside(flipped));
    }

    [Fact]
    public void Rejects_absolute_path_outside_root()
    {
        var guard = new PathGuard(_root);
        var outside = OperatingSystem.IsWindows() ? @"C:\Windows\system32\x" : "/etc/passwd";
        Assert.Throws<CommandException>(() => guard.ResolveInside(outside));
    }

    [Fact]
    public void ToRelative_uses_forward_slashes()
    {
        var guard = new PathGuard(_root);
        Assert.Equal("zettel/x.md", guard.ToRelative(Path.Combine(_root, "zettel", "x.md")));
    }
}
