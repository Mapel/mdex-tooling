using Mdex.Core.Cli;
using Mdex.Core.Commands.Write;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class CaptureInboxTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Creates_inbox_file_with_content()
    {
        using var v = new TestVault();
        var rel = new CaptureInboxCommand().Run(new[] { "--content", "Quick idea" }, Ctx(v))
            .Data().GetProperty("path").GetString()!;
        Assert.StartsWith("inbox/", rel);
        var written = v.Read(rel);
        Assert.Contains("type: inbox", written);
        Assert.Contains("Quick idea", written);
    }

    [Fact]
    public void Second_capture_same_day_increments_number()
    {
        using var v = new TestVault();
        var c = new CaptureInboxCommand();
        var a = c.Run(new[] { "--content", "one" }, Ctx(v)).Data().GetProperty("path").GetString();
        var b = c.Run(new[] { "--content", "two" }, Ctx(v)).Data().GetProperty("path").GetString();
        Assert.NotEqual(a, b);
    }
}
