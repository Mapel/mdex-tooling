using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class NoteRepositoryTests
{
    static NoteRepository Repo(TestVault v) => new(v.Root, new PathGuard(v.Root));

    [Fact]
    public void LoadAll_finds_notes_and_skips_templates()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/emergenz.md",
            "---\ntype: zettel\ntags: [denken]\n---\n\n# Emergenz\n\nText\n");
        var notes = Repo(v).LoadAll();
        Assert.Contains(notes, n => n.Slug == "emergenz" && n.Type() == "zettel");
        Assert.DoesNotContain(notes, n => n.RelativePath.StartsWith("resources/templates"));
    }

    [Fact]
    public void Title_falls_back_to_slug_without_h1()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/no-heading.md", "---\ntype: zettel\n---\n\nJust text\n");
        var note = Repo(v).FindBySlugOrPath("no-heading");
        Assert.NotNull(note);
        Assert.Equal("no-heading", note!.Title);
    }

    [Fact]
    public void FindBySlugOrPath_accepts_relative_path()
    {
        using var v = new TestVault();
        v.WriteNote("projects/x/index.md", "---\ntype: project\n---\n\n# X\n");
        var note = Repo(v).FindBySlugOrPath("projects/x/index.md");
        Assert.NotNull(note);
        Assert.Equal("index", note!.Slug);
    }
}
