using Mdex.Core.Security;
namespace Mdex.Core.Tests;

public class RoleTests
{
    [Fact]
    public void Ladder_is_ordered_read_lt_intent_lt_full()
    {
        Assert.True(Role.Read < Role.WriteIntent);
        Assert.True(Role.WriteIntent < Role.FullWrite);
    }

    [Theory]
    [InlineData(Role.Read, "read")]
    [InlineData(Role.WriteIntent, "write-intent")]
    [InlineData(Role.FullWrite, "fullwrite")]
    public void Wire_roundtrip(Role role, string wire)
    {
        Assert.Equal(wire, Roles.ToWire(role));
        Assert.True(Roles.TryParse(wire, out var parsed));
        Assert.Equal(role, parsed);
    }

    [Fact]
    public void TryParse_rejects_garbage()
    {
        Assert.False(Roles.TryParse("admin", out _));
        Assert.False(Roles.TryParse(null, out _));
    }
}
