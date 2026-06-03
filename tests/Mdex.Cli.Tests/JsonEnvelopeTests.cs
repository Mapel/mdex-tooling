using Mdex.Cli.Cli;
namespace Mdex.Cli.Tests;

public class JsonEnvelopeTests
{
    [Fact]
    public void Ok_envelope_has_ok_command_data()
    {
        var json = JsonEnvelope.Ok("search", new { hits = 2 });
        Assert.Contains("\"ok\": true", json);
        Assert.Contains("\"command\": \"search\"", json);
        Assert.Contains("\"hits\": 2", json);
    }

    [Fact]
    public void Error_envelope_has_ok_false_and_message()
    {
        var json = JsonEnvelope.Err("read-note", "not found");
        Assert.Contains("\"ok\": false", json);
        Assert.Contains("\"error\": \"not found\"", json);
    }
}
