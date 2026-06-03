using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Read;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class ListRelatedTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
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
}
