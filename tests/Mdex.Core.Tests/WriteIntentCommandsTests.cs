using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Commands.Write;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class WriteIntentCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    // ---- move -------------------------------------------------------------

    [Fact]
    public void Move_renames_within_folder_and_bumps_modified()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/old.md", "---\ntype: zettel\nmodified: 2026-01-01\n---\n# Old\n");
        new MoveCommand().Run(new[] { "zettel/old.md", "zettel/new.md" }, Ctx(v));
        Assert.False(File.Exists(Path.Combine(v.Root, "zettel", "old.md")));
        var text = v.Read("zettel/new.md");
        Assert.Contains("type: zettel", text);
        Assert.DoesNotContain("modified: 2026-01-01", text);
    }

    [Fact]
    public void Move_rederives_type_from_destination_folder()
    {
        using var v = new TestVault();
        v.WriteNote("inbox/20260601-idee.md", "---\ntype: inbox\n---\n# Idee\n");
        new MoveCommand().Run(new[] { "inbox/20260601-idee.md", "zettel/idee.md" }, Ctx(v));
        Assert.Contains("type: zettel", v.Read("zettel/idee.md"));
    }

    [Fact]
    public void Move_to_archive_keeps_existing_type()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        new MoveCommand().Run(new[] { "zettel/a.md", "resources/archive/a.md" }, Ctx(v));
        Assert.Contains("type: zettel", v.Read("resources/archive/a.md"));   // archive keeps type
    }

    [Fact]
    public void Move_refuses_to_overwrite()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\n---\n# B\n");
        Assert.Throws<CommandException>(() =>
            new MoveCommand().Run(new[] { "zettel/a.md", "zettel/b.md" }, Ctx(v)));
    }

    // ---- replace-body -----------------------------------------------------

    [Fact]
    public void ReplaceBody_swaps_body_keeps_frontmatter()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ncreated: 2026-01-01T00:00:00\nmodified: 2026-01-01\n---\n\n# Old\n\nold body\n");
        new ReplaceBodyCommand().Run(new[] { "a", "--content", "# New\n\nfresh" }, Ctx(v));
        var text = v.Read("zettel/a.md");
        Assert.Contains("created: 2026-01-01T00:00:00", text);   // frontmatter preserved
        Assert.DoesNotContain("old body", text);
        Assert.Contains("fresh", text);
        Assert.DoesNotContain("modified: 2026-01-01", text);     // bumped
    }

    // ---- set-tags ---------------------------------------------------------

    [Fact]
    public void SetTags_add_unions_with_existing()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x]\n---\n# A\n");
        var tags = new SetTagsCommand().Run(new[] { "a", "--add", "--", "y", "x" }, Ctx(v)).Data()
            .GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(new[] { "x", "y" }, tags);   // x not duplicated
    }

    [Fact]
    public void SetTags_remove_subtracts()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x, y]\n---\n# A\n");
        var tags = new SetTagsCommand().Run(new[] { "a", "--remove", "--", "x" }, Ctx(v)).Data()
            .GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(new[] { "y" }, tags);
    }

    [Fact]
    public void SetTags_set_replaces_all()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x, y]\n---\n# A\n");
        var tags = new SetTagsCommand().Run(new[] { "a", "--set", "--", "z" }, Ctx(v)).Data()
            .GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(new[] { "z" }, tags);
    }

    [Fact]
    public void SetTags_requires_exactly_one_mode()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x]\n---\n# A\n");
        Assert.Throws<CommandException>(() => new SetTagsCommand().Run(new[] { "a", "--", "y" }, Ctx(v)));
        Assert.Throws<CommandException>(() =>
            new SetTagsCommand().Run(new[] { "a", "--add", "--set", "--", "y" }, Ctx(v)));
    }

    // ---- set-todo + list-todos -------------------------------------------

    [Fact]
    public void SetTodo_toggles_a_matched_checkbox()
    {
        using var v = new TestVault();
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n\n- [ ] write the docs\n- [ ] ship it\n");
        new SetTodoCommand().Run(new[] { "projects/p/index.md", "--match", "docs", "--status", "done" }, Ctx(v));
        var text = v.Read("projects/p/index.md");
        Assert.Contains("- [x] write the docs", text);
        Assert.Contains("- [ ] ship it", text);
    }

    [Fact]
    public void SetTodo_ambiguous_match_throws_with_candidates()
    {
        using var v = new TestVault();
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n\n- [ ] review draft\n- [ ] review final\n");
        var ex = Assert.Throws<CommandException>(() =>
            new SetTodoCommand().Run(new[] { "projects/p/index.md", "--match", "review", "--status", "done" }, Ctx(v)));
        Assert.Contains("matches 2", ex.Message);
    }

    [Fact]
    public void SetTodo_no_match_throws()
    {
        using var v = new TestVault();
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n\n- [ ] something\n");
        Assert.Throws<CommandException>(() =>
            new SetTodoCommand().Run(new[] { "projects/p/index.md", "--match", "absent", "--status", "done" }, Ctx(v)));
    }

    [Fact]
    public void ListTodos_collects_open_by_default()
    {
        using var v = new TestVault();
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n\n- [ ] open one\n- [x] done one\n");
        var data = new ListTodosCommand().Run(Array.Empty<string>(), Ctx(v)).Data();
        Assert.Equal(1, data.GetProperty("count").GetInt32());
        Assert.Equal("open one", data.GetProperty("todos")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void ListTodos_all_and_scope()
    {
        using var v = new TestVault();
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n\n- [ ] a\n- [x] b\n");
        v.WriteNote("zettel/z.md", "---\ntype: zettel\n---\n# Z\n\n- [ ] c\n");
        Assert.Equal(3, new ListTodosCommand().Run(new[] { "--status", "all" }, Ctx(v)).Data().GetProperty("count").GetInt32());
        Assert.Equal(2, new ListTodosCommand().Run(new[] { "--status", "all", "--in", "projects" }, Ctx(v)).Data().GetProperty("count").GetInt32());
    }
}
