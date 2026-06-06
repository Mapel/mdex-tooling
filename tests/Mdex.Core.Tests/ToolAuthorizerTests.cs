using Mdex.Core.Security;
namespace Mdex.Core.Tests;

public class ToolAuthorizerTests
{
    static ToolAuthorizer With(Role role) => new(new FixedRoleProvider(role));

    [Fact]
    public void Allows_when_role_meets_or_exceeds_minimum()
    {
        With(Role.FullWrite).Require(Role.WriteIntent);   // no throw
        With(Role.Read).Require(Role.Read);               // no throw
    }

    [Fact]
    public void Blocks_when_role_below_minimum()
    {
        Assert.Throws<MdexToolException>(() => With(Role.WriteIntent).Require(Role.FullWrite));
        Assert.Throws<MdexToolException>(() => With(Role.Read).Require(Role.WriteIntent));
    }
}
