using System.Text.Json;
using Mdex.Core.Cli;

namespace Mdex.Core.Tests;

public static class CommandTestExtensions
{
    /// <summary>Serialize a CommandResult through the real envelope and return its `data` element.</summary>
    public static JsonElement Data(this CommandResult result)
        => JsonDocument.Parse(JsonEnvelope.Ok("test", result.Data)).RootElement.GetProperty("data");
}
