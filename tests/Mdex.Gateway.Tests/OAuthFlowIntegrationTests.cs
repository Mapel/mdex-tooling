using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mdex.Core.Security;
using Mdex.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;

namespace Mdex.Gateway.Tests;

/// <summary>
/// Integration tests for the OAuth 2.1 Authorization Server and Resource Server endpoints.
/// Covers: Protected Resource Metadata (RFC 9728), AS Metadata (RFC 8414), consent page,
/// full PKCE flow, JWT role mapping, and regression guard for the legacy Bearer path.
/// </summary>
public class OAuthFlowIntegrationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "mdex-gw-oauth-" + Guid.NewGuid().ToString("N"));
    readonly string _tokens = Path.Combine(Path.GetTempPath(), "mdex-gw-oauth-" + Guid.NewGuid().ToString("N") + ".json");

    const string TestClientId = "test-read";
    const string TestClientSecret = "test-secret-read";
    const string TestRedirectUri = "https://test.example/callback";

    const string LegacyToken = "mdex_legacy_bearer";

    public OAuthFlowIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        new TokenStore(_tokens).Add(new TokenEntry(
            TokenHasher.Hash(LegacyToken), Roles.ToWire(Role.Read), "legacy", "2026-01-01"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { File.Delete(_tokens); } catch { }
    }

    WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Mdex:Root", _root);
            b.UseSetting("Mdex:TokenStore", _tokens);
            // Inject a test OAuth client
            b.UseSetting("Mdex:OAuthClients:0:ClientId", TestClientId);
            b.UseSetting("Mdex:OAuthClients:0:ClientSecret", TestClientSecret);
            b.UseSetting("Mdex:OAuthClients:0:DisplayName", "Test Read");
            b.UseSetting("Mdex:OAuthClients:0:Scopes:0", "mdex:read");
            b.UseSetting("Mdex:OAuthClients:0:RedirectUris:0", TestRedirectUri);
        });

    // ─── Discovery endpoints ────────────────────────────────────────────────

    [Fact]
    public async Task ProtectedResource_metadata_returns_correct_shape()
    {
        using var client = Factory().CreateClient();
        var res = await client.GetAsync("/.well-known/oauth-protected-resource");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("authorization_servers", out var as_));
        Assert.NotEqual(0, as_.GetArrayLength());
        Assert.True(doc.RootElement.TryGetProperty("scopes_supported", out _));
    }

    [Fact]
    public async Task AS_metadata_returns_correct_shape()
    {
        using var client = Factory().CreateClient();
        var res = await client.GetAsync("/.well-known/oauth-authorization-server");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("issuer", out _));
        Assert.True(doc.RootElement.TryGetProperty("token_endpoint", out _));
        // S256 must be supported
        var methods = doc.RootElement.GetProperty("code_challenge_methods_supported");
        Assert.Contains("S256", methods.EnumerateArray().Select(e => e.GetString()));
    }

    // ─── Consent page ───────────────────────────────────────────────────────

    [Fact]
    public async Task Consent_page_GET_returns_html_with_Allow_button()
    {
        using var client = Factory().CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var (verifier, challenge) = MakePkce();
        var url = AuthUrl(challenge, "mdex:read", "state1");

        var res = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Allow", body);
        Assert.Contains("mdex:read", body);
    }

    [Fact]
    public async Task Deny_decision_returns_access_denied_redirect()
    {
        using var client = Factory().CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var (verifier, challenge) = MakePkce();
        var url = AuthUrl(challenge, "mdex:read", "state2");

        // First GET to seed the request in the OpenIddict store
        await client.GetAsync(url);

        var form = new FormUrlEncodedContent([new("decision", "deny")]);
        var res = await client.PostAsync(url, form);

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var location = res.Headers.Location!.ToString();
        Assert.Contains("error=access_denied", location);
    }

    // ─── Full PKCE flow ─────────────────────────────────────────────────────

    [Fact]
    public async Task Full_pkce_flow_yields_jwt_that_authenticates_mcp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = Factory().CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var (verifier, challenge) = MakePkce();
        var url = AuthUrl(challenge, "mdex:read", "state3");

        // 1. GET consent page (seeds the authorization request in OpenIddict)
        var getResp = await client.GetAsync(url, cts.Token);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        // 2. POST allow
        var form = new FormUrlEncodedContent([new("decision", "allow")]);
        var postResp = await client.PostAsync(url, form, cts.Token);
        Assert.Equal(HttpStatusCode.Redirect, postResp.StatusCode);

        // 3. Extract authorization code from redirect
        var location = postResp.Headers.Location!.ToString();
        var code = ExtractQueryParam(location, "code");
        Assert.NotEmpty(code);

        // 4. Exchange code for token
        var tokenForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = TestClientId,
            ["client_secret"] = TestClientSecret,
            ["code"] = code,
            ["redirect_uri"] = TestRedirectUri,
            ["code_verifier"] = verifier,
        });
        var tokenResp = await client.PostAsync("/connect/token", tokenForm, cts.Token);
        Assert.Equal(HttpStatusCode.OK, tokenResp.StatusCode);
        var tokenJson = await tokenResp.Content.ReadAsStringAsync(cts.Token);
        var accessToken = ExtractJsonField(tokenJson, "access_token");
        Assert.NotEmpty(accessToken);

        // 5. Use the JWT on the MCP endpoint
        var mcpReq = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}",
                Encoding.UTF8, "application/json"),
        };
        mcpReq.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
        var mcpResp = await client.SendAsync(mcpReq, cts.Token);
        Assert.NotEqual(HttpStatusCode.Unauthorized, mcpResp.StatusCode);
    }

    [Fact]
    public async Task Read_scope_jwt_sees_only_read_tools()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var factory = Factory();
        using var httpClient = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var accessToken = await ObtainTokenAsync(httpClient, "mdex:read", cts.Token);

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = httpClient.BaseAddress!,
            AdditionalHeaders = new Dictionary<string, string>
                { ["Authorization"] = "Bearer " + accessToken },
        }, httpClient);
        await using var mcp = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
        var tools = await mcp.ListToolsAsync(cancellationToken: cts.Token);

        Assert.Equal(7, tools.Count);
        Assert.DoesNotContain(tools, t => t.Name == "capture_inbox");
        Assert.DoesNotContain(tools, t => t.Name == "write_file");
    }

    // ─── Regression guard ───────────────────────────────────────────────────

    [Fact]
    public async Task Opaque_bearer_still_works_after_oauth_addition()
    {
        using var client = Factory().CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}",
                Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + LegacyToken);
        var res = await client.SendAsync(req);
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    string AuthUrl(string challenge, string scope, string state) =>
        $"/connect/authorize?client_id={TestClientId}" +
        $"&response_type=code" +
        $"&redirect_uri={Uri.EscapeDataString(TestRedirectUri)}" +
        $"&code_challenge={challenge}" +
        $"&code_challenge_method=S256" +
        $"&scope={Uri.EscapeDataString(scope)}" +
        $"&state={state}";

    async Task<string> ObtainTokenAsync(HttpClient client, string scope, CancellationToken ct)
    {
        var (verifier, challenge) = MakePkce();
        var url = AuthUrl(challenge, scope, "st");

        await client.GetAsync(url, ct);

        var form = new FormUrlEncodedContent([new("decision", "allow")]);
        var postResp = await client.PostAsync(url, form, ct);
        var location = postResp.Headers.Location!.ToString();
        var code = ExtractQueryParam(location, "code");

        var tokenForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = TestClientId,
            ["client_secret"] = TestClientSecret,
            ["code"] = code,
            ["redirect_uri"] = TestRedirectUri,
            ["code_verifier"] = verifier,
        });
        var tokenResp = await client.PostAsync("/connect/token", tokenForm, ct);
        return ExtractJsonField(await tokenResp.Content.ReadAsStringAsync(ct), "access_token");
    }

    static (string verifier, string challenge) MakePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string ExtractQueryParam(string url, string key)
    {
        var query = new Uri(url, UriKind.Absolute).Query.TrimStart('?');
        return query.Split('&')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0] == key)
            .Select(p => Uri.UnescapeDataString(p[1]))
            .FirstOrDefault() ?? "";
    }

    static string ExtractJsonField(string json, string field)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(field).GetString() ?? "";
    }
}
