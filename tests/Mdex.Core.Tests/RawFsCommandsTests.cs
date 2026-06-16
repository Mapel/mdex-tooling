using Mdex.Core.Cli;
using Mdex.Core.Commands.Fs;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class RawFsCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void WriteFile_then_Delete()
    {
        using var v = new TestVault();
        var ctx = Ctx(v);
        new WriteFileCommand().Run(new[] { "scratch/x.md", "--content", "one" }, ctx);
        Assert.True(File.Exists(Path.Combine(v.Root, "scratch", "x.md")));
        new DeleteCommand().Run(new[] { "scratch/x.md" }, ctx);
        Assert.False(File.Exists(Path.Combine(v.Root, "scratch", "x.md")));
    }

    [Fact]
    public void WriteFile_overwrites_existing()
    {
        using var v = new TestVault();
        var ctx = Ctx(v);
        new WriteFileCommand().Run(new[] { "scratch/x.md", "--content", "one" }, ctx);
        new WriteFileCommand().Run(new[] { "scratch/x.md", "--content", "two" }, ctx);
        Assert.Equal("two", v.Read("scratch/x.md"));
    }

    [Fact]
    public void WriteFile_rejects_escape()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() =>
            new WriteFileCommand().Run(new[] { "../evil.md", "--content", "x" }, Ctx(v)));
    }

    [Fact]
    public void Delete_missing_throws()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new DeleteCommand().Run(new[] { "scratch/nope.md" }, Ctx(v)));
    }
}
