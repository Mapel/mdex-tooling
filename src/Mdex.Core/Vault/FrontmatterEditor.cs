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
}
