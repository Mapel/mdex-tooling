using System.Security.Claims;
using Mdex.Core.Security;
using Mdex.Gateway.Auth;
using Microsoft.AspNetCore.Http;
namespace Mdex.Gateway.Tests;

public class HttpRoleProviderTests
{
    static HttpRoleProvider WithClaim(string? wire)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        if (wire is not null)
            accessor.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(HttpRoleProvider.RoleClaim, wire) }, "Bearer"));
        return new HttpRoleProvider(accessor);
    }

    [Fact]
    public void Reads_the_role_from_the_claim()
        => Assert.Equal(Role.WriteIntent, WithClaim("write-intent").Current);

    [Fact]
    public void Missing_claim_is_denied()
        => Assert.Throws<MdexToolException>(() => WithClaim(null).Current);

    [Fact]
    public void Garbage_claim_is_denied()
        => Assert.Throws<MdexToolException>(() => WithClaim("admin").Current);
}
