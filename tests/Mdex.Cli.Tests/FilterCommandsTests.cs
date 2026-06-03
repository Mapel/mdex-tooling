using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Read;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class FilterCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    static int Count(CommandResult r) => r.Data().GetProperty("count").GetInt32();

    [Fact]
    public void GetByTag_requires_all_tags()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x, y]\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ntags: [x]\n---\n# B\n");
        Assert.Equal(1, Count(new GetByTagCommand().Run(new[] { "x", "y" }, Ctx(v))));
        Assert.Equal(2, Count(new GetByTagCommand().Run(new[] { "x" }, Ctx(v))));
    }

    [Fact]
    public void GetByType_filters_on_frontmatter_type()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n");
        Assert.Equal(1, Count(new GetByTypeCommand().Run(new[] { "project" }, Ctx(v))));
    }

    [Fact]
    public void GetByDateRange_inclusive_on_created()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ncreated: 2026-05-01T09:00:00\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ncreated: 2026-06-10T09:00:00\n---\n# B\n");
        var r = new GetByDateRangeCommand().Run(new[] { "--from", "2026-05-01", "--to", "2026-05-31" }, Ctx(v));
        Assert.Equal(1, Count(r));
    }
}
