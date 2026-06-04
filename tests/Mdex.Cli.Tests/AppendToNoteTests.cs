using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Write;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class AppendToNoteTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Appends_and_bumps_modified()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\nmodified: 2026-01-01\n---\n\n# A\n\nold\n");
        new AppendToNoteCommand().Run(new[] { "a", "--content", "new line" }, Ctx(v));
        var text = v.Read("zettel/a.md");
        Assert.Contains("new line", text);
        Assert.DoesNotContain("modified: 2026-01-01", text);
    }

    [Fact]
    public void Ambiguous_slug_throws_and_writes_nothing()
    {
        using var v = new TestVault();
        v.WriteNote("projects/a/index.md", "---\ntype: project\n---\n\n# A\n");
        v.WriteNote("projects/b/index.md", "---\ntype: project\n---\n\n# B\n");
        var ex = Assert.Throws<CommandException>(() =>
            new AppendToNoteCommand().Run(new[] { "index", "--content", "APPENDED" }, Ctx(v)));
        Assert.Contains("projects/a/index.md", ex.Message);
        Assert.Contains("projects/b/index.md", ex.Message);
        Assert.DoesNotContain("APPENDED", v.Read("projects/a/index.md"));
        Assert.DoesNotContain("APPENDED", v.Read("projects/b/index.md"));
    }
}
