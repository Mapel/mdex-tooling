using Mdex.Core.Security;
using Microsoft.AspNetCore.Http;

namespace Mdex.Gateway.Auth;

/// <summary>Feeds the shared <see cref="ToolAuthorizer"/> the caller's role from the authenticated
/// principal's claim (per request). Defense-in-depth: a missing/garbage claim is denied even though
/// <c>RequireAuthorization</c> already gates the endpoint.</summary>
public sealed class HttpRoleProvider(IHttpContextAccessor http) : IRoleProvider
{
    public const string RoleClaim = "mdex-role";

    public Role Current
    {
        get
        {
            var wire = http.HttpContext?.User.FindFirst(RoleClaim)?.Value;
            if (wire is null || !Roles.TryParse(wire, out var role))
                throw new MdexToolException("not authenticated");
            return role;
        }
    }
}
