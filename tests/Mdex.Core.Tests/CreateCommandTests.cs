using Mdex.Core.Cli;
using Mdex.Core.Commands.Write;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class CreateCommandTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Creates_zettel_flat()
    {
        using var v = new TestVault();
        var path = new CreateCommand().Run(new[] { "zettel", "Emergenz Denken" }, Ctx(v))
            .Data().GetProperty("path").GetString();
        Assert.Equal("zettel/emergenz-denken.md", path);
    }

    [Fact]
    public void Creates_project_with_subpath()
    {
        using var v = new TestVault();
        var path = new CreateCommand().Run(new[] { "project", "garten/pflanzplan" }, Ctx(v))
            .Data().GetProperty("path").GetString();
        Assert.Equal("projects/garten/pflanzplan.md", path);
    }

    [Fact]
    public void Applies_passed_tags_to_frontmatter()
    {
        using var v = new TestVault();
        var path = new CreateCommand()
            .Run(new[] { "area", "tagged", "--tag", "nixos", "--tag", "architecture" }, Ctx(v))
            .Data().GetProperty("path").GetString();
        var text = File.ReadAllText(Path.Combine(v.Root, path!));
        Assert.Contains("tags: [nixos, architecture]", text);
    }

    [Fact]
    public void Refuses_inbox()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new CreateCommand().Run(new[] { "inbox", "x" }, Ctx(v)));
    }

    [Fact]
    public void Refuses_overwrite()
    {
        using var v = new TestVault();
        var ctx = Ctx(v);
        new CreateCommand().Run(new[] { "zettel", "dup" }, ctx);
        Assert.Throws<CommandException>(() => new CreateCommand().Run(new[] { "zettel", "dup" }, ctx));
    }
}
