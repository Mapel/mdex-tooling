using Mdex.Core.Cli;
using Mdex.Core.Commands.Write;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class CaptureInboxTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Creates_inbox_file_with_title_and_content()
    {
        using var v = new TestVault();
        var rel = new CaptureInboxCommand().Run(new[] { "--title", "zk evaluieren", "--content", "Quick idea" }, Ctx(v))
            .Data().GetProperty("path").GetString()!;
        Assert.StartsWith("inbox/", rel);
        Assert.EndsWith("-zk-evaluieren.md", rel);          // YYYYMMDD-<title-slug>.md
        var written = v.Read(rel);
        Assert.Contains("type: inbox", written);
        Assert.Contains("# zk evaluieren", written);        // title becomes the H1
        Assert.Contains("Quick idea", written);
    }

    [Fact]
    public void Title_is_required()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() =>
            new CaptureInboxCommand().Run(new[] { "--content", "no title" }, Ctx(v)));
    }

    [Fact]
    public void Content_is_required()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() =>
            new CaptureInboxCommand().Run(new[] { "--title", "no body" }, Ctx(v)));
    }

    [Fact]
    public void Same_title_same_day_gets_a_distinct_filename()
    {
        using var v = new TestVault();
        var c = new CaptureInboxCommand();
        var a = c.Run(new[] { "--title", "idee", "--content", "one" }, Ctx(v)).Data().GetProperty("path").GetString();
        var b = c.Run(new[] { "--title", "idee", "--content", "two" }, Ctx(v)).Data().GetProperty("path").GetString();
        Assert.NotEqual(a, b);
    }
}
