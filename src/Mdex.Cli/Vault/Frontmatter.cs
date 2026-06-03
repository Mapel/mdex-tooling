namespace Mdex.Cli.Vault;

public sealed class Frontmatter
{
    public string? Type { get; set; }
    public string? Created { get; set; }
    public string? Modified { get; set; }
    public List<string> Tags { get; set; } = new();
    /// <summary>Raw key/value for any other fields (author, source, backlinks…).</summary>
    public Dictionary<string, object?> Extra { get; set; } = new();
}

public sealed class Note
{
    public required string Path { get; init; }          // absolute
    public required string RelativePath { get; init; }   // forward-slash, vault-relative
    public required string Slug { get; init; }           // filename without .md
    public required string Title { get; init; }          // first H1 or slug
    public required Frontmatter Frontmatter { get; init; }
    public required string Body { get; init; }

    public NoteSummary ToSummary() => new()
    {
        Path = RelativePath, Slug = Slug, Title = Title,
        Type = Frontmatter.Type, Tags = Frontmatter.Tags,
        Created = Frontmatter.Created, Modified = Frontmatter.Modified,
    };
}

public sealed class NoteSummary
{
    public required string Path { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string? Type { get; init; }
    public List<string> Tags { get; init; } = new();
    public string? Created { get; init; }
    public string? Modified { get; init; }
}

public sealed class ParseResult
{
    public required Frontmatter Frontmatter { get; init; }
    public required string Body { get; init; }
}

public static class NoteExtensions
{
    public static string? Type(this Note n) => n.Frontmatter.Type;
}
