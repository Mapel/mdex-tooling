using Mdex.Core.Cli;
namespace Mdex.Core.Tests;

public class CommandEngineTests
{
    [Fact]
    public void Runs_a_no_vault_command()
    {
        var engine = CommandEngine.Default();
        var result = engine.Run("version", Array.Empty<string>(), rootFlag: null);
        Assert.Contains("version", System.Text.Json.JsonSerializer.Serialize(result.Data));
    }

    [Fact]
    public void Runs_a_vault_command_against_a_root()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x]\n---\n# A\n");
        var engine = CommandEngine.Default();
        var result = engine.Run("list-tags", Array.Empty<string>(), rootFlag: v.Root);
        Assert.Contains("\"tag\":\"x\"",
            System.Text.Json.JsonSerializer.Serialize(result.Data).Replace(" ", ""));
    }

    [Fact]
    public void Unknown_command_throws_CommandException()
    {
        var engine = CommandEngine.Default();
        Assert.Throws<CommandException>(
            () => engine.Run("nope", Array.Empty<string>(), rootFlag: null));
    }
}
