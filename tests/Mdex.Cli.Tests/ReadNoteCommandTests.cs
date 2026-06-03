using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Read;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class ReadNoteCommandTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Returns_body_for_known_slug()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n\n# A\n\nHello\n");
        var d = new ReadNoteCommand().Run(new[] { "a" }, Ctx(v)).Data();
        Assert.Equal("a", d.GetProperty("slug").GetString());
        Assert.Contains("Hello", d.GetProperty("body").GetString());
    }

    [Fact]
    public void Throws_when_missing()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new ReadNoteCommand().Run(new[] { "nope" }, Ctx(v)));
    }
}
