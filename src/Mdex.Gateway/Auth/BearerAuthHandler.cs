using System.Security.Claims;
using System.Text.Encodings.Web;
using Mdex.Core.Security;
using Mdex.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mdex.Gateway.Auth;

/// <summary>Maps a <c>Bearer</c> token to a <see cref="HttpRoleProvider.RoleClaim"/>. The token value
/// is never logged; failures log only the reason. Lookup is fixed-time (see <see cref="TokenStore"/>).</summary>
public sealed class BearerAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";
    readonly TokenStore _store;

    public BearerAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, TokenStore store)
        : base(options, logger, encoder)
    {
        _store = store;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("missing bearer token"));

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0)
            return Task.FromResult(AuthenticateResult.Fail("empty bearer token"));

        var role = _store.FindRole(token);
        if (role is null)
            return Task.FromResult(AuthenticateResult.Fail("invalid token"));   // token value not logged

        var identity = new ClaimsIdentity(
            new[] { new Claim(HttpRoleProvider.RoleClaim, Roles.ToWire(role.Value)) }, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
