using ModelContextProtocol.Client;
namespace Mdex.Mcp.Tests;

/// <summary>
/// End-to-end tool visibility through the real stdio transport: launches the shipped mdex-mcp binary
/// with a fixed --role and drives it with a genuine MCP client. Proves the ListToolsFilter is wired
/// into the local host too (the process's --role flag feeds the same filter as the gateway's token).
/// </summary>
public class ToolVisibilityStdioTests
{
    static string McpBinary()
    {
        var name = OperatingSystem.IsWindows() ? "mdex-mcp.exe" : "mdex-mcp";
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(File.Exists(path), $"mdex-mcp binary not found next to the test assembly: {path}");
        return path;
    }

    static async Task<string[]> ListToolNamesAsync(string role, CancellationToken ct)
    {
        var root = Path.Combine(Path.GetTempPath(), "mdex-mcp-stdio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "mdex-mcp",
                Command = McpBinary(),
                Arguments = ["--role", role, "--root", root],
            });
            await using var mcp = await McpClient.CreateAsync(transport, cancellationToken: ct);
            var tools = await mcp.ListToolsAsync(cancellationToken: ct);
            return tools.Select(t => t.Name).ToArray();
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public async Task Read_role_session_sees_only_read_tools()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var names = await ListToolNamesAsync("read", cts.Token);

        Assert.Equal(9, names.Length);
        Assert.Contains("search", names);
        Assert.DoesNotContain("capture_inbox", names);
        Assert.DoesNotContain("write_file", names);
    }

    [Fact]
    public async Task Fullwrite_role_session_sees_every_tool()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var names = await ListToolNamesAsync("fullwrite", cts.Token);

        Assert.Equal(19, names.Length);
    }
}
