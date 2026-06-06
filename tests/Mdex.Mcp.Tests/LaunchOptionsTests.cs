using Mdex.Core.Security;
using Mdex.Mcp;
namespace Mdex.Mcp.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Missing_role_is_fail_closed()
    {
        var ex = Assert.Throws<LaunchOptionsException>(() => LaunchOptions.Parse(Array.Empty<string>()));
        Assert.Contains("--role", ex.Message);
    }

    [Fact]
    public void Invalid_role_is_rejected()
        => Assert.Throws<LaunchOptionsException>(() => LaunchOptions.Parse(new[] { "--role", "admin" }));

    [Theory]
    [InlineData("read", Role.Read)]
    [InlineData("write-intent", Role.WriteIntent)]
    [InlineData("fullwrite", Role.FullWrite)]
    public void Valid_role_is_parsed(string wire, Role expected)
    {
        var opts = LaunchOptions.Parse(new[] { "--role", wire });
        Assert.Equal(expected, opts.Role);
        Assert.Null(opts.Root);
    }

    [Fact]
    public void Root_flag_is_captured()
    {
        var opts = LaunchOptions.Parse(new[] { "--role", "read", "--root", "/srv/vault" });
        Assert.Equal("/srv/vault", opts.Root);
    }
}
