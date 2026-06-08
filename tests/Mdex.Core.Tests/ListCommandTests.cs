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
    public void List_whole_vault_when_no_path_given()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n");

        var r = new ListCommand().Run(Array.Empty<string>(), Ctx(v));

        Assert.Equal(".", r.Data().GetProperty("root").GetString());
        Assert.Contains("zettel/a.md", Paths(r));
        Assert.Contains("projects/p/index.md", Paths(r));
    }

    [Fact]
    public void List_subpath_scopes_to_that_directory()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n");

        var paths = Paths(new ListCommand().Run(new[] { "zettel" }, Ctx(v))).ToList();

        Assert.Contains("zettel/a.md", paths);
        Assert.DoesNotContain("projects/p/index.md", paths);
    }

    [Fact]
    public void List_rejects_escaping_subpath()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new ListCommand().Run(new[] { "../escape" }, Ctx(v)));
    }
}
