using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

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

    [Fact]
    public void SetTags_replaces_inline_list()
    {
        var text = "---\ntype: zettel\ntags: [old, stale]\n---\n\nbody\n";
        var updated = FrontmatterEditor.SetTags(text, new[] { "fresh", "new" });
        Assert.Contains("tags: [fresh, new]", updated);
        Assert.DoesNotContain("old", updated);
        Assert.Contains("body", updated);
    }

    [Fact]
    public void SetTags_replaces_block_list_and_drops_items()
    {
        var text = "---\ntype: zettel\ntags:\n  - old\n  - stale\nmodified: 2026-01-01\n---\n\nbody\n";
        var updated = FrontmatterEditor.SetTags(text, new[] { "fresh" });
        Assert.Contains("tags: [fresh]", updated);
        Assert.DoesNotContain("- old", updated);
        Assert.DoesNotContain("- stale", updated);
        Assert.Contains("modified: 2026-01-01", updated);   // sibling fields survive
    }

    [Fact]
    public void SetTags_inserts_when_absent()
    {
        var text = "---\ntype: zettel\n---\n\nbody\n";
        var updated = FrontmatterEditor.SetTags(text, new[] { "x" });
        Assert.Contains("tags: [x]", updated);
    }
}
