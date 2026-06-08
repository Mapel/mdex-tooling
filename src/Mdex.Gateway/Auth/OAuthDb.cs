using Microsoft.EntityFrameworkCore;

namespace Mdex.Gateway.Auth;

/// <summary>Minimal EF Core DbContext that hosts only the four OpenIddict entity stores.
/// No domain entities live here — the gateway's only persistent OAuth state is the client
/// registry, authorization codes, and tokens managed by OpenIddict.</summary>
public sealed class OAuthDb(DbContextOptions<OAuthDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.UseOpenIddict();
    }
}
