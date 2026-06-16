using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class ReadNoteCommandTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Returns_body_for_known_slug()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n\n# A\n\nHello\n");
        var d = new ReadNoteCommand().Run(new[] { "a" }, Ctx(v)).Data();
        var note = d.GetProperty("notes")[0];
        Assert.Equal("a", note.GetProperty("slug").GetString());
        Assert.Contains("Hello", note.GetProperty("body").GetString());
        Assert.Empty(d.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public void Reads_multiple_targets()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n\n# A\n\nAlpha\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\n---\n\n# B\n\nBeta\n");
        var notes = new ReadNoteCommand().Run(new[] { "a", "b" }, Ctx(v)).Data()
            .GetProperty("notes").EnumerateArray()
            .Select(n => n.GetProperty("slug").GetString()).ToList();
        Assert.Equal(new[] { "a", "b" }, notes);
    }

    [Fact]
    public void Partial_success_returns_found_and_errors()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n\n# A\n\nAlpha\n");
        var d = new ReadNoteCommand().Run(new[] { "a", "nope" }, Ctx(v)).Data();
        Assert.Single(d.GetProperty("notes").EnumerateArray());
        var err = d.GetProperty("errors")[0];
        Assert.Equal("nope", err.GetProperty("target").GetString());
    }

    [Fact]
    public void Throws_when_all_targets_missing()
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
        Assert.Contains("Beta", d.GetProperty("notes")[0].GetProperty("body").GetString());
    }
}
