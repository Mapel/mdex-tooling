using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Mdex.Mcp.Tools;

/// <summary>
/// Some clients/models (e.g. via Langdock, which flattens the <c>["array","null"]</c> schema to
/// "string") send a comma-separated string for array-typed tool arguments — <c>tags</c>,
/// <c>add/remove/set</c>, <c>targets</c> — instead of a JSON array. This CallTool filter splits such
/// strings into arrays BEFORE the SDK binds them, so the tool's <c>string[]</c> params still receive
/// a list. It runs before the tool handler, leaves real arrays untouched, and — unlike a JSON
/// converter — does NOT alter the advertised schema (which stays a typed array).
/// </summary>
public static class ArrayArgNormalizer
{
    // tool protocol name -> names of its string[] parameters (derived by reflection, self-maintaining)
    static readonly IReadOnlyDictionary<string, HashSet<string>> ArrayParamsByTool = Build();

    static Dictionary<string, HashSet<string>> Build()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var methods = typeof(MdexBackend).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

        foreach (var m in methods)
        {
            var arrayParams = m.GetParameters()
                .Where(p => p.ParameterType == typeof(string[]))   // covers string[] and nullable string[]?
                .Select(p => p.Name!)
                .ToHashSet(StringComparer.Ordinal);
            if (arrayParams.Count == 0) continue;
            var name = McpServerTool.Create(m, target: null, new McpServerToolCreateOptions()).ProtocolTool.Name;
            map[name] = arrayParams;
        }
        return map;
    }

    /// <summary>In-place: for the named tool, replace any array-typed argument that arrived as a
    /// comma-separated string with the equivalent JSON array. Real arrays and unknown tools/params
    /// are left untouched. Exposed for testing.</summary>
    public static void Normalize(string toolName, IDictionary<string, JsonElement> args)
    {
        if (!ArrayParamsByTool.TryGetValue(toolName, out var arrayParams)) return;
        foreach (var key in arrayParams)
            if (args.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String)
                args[key] = JsonSerializer.SerializeToElement(
                    v.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Filter to register in <c>McpServerOptions.Filters.Request.CallToolFilters</c>.</summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> CallToolFilter { get; } =
        next => (request, ct) =>
        {
            if (request.Params is { Arguments: { } args, Name: { } toolName })
                Normalize(toolName, args);
            return next(request, ct);
        };
}
