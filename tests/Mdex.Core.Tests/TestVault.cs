using System.Text;

namespace Mdex.Core.Tests;

/// <summary>Disposable temp vault: BRAID folders + the real template files.</summary>
public sealed class TestVault : IDisposable
{
    public string Root { get; }

    public TestVault()
    {
        Root = Path.Combine(Path.GetTempPath(), "mdex-test-" + Guid.NewGuid().ToString("N"));
        foreach (var d in new[] { "inbox", "zettel", "projects", "areas",
                                  "resources/references", "resources/templates" })
            Directory.CreateDirectory(Path.Combine(Root, d.Replace('/', Path.DirectorySeparatorChar)));

        WriteTemplate("frontmatter-inbox.md",
            "---\ntype: inbox\ncreated: {{created}}\nmodified: {{modified}}\nbacklinks: []\n---\n");
        WriteTemplate("frontmatter-zettel.md",
            "---\ntype: zettel\ncreated: {{created}}\nmodified: {{modified}}\ntags: []\nbacklinks: []\nsource:\n---\n");
        WriteTemplate("frontmatter-project.md",
            "---\ntype: project\ncreated: {{created}}\nmodified: {{modified}}\ntags: []\nbacklinks: []\n---\n");
        WriteTemplate("frontmatter-area.md",
            "---\ntype: area\ncreated: {{created}}\nmodified: {{modified}}\ntags: []\nbacklinks: []\n---\n");
        WriteTemplate("frontmatter-reference.md",
            "---\ntype: reference\ncreated: {{created}}\nmodified: {{modified}}\nauthor:\nsource:\nbacklinks: []\n---\n");
        WriteTemplate("body-project-default.md",
            "## Ziel\n\n## Kontext\n\n## Tasks\n- [ ]\n\n## Log\n### YYYY-MM-DD\n-\n\n---\nVerbundene Zettel:\n-\n");
    }

    void WriteTemplate(string name, string content) =>
        File.WriteAllText(Path.Combine(Root, "resources", "templates", name), content, new UTF8Encoding(false));

    /// <summary>Write a note with raw text at a vault-relative path. Returns the absolute path.</summary>
    public string WriteNote(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    public string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}
