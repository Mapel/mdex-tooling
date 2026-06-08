using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Mdex.Core.Security;
using Mdex.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
namespace Mdex.Gateway.Tests;

public class AuthIntegrationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "mdex-gw-it-" + Guid.NewGuid().ToString("N"));
    readonly string _tokens = Path.Combine(Path.GetTempPath(), "mdex-gw-it-" + Guid.NewGuid().ToString("N") + ".json");
    const string RawToken = "mdex_integration";

    public AuthIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        new TokenStore(_tokens).Add(new TokenEntry(
            TokenHasher.Hash(RawToken), Roles.ToWire(Role.Read), "it", "2026-06-05"));
    }
    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { File.Delete(_tokens); } catch { }
    }

    WebApplicationFactory<Program> Factory(string? tokensOverride = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mdex:Root", _root);
            b.UseSetting("Mdex:TokenStore", tokensOverride ?? _tokens);
        });

    static HttpRequestMessage McpPost(string? rawAuthHeader)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", Encoding.UTF8, "application/json"),
        };
        if (rawAuthHeader is not null)
            req.Headers.TryAddWithoutValidation("Authorization", rawAuthHeader);
        return req;
    }

    [Fact]
    public async Task No_header_is_401()
    {
        using var client = Factory().CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(McpPost(null))).StatusCode);
    }

    [Theory]
    [InlineData("Bearer mdex_wrong")]     // unknown token
    [InlineData("Bearer ")]               // empty token
    [InlineData("Basic abc")]             // wrong scheme
    [InlineData("mdex_integration")]      // no scheme prefix
    public async Task Malformed_or_invalid_header_is_401(string header)
    {
        using var client = Factory().CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(McpPost(header))).StatusCode);
    }

    [Fact]
    public async Task Valid_token_is_accepted_not_401()
    {
        using var client = Factory().CreateClient();
        var res = await client.SendAsync(McpPost("Bearer " + RawToken));
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Corrupt_token_file_fails_closed_401()   // hot-reload robustness
    {
        var corrupt = Path.Combine(Path.GetTempPath(), "mdex-gw-it-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(corrupt, "{ this is not valid json");
        try
        {
            using var client = Factory(corrupt).CreateClient();
            // corrupt file → store loads empty → even a well-formed bearer is unknown → 401
            var res = await client.SendAsync(McpPost("Bearer " + RawToken));
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
        finally { File.Delete(corrupt); }
    }
}
