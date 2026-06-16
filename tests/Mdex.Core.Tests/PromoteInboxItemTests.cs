using Mdex.Core.Cli;
using Mdex.Core.Commands.Write;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class PromoteInboxItemTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Promotes_inbox_to_zettel_and_removes_original()
    {
        using var v = new TestVault();
        v.WriteNote("inbox/20260601-01.md",
            "---\ntype: inbox\ncreated: 2026-06-01T09:00:00\nmodified: 2026-06-01\nbacklinks: []\n---\n\nEine rohe Idee.\n");
        var to = new PromoteInboxItemCommand().Run(
            new[] { "20260601-01", "zettel", "--name", "rohe-idee" }, Ctx(v))
            .Data().GetProperty("to").GetString();

        Assert.Equal("zettel/rohe-idee.md", to);
        Assert.False(File.Exists(Path.Combine(v.Root, "inbox", "20260601-01.md")));
        var text = v.Read(to);
        Assert.Contains("type: zettel", text);
        Assert.Contains("created: 2026-06-01T09:00:00", text);   // preserved
        Assert.Contains("Eine rohe Idee.", text);
    }

    [Fact]
    public void Promotes_legacy_item_with_incomplete_frontmatter()
    {
        using var v = new TestVault();
        // Older capture: only `tags: []`, no type/created — used to make promote fail.
        v.WriteNote("inbox/20260601-02.md", "---\ntags: []\n---\n\nAlte rohe Idee.\n");
        var to = new PromoteInboxItemCommand().Run(
            new[] { "20260601-02", "zettel", "--name", "alt" }, Ctx(v))
            .Data().GetProperty("to").GetString();

        Assert.Equal("zettel/alt.md", to);
        var text = v.Read(to);
        Assert.Contains("type: zettel", text);     // frontmatter rebuilt
        Assert.Contains("created:", text);          // repaired
        Assert.Contains("Alte rohe Idee.", text);
    }
}
