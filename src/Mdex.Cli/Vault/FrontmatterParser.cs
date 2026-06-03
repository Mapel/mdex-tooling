using YamlDotNet.Serialization;

namespace Mdex.Cli.Vault;

public static class FrontmatterParser
{
    static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    public static ParseResult Parse(string text)
    {
        text = text.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n"))
            return new ParseResult { Frontmatter = new Frontmatter(), Body = text };

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
            return new ParseResult { Frontmatter = new Frontmatter(), Body = text };

        var yaml = text.Substring(4, end - 4);
        var afterMarker = text.IndexOf('\n', end + 1);
        var body = afterMarker < 0 ? "" : text[(afterMarker + 1)..];

        return new ParseResult { Frontmatter = FromYaml(yaml), Body = body };
    }

    static Frontmatter FromYaml(string yaml)
    {
        var fm = new Frontmatter();
        Dictionary<string, object?>? map;
        try { map = Yaml.Deserialize<Dictionary<string, object?>>(yaml); }
        catch { return fm; }
        if (map is null) return fm;

        foreach (var (key, value) in map)
        {
            switch (key)
            {
                case "type": fm.Type = value?.ToString(); break;
                case "created": fm.Created = value?.ToString(); break;
                case "modified": fm.Modified = value?.ToString(); break;
                case "tags": fm.Tags = AsList(value); break;
                default: fm.Extra[key] = value; break;
            }
        }
        return fm;
    }

    static List<string> AsList(object? value) => value switch
    {
        IEnumerable<object> seq => seq.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList(),
        string s when s.Length > 0 => new List<string> { s },
        _ => new List<string>(),
    };
}
