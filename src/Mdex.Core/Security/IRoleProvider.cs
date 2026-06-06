namespace Mdex.Core.Security;

/// <summary>
/// Supplies the current caller's role to <see cref="ToolAuthorizer"/>. Local host: a
/// <see cref="FixedRoleProvider"/> set once from the <c>--role</c> launch flag. Remote
/// host (Plan C): an HTTP adapter reading the authenticated token's role claim per request.
/// </summary>
public interface IRoleProvider
{
    Role Current { get; }
}

/// <summary>A role fixed for the whole process (the local stdio session model).</summary>
public sealed class FixedRoleProvider(Role role) : IRoleProvider
{
    public Role Current { get; } = role;
}
