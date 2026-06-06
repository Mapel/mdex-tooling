namespace Mdex.Core.Security;

/// <summary>Enforces a per-tool minimum role against the caller's <see cref="IRoleProvider.Current"/>.
/// Default-deny: anything below the minimum throws. Host-agnostic (no ASP.NET, no MCP SDK).</summary>
public sealed class ToolAuthorizer(IRoleProvider provider)
{
    public void Require(Role minimum)
    {
        if (provider.Current < minimum)
            throw new MdexToolException($"this tool requires the '{Roles.ToWire(minimum)}' role", null);
    }
}
