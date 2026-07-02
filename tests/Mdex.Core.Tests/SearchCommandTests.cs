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

    [Fact]
    public void Searches_non_markdown_text_content()
    {
        using var v = new TestVault();
        v.WriteNote("resources/page.html", "<html><body>Widgets everywhere</body></html>");
        var d = new SearchCommand().Run(new[] { "Widgets" }, Ctx(v)).Data();
        Assert.Equal(1, d.GetProperty("hits").GetArrayLength());
        var hit = d.GetProperty("hits")[0];
        Assert.Equal("resources/page.html", hit.GetProperty("path").GetString());
        Assert.False(hit.TryGetProperty("slug", out _));   // non-note file → no slug
    }

    [Fact]
    public void Matches_files_and_folders_by_name()
    {
        using var v = new TestVault();
        v.WriteNote("projects/quantum-notes/todo.txt", "nothing relevant here");
        // 'quantum' appears only in the folder name, never in the content
        Assert.Equal(1, Hits(new SearchCommand().Run(new[] { "quantum" }, Ctx(v))));
    }

    [Fact]
    public void Binary_extension_matches_by_name_only()
    {
        using var v = new TestVault();
        v.WriteNote("resources/diagram.png", "\0\0secretword\0");
        Assert.Equal(1, Hits(new SearchCommand().Run(new[] { "diagram" }, Ctx(v))));      // name
        Assert.Equal(0, Hits(new SearchCommand().Run(new[] { "secretword" }, Ctx(v))));   // never content
    }

    [Fact]
    public void Text_extension_with_nul_bytes_is_treated_binary()
    {
        using var v = new TestVault();
        v.WriteNote("resources/data.json", "\0\0binarypayload");   // .json is whitelisted, but NUL wins
        Assert.Equal(1, Hits(new SearchCommand().Run(new[] { "data" }, Ctx(v))));           // name
        Assert.Equal(0, Hits(new SearchCommand().Run(new[] { "binarypayload" }, Ctx(v))));  // not content
    }
}
