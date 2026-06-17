using Mdex.Gateway.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Mdex.Gateway.Tests;

/// <summary>
/// Unit tests for <see cref="OAuthClientSeed"/> — the service that keeps the OpenIddict application
/// store in sync with the (watched) client config: create, update, remove, and the empty-config
/// guard that prevents a truncated file from wiping every client.
/// </summary>
public sealed class OAuthClientSeedTests : IDisposable
{
    readonly List<ServiceProvider> _providers = [];
    readonly string _dir = Path.Combine(Path.GetTempPath(), "mdex-seed-" + Guid.NewGuid().ToString("N"));

    public OAuthClientSeedTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var p in _providers) p.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // A shared OpenIddict store (single SQLite file) so multiple syncs see the same data.
    ServiceProvider NewStore()
    {
        var dbPath = Path.Combine(_dir, "openiddict.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DbContext>(o =>
        {
            o.UseSqlite($"Data Source={dbPath}");
            o.UseOpenIddict();
        });
        services.AddOpenIddict().AddCore(o => o.UseEntityFrameworkCore().UseDbContext<DbContext>());
        var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope())
            scope.ServiceProvider.GetRequiredService<DbContext>().Database.EnsureCreated();
        _providers.Add(sp);
        return sp;
    }

    static OAuthClientConfig Client(string id, params string[] redirectUris) => new()
    {
        ClientId = id,
        ClientSecret = "secret-" + id,
        DisplayName = id,
        Scopes = ["mdex:read"],
        RedirectUris = [.. redirectUris],
    };

    // Run one sync against the store with the given client list (mirrors a config reload).
    static async Task SyncAsync(IServiceProvider store, params OAuthClientConfig[] clients)
    {
        var seed = new OAuthClientSeed(
            store,
            new StaticMonitor<List<OAuthClientConfig>>([.. clients]),
            LoggerFactory.Create(b => { }).CreateLogger<OAuthClientSeed>());
        await seed.StartAsync(CancellationToken.None);
        await seed.StopAsync(CancellationToken.None);
    }

    static async Task<string[]> ClientIdsAsync(IServiceProvider store)
    {
        using var scope = store.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var ids = new List<string>();
        await foreach (var app in mgr.ListAsync())
            ids.Add(await mgr.GetClientIdAsync(app) ?? "");
        return [.. ids];
    }

    static async Task<(string[] uris, string[] perms)> InspectAsync(IServiceProvider store, string clientId)
    {
        using var scope = store.CreateScope();
        var mgr = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = await mgr.FindByClientIdAsync(clientId);
        Assert.NotNull(app);
        var uris = await mgr.GetRedirectUrisAsync(app);
        var perms = await mgr.GetPermissionsAsync(app);
        return ([.. uris], [.. perms]);
    }

    [Fact]
    public async Task Seeds_configured_clients_with_refresh_and_offline_access()
    {
        var store = NewStore();
        await SyncAsync(store, Client("read", "https://a/cb"), Client("wi", "https://b/cb"));

        Assert.Equal(["read", "wi"], (await ClientIdsAsync(store)).Order());

        var (uris, perms) = await InspectAsync(store, "wi");
        Assert.Contains("https://b/cb", uris);
        Assert.Contains(Permissions.GrantTypes.RefreshToken, perms);
        Assert.Contains(Permissions.Prefixes.Scope + Scopes.OfflineAccess, perms);
    }

    [Fact]
    public async Task Removes_clients_absent_from_config()
    {
        var store = NewStore();
        await SyncAsync(store, Client("read"), Client("wi"));
        await SyncAsync(store, Client("read"));   // wi dropped

        Assert.Equal(["read"], await ClientIdsAsync(store));
    }

    [Fact]
    public async Task Empty_config_does_not_delete_existing_clients()
    {
        var store = NewStore();
        await SyncAsync(store, Client("read"));
        await SyncAsync(store);   // empty — guard must keep existing clients

        Assert.Equal(["read"], await ClientIdsAsync(store));
    }

    [Fact]
    public async Task Updates_redirect_uris_on_resync()
    {
        var store = NewStore();
        await SyncAsync(store, Client("read", "https://a/cb"));
        await SyncAsync(store, Client("read", "https://a/cb", "https://new/cb"));

        var (uris, _) = await InspectAsync(store, "read");
        Assert.Contains("https://new/cb", uris);
        Assert.Single(await ClientIdsAsync(store));
    }

    sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
