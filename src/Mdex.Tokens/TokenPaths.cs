namespace Mdex.Tokens;

public static class TokenPaths
{
    /// <summary>Default token-store location: env <c>MDEX_GATEWAY_TOKENS</c>, else per-OS config dir
    /// under <c>mdex-gateway</c> (the path follows the gateway binary name).</summary>
    public static string Default()
    {
        var env = Environment.GetEnvironmentVariable("MDEX_GATEWAY_TOKENS");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "mdex-gateway", "tokens.json");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "mdex-gateway", "tokens.json");
    }
}
