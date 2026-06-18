using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class ListTemplatesCommandTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Lists_body_templates_with_content()
    {
        using var v = new TestVault();
        var data = new ListTemplatesCommand().Run(Array.Empty<string>(), Ctx(v)).Data();
        var templates = data.GetProperty("templates");
        Assert.Equal(1, templates.GetArrayLength());
        Assert.Equal("project-default", templates[0].GetProperty("name").GetString());
        Assert.Contains("## Ziel", templates[0].GetProperty("content").GetString());
    }
}
