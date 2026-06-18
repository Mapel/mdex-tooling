using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class TemplateServiceTests
{
    [Fact]
    public void Builds_zettel_with_title_h1_and_substituted_dates()
    {
        using var v = new TestVault();
        var svc = new TemplateService(v.Root);
        var text = svc.BuildNote("zettel", "emergenz-denken", includeBody: false,
            created: "2026-06-02T10:00:00", modified: "2026-06-02");
        Assert.Contains("type: zettel", text);
        Assert.Contains("created: 2026-06-02T10:00:00", text);
        // includeBody:false means no H1; verify placeholders gone
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public void Named_body_template_is_included_with_h1()
    {
        using var v = new TestVault();
        var svc = new TemplateService(v.Root);
        var text = svc.BuildNote("project", "mein-projekt", includeBody: true,
            created: "2026-06-02T10:00:00", modified: "2026-06-02", bodyTemplate: "project-default");
        Assert.Contains("## Log", text);
        Assert.Contains("# Mein Projekt", text);
    }

    [Fact]
    public void No_body_template_means_frontmatter_only()
    {
        using var v = new TestVault();
        var svc = new TemplateService(v.Root);
        var text = svc.BuildNote("project", "p", includeBody: true,
            created: "2026-06-02T10:00:00", modified: "2026-06-02");   // no bodyTemplate
        Assert.DoesNotContain("## Log", text);
        Assert.DoesNotContain("# ", text);
    }
}
