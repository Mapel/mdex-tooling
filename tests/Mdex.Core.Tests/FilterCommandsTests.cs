using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class FilterCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    static int Count(CommandResult r) => r.Data().GetProperty("count").GetInt32();

    [Fact]
    public void FindNotes_by_tag_requires_all_by_default()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x, y]\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ntags: [x]\n---\n# B\n");
        Assert.Equal(1, Count(new FindNotesCommand().Run(new[] { "--tag", "x", "--tag", "y" }, Ctx(v))));
        Assert.Equal(2, Count(new FindNotesCommand().Run(new[] { "--tag", "x" }, Ctx(v))));
    }

    [Fact]
    public void FindNotes_by_tag_match_any()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x, y]\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ntags: [z]\n---\n# B\n");
        Assert.Equal(2, Count(new FindNotesCommand().Run(new[] { "--tag", "x", "--tag", "z", "--match", "any" }, Ctx(v))));
        Assert.Equal(0, Count(new FindNotesCommand().Run(new[] { "--tag", "x", "--tag", "z", "--match", "all" }, Ctx(v))));
    }

    [Fact]
    public void FindNotes_by_type()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n");
        Assert.Equal(1, Count(new FindNotesCommand().Run(new[] { "--type", "project" }, Ctx(v))));
    }

    [Fact]
    public void FindNotes_by_date_range_inclusive_on_created()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ncreated: 2026-05-01T09:00:00\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ncreated: 2026-06-10T09:00:00\n---\n# B\n");
        var r = new FindNotesCommand().Run(new[] { "--from", "2026-05-01", "--to", "2026-05-31" }, Ctx(v));
        Assert.Equal(1, Count(r));
    }

    [Fact]
    public void FindNotes_open_ended_with_only_from()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ncreated: 2026-05-01T09:00:00\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ncreated: 2026-06-10T09:00:00\n---\n# B\n");
        Assert.Equal(1, Count(new FindNotesCommand().Run(new[] { "--from", "2026-06-01" }, Ctx(v))));
    }

    [Fact]
    public void FindNotes_combines_type_and_tag_as_AND()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x]\n---\n# A\n");
        v.WriteNote("projects/p/index.md", "---\ntype: project\ntags: [x]\n---\n# P\n");
        Assert.Equal(1, Count(new FindNotesCommand().Run(new[] { "--type", "zettel", "--tag", "x" }, Ctx(v))));
    }

    [Fact]
    public void FindNotes_requires_at_least_one_filter()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new FindNotesCommand().Run(Array.Empty<string>(), Ctx(v)));
    }
}
