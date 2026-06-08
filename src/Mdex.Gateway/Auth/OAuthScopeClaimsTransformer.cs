using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Mdex.Gateway.Auth;

/// <summary>After OpenIddict Validation authenticates a JWT, maps its granted scopes to the
/// <see cref="HttpRoleProvider.RoleClaim"/> so <see cref="HttpRoleProvider"/> works identically
/// whether the caller used a custom bearer token or an OAuth JWT. Opaque-token callers already
/// have the claim set by <see cref="BearerAuthHandler"/> — this is a no-op for them.</summary>
public sealed class OAuthScopeClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.FindFirst(HttpRoleProvider.RoleClaim) is not null)
            return Task.FromResult(principal);

        // OpenIddict emits a space-separated "scope" claim in the JWT
        var scopeValues = principal.Claims
            .Where(c => c.Type is "scope" or "scp")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

        var wire = scopeValues.Contains("mdex:fullwrite") ? "fullwrite"
            : scopeValues.Contains("mdex:write-intent") ? "write-intent"
            : scopeValues.Contains("mdex:read") ? "read"
            : null;

        if (wire is null) return Task.FromResult(principal);

        // IClaimsTransformation contract: return a new principal, do not mutate in-place
        var cloned = principal.Clone();
        ((ClaimsIdentity)cloned.Identity!).AddClaim(
            new Claim(HttpRoleProvider.RoleClaim, wire));
        return Task.FromResult(cloned);
    }
}
