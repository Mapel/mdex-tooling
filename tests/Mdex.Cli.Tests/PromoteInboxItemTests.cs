using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Write;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class PromoteInboxItemTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
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
}
