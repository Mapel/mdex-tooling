using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class TagCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
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
    public void SearchTags_substring_match()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [deep-work, fokus]\n---\n# A\n");
        var tags = new SearchTagsCommand().Run(new[] { "work" }, Ctx(v)).Data()
            .GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(new[] { "deep-work" }, tags);
    }
}
