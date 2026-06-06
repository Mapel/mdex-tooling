using Mdex.Core.Cli;
namespace Mdex.Core.Tests;

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

    [Fact]
    public void Error_envelope_omits_details_when_null()
    {
        var json = JsonEnvelope.Err("read-note", "not found");
        Assert.DoesNotContain("details", json);
    }

    [Fact]
    public void Error_envelope_includes_structured_details()
    {
        var json = JsonEnvelope.Err("read-note", "ambiguous",
            new { candidates = new[] { "projects/a/index.md", "projects/b/index.md" } });
        Assert.Contains("\"details\"", json);
        Assert.Contains("\"candidates\"", json);
        Assert.Contains("projects/b/index.md", json);
    }

    [Fact]
    public void Non_ascii_is_emitted_as_real_utf8_not_escaped()
    {
        var json = JsonEnvelope.Ok("read-note", new { title = "Komplexität — Job" });
        Assert.Contains("Komplexität — Job", json);
        Assert.DoesNotContain("\\u00E4", json);   // ä not \u-escaped
    }
}
