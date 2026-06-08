using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Mdex.Gateway.Auth;

/// <summary>Idempotent startup seed: ensures each configured OAuth client exists in the OpenIddict
/// store. Runs as IHostedService so EF Core stores are ready. Safe to re-run on every boot —
/// existing clients are updated, new ones are created.</summary>
public sealed class OAuthClientSeed(
    IServiceProvider services,
    IOptions<List<OAuthClientConfig>> clientOptions) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // OpenIddict managers are scoped — must resolve in a scope
        using var scope = services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var cfg in clientOptions.Value)
        {
            var existing = await manager.FindByClientIdAsync(cfg.ClientId, ct);

            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = cfg.ClientId,
                ClientSecret = cfg.ClientSecret,
                DisplayName = cfg.DisplayName,
                ClientType = ClientTypes.Confidential,
                ConsentType = ConsentTypes.Explicit,
            };

            descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
            descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(Permissions.Endpoints.Token);

            foreach (var scope2 in cfg.Scopes)
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope2);

            foreach (var uri in cfg.RedirectUris)
                descriptor.RedirectUris.Add(new Uri(uri));

            if (existing is null)
                await manager.CreateAsync(descriptor, ct);
            else
                await manager.UpdateAsync(existing, descriptor, ct);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
