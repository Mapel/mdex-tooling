using System.Reflection;
using Mdex.Core.Cli;
using Mdex.Core.Security;
using ModelContextProtocol.Server;
namespace Mdex.Mcp.Tools.Tests;

public class ToolGateGuardTests
{
    static IEnumerable<MethodInfo> Tools() =>
        typeof(MdexBackend).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    [Fact]
    public void There_are_nineteen_tools()   // 9 read + 5 intent + 5 raw-fs
        => Assert.Equal(19, Tools().Count());

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
}
