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
    public void Project_without_body_template_has_no_body()
    {
        using var v = new TestVault();
        var path = new CreateCommand().Run(new[] { "project", "p1" }, Ctx(v))
            .Data().GetProperty("path").GetString();
        Assert.DoesNotContain("## Ziel", v.Read(path!));   // project-default no longer auto-applied
    }

    [Fact]
    public void Body_template_is_applied_when_named()
    {
        using var v = new TestVault();
        var path = new CreateCommand().Run(new[] { "project", "p2", "--body-template", "project-default" }, Ctx(v))
            .Data().GetProperty("path").GetString();
        Assert.Contains("## Ziel", v.Read(path!));
    }

    [Fact]
    public void Unknown_body_template_reports_available()
    {
        using var v = new TestVault();
        var ex = Assert.Throws<CommandException>(() =>
            new CreateCommand().Run(new[] { "area", "x", "--body-template", "nope" }, Ctx(v)));
        Assert.Contains("Available: project-default", ex.Message);
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
