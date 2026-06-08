namespace Mdex.Gateway.Auth;

public sealed class OAuthClientConfig
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Scopes { get; set; } = new();
    public List<string> RedirectUris { get; set; } = new();
}
