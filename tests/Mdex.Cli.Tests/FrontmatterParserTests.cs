using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class FrontmatterParserTests
{
    const string Sample =
        "---\ntype: zettel\ncreated: 2026-06-01T10:00:00\nmodified: 2026-06-02\n" +
        "tags: [emergenz, denken]\nsource:\n---\n\n# Emergenz\n\nBody text.\n";

    [Fact]
    public void Parses_typed_fields_and_tags()
    {
        var r = FrontmatterParser.Parse(Sample);
        Assert.Equal("zettel", r.Frontmatter.Type);
        Assert.Equal("2026-06-01T10:00:00", r.Frontmatter.Created);
        Assert.Equal("2026-06-02", r.Frontmatter.Modified);
        Assert.Equal(new[] { "emergenz", "denken" }, r.Frontmatter.Tags);
    }

    [Fact]
    public void Separates_body()
    {
        var r = FrontmatterParser.Parse(Sample);
        Assert.StartsWith("# Emergenz", r.Body.TrimStart());
    }

    [Fact]
    public void No_frontmatter_yields_empty_model_and_full_body()
    {
        var r = FrontmatterParser.Parse("# Just a heading\n\ntext\n");
        Assert.Null(r.Frontmatter.Type);
        Assert.StartsWith("# Just a heading", r.Body);
    }
}
