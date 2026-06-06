// tests/Mdex.Core.Tests/NoteContainmentTests.cs
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class NoteContainmentTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "mdex-contain-" + Guid.NewGuid().ToString("N"));
    readonly string _root;

    public NoteContainmentTests()
    {
        _root = Path.Combine(_tmp, "vault");
        Directory.CreateDirectory(Path.Combine(_root, "inbox"));
        File.WriteAllText(Path.Combine(_root, "inbox", "note.md"), "---\ntype: inbox\n---\n# hi\n");
        File.WriteAllText(Path.Combine(_tmp, "secret.md"), "---\ntype: secret\n---\n# TOP SECRET\n"); // OUTSIDE the vault
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    NoteRepository Repo() => new(_root, new PathGuard(_root));

    [Fact]
    public void Relative_traversal_finds_nothing()
    {
        Assert.Empty(Repo().FindBySlugOrPath("../secret"));
    }

    [Fact]
    public void Absolute_path_outside_vault_finds_nothing()
    {
        Assert.Empty(Repo().FindBySlugOrPath(Path.Combine(_tmp, "secret.md")));
    }

    [Fact]
    public void Legit_inside_path_still_resolves()
    {
        var hits = Repo().FindBySlugOrPath("inbox/note");
        Assert.Single(hits);
        Assert.Equal("inbox/note.md", hits[0].RelativePath);
    }
}
