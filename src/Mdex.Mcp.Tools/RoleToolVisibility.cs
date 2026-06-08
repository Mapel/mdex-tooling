using System.Reflection;
using Mdex.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Role = Mdex.Core.Security.Role;

namespace Mdex.Mcp.Tools;

/// <summary>Maps each tool's protocol name to the minimum role it declares via <see cref="MinRoleAttribute"/>,
/// so a host can hide tools a caller's role may not use. This is sichtbarkeit only — the in-tool
/// <c>auth.Require(...)</c> remains the actual gate (default-deny stays intact).</summary>
public static class RoleToolVisibility
{
    /// <summary>Tool protocol name → minimum role. Keys are derived by the SDK itself
    /// (<see cref="McpServerTool.Create(MethodInfo, object?, McpServerToolCreateOptions?)"/>), so they
    /// exactly match what a client sees in <c>tools/list</c> (PascalCase method → snake_case).</summary>
    public static IReadOnlyDictionary<string, Role> MinRoleByName { get; } = Build();

    static Dictionary<string, Role> Build()
    {
        var map = new Dictionary<string, Role>(StringComparer.Ordinal);
        var methods = typeof(MdexBackend).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

        foreach (var m in methods)
        {
            var min = m.GetCustomAttribute<MinRoleAttribute>()
                ?? throw new InvalidOperationException($"tool '{m.Name}' has no [MinRole]");
            var name = McpServerTool.Create(m, target: null, new McpServerToolCreateOptions()).ProtocolTool.Name;
            map[name] = min.Minimum;
        }
        return map;
    }

    /// <summary>Removes from <paramref name="tools"/> every entry the caller's <paramref name="role"/>
    /// may not use. A name absent from <see cref="MinRoleByName"/> is removed too (fail-closed): the
    /// host only intends to expose tools it knows about.</summary>
    public static void Apply(IList<Tool> tools, Role role)
    {
        for (var i = tools.Count - 1; i >= 0; i--)
            if (!MinRoleByName.TryGetValue(tools[i].Name, out var min) || role < min)
                tools.RemoveAt(i);
    }

    /// <summary>The <c>tools/list</c> pipeline filter to register in
    /// <c>McpServerOptions.Filters.Request.ListToolsFilters</c>. Reads the caller's role from the
    /// per-request <see cref="IRoleProvider"/> (a fixed role locally, the token claim on the gateway)
    /// and hides every tool above it. Identical for both hosts.</summary>
    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> ListToolsFilter { get; } =
        next => async (request, ct) =>
        {
            var result = await next(request, ct);
            var role = request.Services!.GetRequiredService<IRoleProvider>().Current;
            Apply(result.Tools, role);
            return result;
        };
}
