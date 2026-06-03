using System.Text.Json;
using Mdex.Cli.Cli;

namespace Mdex.Cli.Vault;

public static class VaultResolver
{
    public static string Resolve(string? rootFlag)
    {
        foreach (var candidate in Candidates(rootFlag))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full)) return full;
        }
        throw new CommandException(
            "Vault root not found. Pass --root <path>, set MDEX_ROOT, or create the config file.");
    }

    static IEnumerable<string?> Candidates(string? rootFlag)
    {
        yield return rootFlag;
        yield return Environment.GetEnvironmentVariable("MDEX_ROOT");
        yield return FromConfig();
    }

    static string? FromConfig()
    {
        var path = ConfigPath();
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("root", out var r) ? r.GetString() : null;
        }
        catch { return null; }
    }

    static string ConfigPath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdex", "config.json");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "mdex", "config.json");
    }
}
