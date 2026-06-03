using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class SlugifierTests
{
    [Theory]
    [InlineData("Emergenz Denken", "emergenz-denken")]
    [InlineData("Café & Bar!!", "caf-bar")]   // é outside keep-set [a-z0-9äöüß-] -> -, then & ! collapsed, trimmed
    [InlineData("  Über_Alles  ", "über-alles")]
    [InlineData("ÄÖÜ-ß", "äöü-ß")]
    public void Slugify_matches_pkmt_rules(string input, string expected)
        => Assert.Equal(expected, Slugifier.Slugify(input));
}
