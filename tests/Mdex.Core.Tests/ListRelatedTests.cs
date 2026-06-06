using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class ListRelatedTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void List_returns_entries_under_subpath()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        var entries = new ListCommand().Run(new[] { "zettel" }, Ctx(v)).Data()
            .GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("path").GetString());
        Assert.Contains("zettel/a.md", entries);
    }

    [Fact]
    public void FindRelated_follows_wikilinks_both_directions()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n\nSiehe [[b]].\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\n---\n# B\n");
        v.WriteNote("zettel/c.md", "---\ntype: zettel\n---\n# C\n\nlinks [[a]]\n");
        var slugs = new FindRelatedCommand().Run(new[] { "a" }, Ctx(v)).Data()
            .GetProperty("related").EnumerateArray()
            .Select(n => n.GetProperty("slug").GetString()).ToList();
        Assert.Contains("b", slugs); // outgoing
        Assert.Contains("c", slugs); // incoming
    }

    [Fact]
    public void FindRelated_accepts_a_path()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n\nSiehe [[b]].\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\n---\n# B\n");
        var slugs = new FindRelatedCommand().Run(new[] { "zettel/a.md" }, Ctx(v)).Data()
            .GetProperty("related").EnumerateArray()
            .Select(n => n.GetProperty("slug").GetString()).ToList();
        Assert.Contains("b", slugs);
    }

    [Fact]
    public void FindRelated_ambiguous_slug_lists_paths()
    {
        using var v = new TestVault();
        v.WriteNote("projects/a/index.md", "---\ntype: project\n---\n# A\n");
        v.WriteNote("projects/b/index.md", "---\ntype: project\n---\n# B\n");
        var ex = Assert.Throws<CommandException>(() =>
            new FindRelatedCommand().Run(new[] { "index" }, Ctx(v)));
        Assert.Contains("projects/a/index.md", ex.Message);
        Assert.Contains("projects/b/index.md", ex.Message);
    }

    // a → b → c → d chain (outgoing links only).
    static void WriteChain(TestVault v)
    {
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n\n[[b]]\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\n---\n# B\n\n[[c]]\n");
        v.WriteNote("zettel/c.md", "---\ntype: zettel\n---\n# C\n\n[[d]]\n");
        v.WriteNote("zettel/d.md", "---\ntype: zettel\n---\n# D\n");
    }

    static List<string?> Related(TestVault v, params string[] args) =>
        new FindRelatedCommand().Run(args, Ctx(v)).Data()
            .GetProperty("related").EnumerateArray()
            .Select(n => n.GetProperty("slug").GetString()).ToList();

    [Fact]
    public void FindRelated_default_is_one_hop()
    {
        using var v = new TestVault();
        WriteChain(v);
        var slugs = Related(v, "a");
        Assert.Contains("b", slugs);
        Assert.DoesNotContain("c", slugs);
    }

    [Fact]
    public void FindRelated_recursive_walks_whole_chain()
    {
        using var v = new TestVault();
        WriteChain(v);
        var slugs = Related(v, "a", "--recursive");
        Assert.Equal(new[] { "b", "c", "d" }, slugs.OrderBy(s => s));
    }

    [Fact]
    public void FindRelated_maxhops_caps_depth()
    {
        using var v = new TestVault();
        WriteChain(v);
        var slugs = Related(v, "a", "--maxhops", "2");
        Assert.Contains("b", slugs);
        Assert.Contains("c", slugs);
        Assert.DoesNotContain("d", slugs);   // 3rd hop not taken
    }

    [Fact]
    public void FindRelated_maxhops_wins_over_recursive()
    {
        using var v = new TestVault();
        WriteChain(v);
        var slugs = Related(v, "a", "--maxhops", "1", "--recursive");
        Assert.Contains("b", slugs);
        Assert.DoesNotContain("c", slugs);   // capped at 1 despite --recursive
    }

    [Fact]
    public void FindRelated_recursive_terminates_on_cycle()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n\n[[b]]\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\n---\n# B\n\n[[c]]\n");
        v.WriteNote("zettel/c.md", "---\ntype: zettel\n---\n# C\n\n[[a]]\n"); // closes the loop
        var slugs = Related(v, "a", "--recursive");
        Assert.Equal(new[] { "b", "c" }, slugs.OrderBy(s => s));   // terminates, start excluded
    }
}
