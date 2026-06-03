using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Fs;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class RawFsCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void WriteFile_then_AppendFile()
    {
        using var v = new TestVault();
        new WriteFileCommand().Run(new[] { "scratch/x.md", "--content", "one" }, Ctx(v));
        new AppendFileCommand().Run(new[] { "scratch/x.md", "--content", "two" }, Ctx(v));
        var text = v.Read("scratch/x.md");
        Assert.Contains("one", text);
        Assert.Contains("two", text);
    }

    [Fact]
    public void Move_and_Delete()
    {
        using var v = new TestVault();
        var ctx = Ctx(v);
        new WriteFileCommand().Run(new[] { "scratch/a.md", "--content", "x" }, ctx);
        new MoveCommand().Run(new[] { "scratch/a.md", "scratch/b.md" }, ctx);
        Assert.True(File.Exists(Path.Combine(v.Root, "scratch", "b.md")));
        new DeleteCommand().Run(new[] { "scratch/b.md" }, ctx);
        Assert.False(File.Exists(Path.Combine(v.Root, "scratch", "b.md")));
    }

    [Fact]
    public void WriteFile_rejects_escape()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() =>
            new WriteFileCommand().Run(new[] { "../evil.md", "--content", "x" }, Ctx(v)));
    }
}
