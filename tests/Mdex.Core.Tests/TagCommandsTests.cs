using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class TagCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void ListTags_counts_and_orders()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [deep-work, fokus]\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ntags: [deep-work]\n---\n# B\n");
        var first = new ListTagsCommand().Run(Array.Empty<string>(), Ctx(v)).Data()
            .GetProperty("tags")[0];
        Assert.Equal("deep-work", first.GetProperty("tag").GetString());
        Assert.Equal(2, first.GetProperty("count").GetInt32());
    }

    [Fact]
    public void ListTags_pattern_filters_by_name()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [deep-work, fokus]\n---\n# A\n");
        var tags = new ListTagsCommand().Run(new[] { "work" }, Ctx(v)).Data()
            .GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("tag").GetString()).ToArray();
        Assert.Equal(new[] { "deep-work" }, tags);
    }

    [Fact]
    public void ListTags_top_caps_results()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [a, b]\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ntags: [a]\n---\n# B\n");
        var tags = new ListTagsCommand().Run(new[] { "--top", "1" }, Ctx(v)).Data()
            .GetProperty("tags").EnumerateArray().ToList();
        Assert.Single(tags);
        Assert.Equal("a", tags[0].GetProperty("tag").GetString());   // highest count wins
    }
}
