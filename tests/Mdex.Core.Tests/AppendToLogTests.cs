using Mdex.Core.Cli;
using Mdex.Core.Commands.Write;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class AppendToLogTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Creates_dated_subsection_under_log()
    {
        using var v = new TestVault();
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        v.WriteNote("projects/p/index.md",
            "---\ntype: project\nmodified: 2026-01-01\n---\n\n# P\n\n## Log\n### 2026-01-01\n- old entry\n");
        new AppendToLogCommand().Run(new[] { "index", "--content", "did a thing" }, Ctx(v));
        var text = v.Read("projects/p/index.md");
        Assert.Contains($"### {today}", text);
        Assert.Contains("- did a thing", text);
    }

    [Fact]
    public void Adds_log_section_when_missing()
    {
        using var v = new TestVault();
        v.WriteNote("projects/q/index.md", "---\ntype: project\n---\n\n# Q\n\n## Ziel\nx\n");
        new AppendToLogCommand().Run(new[] { "projects/q/index.md", "--content", "kickoff" }, Ctx(v));
        var text = v.Read("projects/q/index.md");
        Assert.Contains("## Log", text);
        Assert.Contains("- kickoff", text);
    }
}
