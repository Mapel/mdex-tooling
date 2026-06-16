using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class SearchCommandTests
{
    static CliContext Ctx(TestVault v)
    {
        var guard = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, guard), Guard = guard,
            Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Finds_matching_line_with_snippet()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n\n# A\n\nEmergenz ist spannend.\n");
        var data = new SearchCommand().Run(new[] { "Emergenz" }, Ctx(v)).Data();
        Assert.Equal(1, data.GetProperty("hits").GetArrayLength());
    }

    [Fact]
    public void Requires_query()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new SearchCommand().Run(Array.Empty<string>(), Ctx(v)));
    }

    static int Hits(CommandResult r) => r.Data().GetProperty("hits").GetArrayLength();

    [Fact]
    public void Match_all_requires_every_term()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n\n# A\n\nEmergenz und Komplexität.\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\n---\n\n# B\n\nNur Emergenz hier.\n");
        // default --match all: both terms must appear → only a
        Assert.Equal(1, Hits(new SearchCommand().Run(new[] { "Emergenz Komplexität" }, Ctx(v))));
    }

    [Fact]
    public void Match_any_needs_one_term()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n\n# A\n\nEmergenz und Komplexität.\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\n---\n\n# B\n\nNur Emergenz hier.\n");
        Assert.Equal(2, Hits(new SearchCommand().Run(new[] { "Emergenz", "Komplexität", "--match", "any" }, Ctx(v))));
    }
}
