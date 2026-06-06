using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mdex.Core.Cli;

public static class JsonEnvelope
{
    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Emit real UTF-8 (umlauts, dashes, quotes) instead of \uXXXX. The decoded value is
        // identical; this is a stdio/MCP contract, not HTML, so relaxed escaping is safe.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Ok(string command, object? data)
        => JsonSerializer.Serialize(new { ok = true, command, data }, Opts);

    public static string Err(string command, string error, object? details = null)
        => JsonSerializer.Serialize(new { ok = false, command, error, details }, Opts);
}
