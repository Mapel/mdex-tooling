namespace Mdex.Core.Tests;

public class TestVaultTests
{
    [Fact]
    public void Creates_braid_folders_and_templates()
    {
        using var v = new TestVault();
        Assert.True(Directory.Exists(Path.Combine(v.Root, "inbox")));
        Assert.True(File.Exists(Path.Combine(v.Root, "resources", "templates", "frontmatter-zettel.md")));
    }
}
