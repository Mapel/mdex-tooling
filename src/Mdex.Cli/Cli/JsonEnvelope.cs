using System.Text.Json;

namespace Mdex.Cli.Cli;

public static class JsonEnvelope
{
    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Ok(string command, object? data)
        => JsonSerializer.Serialize(new { ok = true, command, data }, Opts);

    public static string Err(string command, string error)
        => JsonSerializer.Serialize(new { ok = false, command, error }, Opts);
}
