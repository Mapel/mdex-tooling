using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class ListCommandTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    static IEnumerable<string> Paths(CommandResult r) =>
        r.Data().GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("path").GetString()!);

    [Fact]
    public void List_default_depth_one_shows_only_direct_children()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n");

        var r = new ListCommand().Run(Array.Empty<string>(), Ctx(v));

        Assert.Equal(".", r.Data().GetProperty("root").GetString());
        var paths = Paths(r).ToList();
        Assert.Contains("zettel", paths);                 // direct child dir
        Assert.DoesNotContain("zettel/a.md", paths);      // depth 2 — hidden by default
        Assert.DoesNotContain("projects/p/index.md", paths);
    }

    [Fact]
    public void List_depth_all_is_fully_recursive()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n");

        var paths = Paths(new ListCommand().Run(new[] { "--depth", "all" }, Ctx(v))).ToList();
        Assert.Contains("zettel/a.md", paths);
        Assert.Contains("projects/p/index.md", paths);
    }

    [Fact]
    public void List_depth_two_reaches_grandchildren()
    {
        using var v = new TestVault();
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n");
        var paths = Paths(new ListCommand().Run(new[] { "--depth", "2" }, Ctx(v))).ToList();
        Assert.Contains("projects/p", paths);             // depth 2
        Assert.DoesNotContain("projects/p/index.md", paths); // depth 3
    }

    [Fact]
    public void List_subpath_scopes_to_that_directory()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n");

        var paths = Paths(new ListCommand().Run(new[] { "zettel" }, Ctx(v))).ToList();

        Assert.Contains("zettel/a.md", paths);            // direct child of the subpath
        Assert.DoesNotContain("projects/p/index.md", paths);
    }

    [Fact]
    public void List_rejects_escaping_subpath()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new ListCommand().Run(new[] { "../escape" }, Ctx(v)));
    }
}
