using System.Text.Json;
using Mdex.Mcp.Tools;

namespace Mdex.Mcp.Tools.Tests;

public class ArrayArgNormalizerTests
{
    static JsonElement El(object v) => JsonSerializer.SerializeToElement(v);

    [Fact]
    public void Splits_comma_separated_string_for_array_param()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["type"] = El("area"),
            ["name"] = El("x"),
            ["tags"] = El("alpha, beta,gamma"),   // sent as a string
        };

        ArrayArgNormalizer.Normalize("create", args);

        Assert.Equal(JsonValueKind.Array, args["tags"].ValueKind);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, args["tags"].Deserialize<string[]>());
        Assert.Equal(JsonValueKind.String, args["name"].ValueKind);   // non-array param untouched
    }

    [Fact]
    public void Leaves_a_real_array_untouched()
    {
        var args = new Dictionary<string, JsonElement> { ["tags"] = El(new[] { "a", "b" }) };
        ArrayArgNormalizer.Normalize("create", args);
        Assert.Equal(new[] { "a", "b" }, args["tags"].Deserialize<string[]>());
    }

    [Fact]
    public void Recognizes_set_tags_array_params()
    {
        var args = new Dictionary<string, JsonElement> { ["add"] = El("x,y") };
        ArrayArgNormalizer.Normalize("set_tags", args);
        Assert.Equal(new[] { "x", "y" }, args["add"].Deserialize<string[]>());
    }

    [Fact]
    public void Ignores_unknown_tool()
    {
        var args = new Dictionary<string, JsonElement> { ["tags"] = El("x,y") };
        ArrayArgNormalizer.Normalize("not_a_tool", args);
        Assert.Equal(JsonValueKind.String, args["tags"].ValueKind);   // unchanged
    }
}
