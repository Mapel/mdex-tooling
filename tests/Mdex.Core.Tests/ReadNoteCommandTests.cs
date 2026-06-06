using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

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

    [Fact]
    public void Ambiguous_slug_lists_all_paths_and_returns_no_content()
    {
        using var v = new TestVault();
        v.WriteNote("projects/a/index.md", "---\ntype: project\n---\n\n# A\n");
        v.WriteNote("projects/b/index.md", "---\ntype: project\n---\n\n# B\n");
        var ex = Assert.Throws<CommandException>(() => new ReadNoteCommand().Run(new[] { "index" }, Ctx(v)));
        Assert.Contains("projects/a/index.md", ex.Message);
        Assert.Contains("projects/b/index.md", ex.Message);

        // candidates also ride along as structured JSON details for machine consumers
        var json = JsonEnvelope.Err("read-note", ex.Message, ex.Details);
        Assert.Contains("\"candidates\"", json);
        Assert.Contains("projects/a/index.md", json);
    }

    [Fact]
    public void Explicit_path_reads_one_of_shared_slug()
    {
        using var v = new TestVault();
        v.WriteNote("projects/a/index.md", "---\ntype: project\n---\n\n# A\n\nAlpha\n");
        v.WriteNote("projects/b/index.md", "---\ntype: project\n---\n\n# B\n\nBeta\n");
        var d = new ReadNoteCommand().Run(new[] { "projects/b/index.md" }, Ctx(v)).Data();
        Assert.Contains("Beta", d.GetProperty("body").GetString());
    }
}
