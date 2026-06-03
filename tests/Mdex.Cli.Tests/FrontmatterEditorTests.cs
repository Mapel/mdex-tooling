using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class FrontmatterEditorTests
{
    [Fact]
    public void SetField_replaces_existing_line()
    {
        var text = "---\ntype: inbox\ncreated: 2026-06-01T09:00:00\nmodified: 2026-06-01\n---\n\nbody\n";
        var updated = FrontmatterEditor.SetField(text, "modified", "2026-06-02");
        Assert.Contains("modified: 2026-06-02", updated);
        Assert.DoesNotContain("modified: 2026-06-01", updated);
        Assert.Contains("body", updated);
    }
}
