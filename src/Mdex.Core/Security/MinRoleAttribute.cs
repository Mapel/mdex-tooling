namespace Mdex.Core.Security;

/// <summary>Declares the minimum role a tool requires. Presence on every <c>[McpServerTool]</c>
/// is enforced by the reflection guard test (Plan B Task 6), so a tool that forgets it fails the
/// build (default-deny). Mirrors the in-method <c>auth.Require(...)</c> call.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MinRoleAttribute(Role minimum) : Attribute
{
    public Role Minimum { get; } = minimum;
}
