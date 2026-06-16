namespace Mdex.Core.Vault;

public static class FrontmatterEditor
{
    /// <summary>Replace `key: ...` inside the leading frontmatter block; insert before closing --- if absent.</summary>
    public static string SetField(string text, string key, string value)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count == 0 || lines[0] != "---") return text;

        var close = lines.FindIndex(1, l => l == "---");
        if (close < 0) return text;

        var idx = lines.FindIndex(1, close - 1, l => l.StartsWith(key + ":"));
        if (idx >= 0) lines[idx] = $"{key}: {value}";
        else lines.Insert(close, $"{key}: {value}");

        return string.Join(nl, lines);
    }

    /// <summary>
    /// Replace the frontmatter's <c>tags</c> with an inline list (<c>tags: [a, b, c]</c>).
    /// Handles both inline (<c>tags: [..]</c>) and block (<c>tags:\n  - a</c>) source formats,
    /// dropping any leftover block items. Inserts a <c>tags:</c> line before the closing --- if absent.
    /// </summary>
    public static string SetTags(string text, IEnumerable<string> tags)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count == 0 || lines[0] != "---") return text;

        var close = lines.FindIndex(1, l => l == "---");
        if (close < 0) return text;

        var rendered = "tags: [" + string.Join(", ", tags) + "]";
        var idx = lines.FindIndex(1, close - 1, l => l.StartsWith("tags:"));
        if (idx < 0)
        {
            lines.Insert(close, rendered);
            return string.Join(nl, lines);
        }

        // Replace the tags line itself, then drop any following block-list items ("  - x")
        // that belonged to it (only relevant when the source used the multi-line form).
        lines[idx] = rendered;
        var next = idx + 1;
        while (next < close && lines[next].TrimStart().StartsWith("- "))
        {
            lines.RemoveAt(next);
            close--;
        }
        return string.Join(nl, lines);
    }
}
