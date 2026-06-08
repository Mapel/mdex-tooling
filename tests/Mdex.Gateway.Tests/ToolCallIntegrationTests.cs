using Mdex.Core.Security;
using Mdex.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
namespace Mdex.Gateway.Tests;

/// <summary>
/// End-to-end role enforcement through the real MCP HTTP transport: a genuine MCP client drives the
/// gateway over the in-memory test server, so this exercises the per-request scoped seam
/// (HttpRoleProvider → ToolAuthorizer, resolved from RequestServices) that the 401-level tests do not.
/// With one <c>read</c> token: a read tool call succeeds, while a fullwrite tool is hidden from
/// tools/list yet still refused on a direct call — proving visibility is layered over, not a
/// replacement for, the in-tool gate (defense-in-depth).
/// </summary>
public class ToolCallIntegrationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "mdex-gw-call-" + Guid.NewGuid().ToString("N"));
    readonly string _tokens = Path.Combine(Path.GetTempPath(), "mdex-gw-call-" + Guid.NewGuid().ToString("N") + ".json");
    const string ReadToken = "mdex_readonly";

    public ToolCallIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        new TokenStore(_tokens).Add(new TokenEntry(
            TokenHasher.Hash(ReadToken), Roles.ToWire(Role.Read), "ro", "2026-06-05"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { File.Delete(_tokens); } catch { }
    }

    async Task<McpClient> ConnectAsync(string rawToken, CancellationToken ct)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mdex:Root", _root);
            b.UseSetting("Mdex:TokenStore", _tokens);
        });
        var http = factory.CreateClient();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = http.BaseAddress!,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + rawToken },
        }, http);
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    [Fact]
    public async Task Read_token_is_allowed_read_tools_but_refused_fullwrite_tools()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var mcp = await ConnectAsync(ReadToken, cts.Token);

        var tools = await mcp.ListToolsAsync(cancellationToken: cts.Token);
        var readTool = tools.First(t =>
            t.Name.Contains("list", StringComparison.OrdinalIgnoreCase) &&
            t.Name.Contains("tag", StringComparison.OrdinalIgnoreCase));

        // the fullwrite tool is hidden from a read token's tools/list (visibility filter)…
        Assert.DoesNotContain(tools, t => t.Name == "write_file");

        // sufficient role → no role error (empty vault → empty result, not an error)
        var allowed = await mcp.CallToolAsync(readTool.Name,
            new Dictionary<string, object?>(), cancellationToken: cts.Token);
        Assert.False(allowed.IsError is true);

        // …yet a direct call to the hidden tool is still denied in-band by the scoped authorizer
        var denied = await mcp.CallToolAsync("write_file",
            new Dictionary<string, object?> { ["path"] = "note.md", ["content"] = "x" },
            cancellationToken: cts.Token);
        Assert.True(denied.IsError is true);
    }
}
