using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Mdex.Gateway.Auth;

/// <summary>Keeps the OpenIddict application store in sync with the OAuth client config. Syncs once at
/// startup and again whenever the watched config file (clients.json) changes, so clients — including
/// redirect URIs — can be added, changed, or removed at runtime without a rebuild or restart.
/// <para>The sync is idempotent: configured clients are created or updated, and clients absent from
/// the config are removed. A guard skips removal when the configured set is empty, so a truncated or
/// unreadable file can never wipe every client and lock everyone out. A bad reload is logged and
/// leaves the running store untouched.</para></summary>
public sealed class OAuthClientSeed(
    IServiceProvider services,
    IOptionsMonitor<List<OAuthClientConfig>> clients,
    ILogger<OAuthClientSeed> log) : IHostedService, IDisposable
{
    readonly SemaphoreSlim _sync = new(1, 1);   // serialize syncs — concurrent SQLite writers would clash
    readonly Lock _gate = new();
    IDisposable? _onChange;
    Timer? _debounce;

    public async Task StartAsync(CancellationToken ct)
    {
        await RunSyncAsync(clients.CurrentValue, ct);
        // Re-sync on config-file change, debounced (a single save can fire several reload events).
        _onChange = clients.OnChange(_ => ScheduleSync());
    }

    void ScheduleSync()
    {
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = new Timer(async _ =>
            {
                try { await RunSyncAsync(clients.CurrentValue, CancellationToken.None); }
                catch (Exception ex) { log.LogError(ex, "OAuth client re-sync failed; keeping current clients."); }
            }, null, 300, Timeout.Infinite);
        }
    }

    async Task RunSyncAsync(IReadOnlyList<OAuthClientConfig> configured, CancellationToken ct)
    {
        await _sync.WaitAsync(ct);
        try
        {
            // OpenIddict managers are scoped — resolve inside a fresh scope each run.
            using var scope = services.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            foreach (var cfg in configured)
            {
                var descriptor = BuildDescriptor(cfg);
                var existing = await manager.FindByClientIdAsync(cfg.ClientId, ct);
                if (existing is null)
                    await manager.CreateAsync(descriptor, ct);
                else
                    await manager.UpdateAsync(existing, descriptor, ct);
            }

            // Remove clients no longer in the config — but never when it's empty, which signals a
            // truncated/unreadable file and must not delete every client.
            if (configured.Count == 0)
            {
                log.LogWarning("OAuth client config is empty; skipping removal to avoid lockout.");
                return;
            }

            var keep = configured.Select(c => c.ClientId).ToHashSet(StringComparer.Ordinal);
            await foreach (var app in manager.ListAsync(cancellationToken: ct))
            {
                var id = await manager.GetClientIdAsync(app, ct);
                if (id is not null && !keep.Contains(id))
                {
                    await manager.DeleteAsync(app, ct);
                    log.LogInformation("Removed OAuth client no longer in config: {ClientId}", id);
                }
            }
        }
        finally
        {
            _sync.Release();
        }
    }

    static OpenIddictApplicationDescriptor BuildDescriptor(OAuthClientConfig cfg)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = cfg.ClientId,
            ClientSecret = cfg.ClientSecret,
            DisplayName = cfg.DisplayName,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Explicit,
        };

        descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);

        // offline_access is validated against an explicit client permission even when
        // IgnoreScopePermissions() is set — without it the refresh-token request fails (ID2065).
        descriptor.Permissions.Add(Permissions.Prefixes.Scope + Scopes.OfflineAccess);

        foreach (var scope in cfg.Scopes)
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);

        foreach (var uri in cfg.RedirectUris)
            descriptor.RedirectUris.Add(new Uri(uri));

        return descriptor;
    }

    public Task StopAsync(CancellationToken ct)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _onChange?.Dispose();
        lock (_gate) _debounce?.Dispose();
    }
}
