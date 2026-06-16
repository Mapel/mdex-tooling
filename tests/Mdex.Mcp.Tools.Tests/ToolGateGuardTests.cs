using System.Reflection;
using Mdex.Core.Cli;
using Mdex.Core.Security;
using Mdex.Mcp.Tools;
using ModelContextProtocol.Server;
namespace Mdex.Mcp.Tools.Tests;

public class ToolGateGuardTests
{
    static IEnumerable<MethodInfo> Tools() =>
        typeof(MdexBackend).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    [Fact]
    public void There_are_twenty_tools()   // 7 read + 11 intent + 2 raw-fs
        => Assert.Equal(20, Tools().Count());

    [Fact]
    public void Every_tool_declares_a_minimum_role()
    {
        var missing = Tools().Where(m => m.GetCustomAttribute<MinRoleAttribute>() is null)
                             .Select(m => m.Name).ToArray();
        Assert.True(missing.Length == 0, "tools without [MinRole]: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_tool_takes_the_authorizer_it_needs_to_gate()
    {
        var ungated = Tools().Where(m => m.GetParameters().All(p => p.ParameterType != typeof(ToolAuthorizer)))
                            .Select(m => m.Name).ToArray();
        Assert.True(ungated.Length == 0, "tools without ToolAuthorizer param: " + string.Join(", ", ungated));
    }

    [Fact]
    public void Tool_count_matches_engine_commands_minus_meta()
    {
        // One tool per runnable verb (the meta 'version' verb is not exposed as a tool).
        var verbs = CommandEngine.Default().Commands.Count(c => c.Name != "version");
        Assert.Equal(verbs, Tools().Count());
    }

    // D-2 (SEC-1): declaration is not enforcement. The asserts above prove every tool *declares*
    // [MinRole] and *takes* a ToolAuthorizer; this one proves each above-floor tool actually *calls*
    // auth.Require with its declared role. We invoke the tool with the role one rung below its minimum
    // and require the authorizer's own refusal message — which originates only in ToolAuthorizer.Require,
    // so a forgotten Require (backend throws a different message) or a wrong-role Require (wrong role in
    // the message) both fail this test. 'read'-floor tools are exempt: read is the lowest rung, so a
    // missing check there cannot escalate any authenticated caller.
    [Fact]
    public void Every_above_floor_tool_refuses_the_next_lower_role()
    {
        var engine = CommandEngine.Default();
        var root = Path.Combine(Path.GetTempPath(), "mdex-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var backend = new MdexBackend(engine, root);
            foreach (var m in Tools())
            {
                var min = m.GetCustomAttribute<MinRoleAttribute>()!.Minimum;
                if (min == Role.Read) continue;                       // floor: nothing lower to deny
                var lower = (Role)((int)min - 1);
                var auth = new ToolAuthorizer(new FixedRole(lower));
                var args = m.GetParameters().Select(p => Dummy(p.ParameterType, backend, auth)).ToArray();

                var ex = Assert.ThrowsAny<Exception>(() => m.Invoke(null, args));
                var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                Assert.True(inner is MdexToolException &&
                            inner.Message.Contains($"requires the '{Roles.ToWire(min)}' role"),
                    $"{m.Name}: as role '{Roles.ToWire(lower)}' (min '{Roles.ToWire(min)}') it was not refused " +
                    $"by the authorizer; got {inner.GetType().Name}: {inner.Message}");
            }
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    static object? Dummy(Type t, MdexBackend backend, ToolAuthorizer auth)
    {
        if (t == typeof(MdexBackend)) return backend;
        if (t == typeof(ToolAuthorizer)) return auth;
        if (t == typeof(string)) return "x";
        if (t == typeof(string[])) return new[] { "x" };
        if (t == typeof(bool)) return false;
        return t.IsValueType ? Activator.CreateInstance(t) : null;   // int? / other → null/default
    }

    sealed class FixedRole(Role role) : IRoleProvider { public Role Current => role; }
}
