using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

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
        var note = Assert.Single(Repo(v).FindBySlugOrPath("no-heading"));
        Assert.Equal("no-heading", note.Title);
    }

    [Fact]
    public void FindBySlugOrPath_accepts_relative_path()
    {
        using var v = new TestVault();
        v.WriteNote("projects/x/index.md", "---\ntype: project\n---\n\n# X\n");
        var note = Assert.Single(Repo(v).FindBySlugOrPath("projects/x/index.md"));
        Assert.Equal("index", note.Slug);
    }

    [Fact]
    public void FindBySlugOrPath_returns_all_notes_sharing_a_slug()
    {
        using var v = new TestVault();
        v.WriteNote("projects/a/index.md", "---\ntype: project\n---\n\n# A\n");
        v.WriteNote("projects/b/index.md", "---\ntype: project\n---\n\n# B\n");
        var matches = Repo(v).FindBySlugOrPath("index");
        Assert.Equal(2, matches.Count);
        Assert.All(matches, n => Assert.Equal("index", n.Slug));
    }

    [Fact]
    public void FindBySlugOrPath_path_hit_is_unambiguous_despite_shared_slug()
    {
        using var v = new TestVault();
        v.WriteNote("projects/a/index.md", "---\ntype: project\n---\n\n# A\n");
        v.WriteNote("projects/b/index.md", "---\ntype: project\n---\n\n# B\n");
        var note = Assert.Single(Repo(v).FindBySlugOrPath("projects/b/index.md"));
        Assert.Equal("projects/b/index.md", note.RelativePath);
    }
}
