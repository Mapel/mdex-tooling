using Mdex.Core.Security;
using Mdex.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
namespace Mdex.Gateway.Tests;

/// <summary>
/// End-to-end tool *visibility* through the real MCP HTTP transport: a genuine client drives the
/// gateway, so tools/list runs through the per-request ListToolsFilter wired into the host. The
/// caller's role comes from the bearer token's claim (HttpRoleProvider), so each token sees only the
/// tools its role may use. This is visibility, layered on top of the existing in-tool enforcement.
/// </summary>
public class ToolVisibilityIntegrationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "mdex-gw-vis-" + Guid.NewGuid().ToString("N"));
    readonly string _tokens = Path.Combine(Path.GetTempPath(), "mdex-gw-vis-" + Guid.NewGuid().ToString("N") + ".json");
    const string ReadToken = "mdex_read";
    const string IntentToken = "mdex_intent";
    const string FullToken = "mdex_full";

    public ToolVisibilityIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        var store = new TokenStore(_tokens);
        store.Add(new TokenEntry(TokenHasher.Hash(ReadToken), Roles.ToWire(Role.Read), "ro", "2026-06-05"));
        store.Add(new TokenEntry(TokenHasher.Hash(IntentToken), Roles.ToWire(Role.WriteIntent), "wi", "2026-06-05"));
        store.Add(new TokenEntry(TokenHasher.Hash(FullToken), Roles.ToWire(Role.FullWrite), "fw", "2026-06-05"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { File.Delete(_tokens); } catch { }
    }

    async Task<string[]> ListToolNamesAsync(string rawToken, CancellationToken ct)
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
        await using var mcp = await McpClient.CreateAsync(transport, cancellationToken: ct);
        var tools = await mcp.ListToolsAsync(cancellationToken: ct);
        return tools.Select(t => t.Name).ToArray();
    }

    [Fact]
    public async Task Read_token_sees_only_read_tools()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var names = await ListToolNamesAsync(ReadToken, cts.Token);

        Assert.Equal(7, names.Length);
        Assert.Contains("search", names);
        Assert.DoesNotContain("capture_inbox", names);
        Assert.DoesNotContain("write_file", names);
    }

    [Fact]
    public async Task WriteIntent_token_sees_read_and_intent_but_not_raw_fs()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var names = await ListToolNamesAsync(IntentToken, cts.Token);

        Assert.Equal(18, names.Length);
        Assert.Contains("capture_inbox", names);
        Assert.DoesNotContain("delete", names);
    }

    [Fact]
    public async Task FullWrite_token_sees_every_tool()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var names = await ListToolNamesAsync(FullToken, cts.Token);

        Assert.Equal(20, names.Length);
    }
}
