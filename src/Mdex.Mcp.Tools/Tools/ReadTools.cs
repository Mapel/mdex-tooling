using System.ComponentModel;
using Mdex.Core.Security;
using ModelContextProtocol.Server;

namespace Mdex.Mcp.Tools.Tools;

[McpServerToolType]
public static class ReadTools
{
    [McpServerTool, MinRole(Role.Read), Description("Full-text substring search over note titles and bodies; one hit per note. Whitespace tokenises into terms (--match all=default | any).")]
    public static object? Search(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Search query (whitespace splits into terms)")] string query,
        [Description("Term match mode: all (default) or any")] string? match = null)
    {
        auth.Require(Role.Read);
        var args = new List<string>();
        if (match is not null) args.Add($"--match={match}");
        args.Add("--");
        args.Add(query);
        return mdex.Call("search", args.ToArray());
    }

    [McpServerTool, MinRole(Role.Read), Description("List available body templates (name + content). Call before passing bodyTemplate to create, to learn the valid names and follow a note-type's convention.")]
    public static object? ListTemplates(MdexBackend mdex, ToolAuthorizer auth)
    {
        auth.Require(Role.Read);
        return mdex.Call("list-templates");
    }

    [McpServerTool, MinRole(Role.Read), Description("Return one or more notes by slug or vault-relative path. Partial success: missing targets are listed under errors.")]
    public static object? ReadNote(MdexBackend mdex, ToolAuthorizer auth,
        [Description("One or more note slugs or vault-relative paths")] string[] targets)
    {
        auth.Require(Role.Read);
        var args = new List<string> { "--" };
        args.AddRange(targets);
        return mdex.Call("read-note", args.ToArray());
    }

    [McpServerTool, MinRole(Role.Read), Description("Find notes by tag, type, and/or date range. Different filters AND; --match governs tag matching (all=default | any).")]
    public static object? FindNotes(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Filter by tags (combined per --match)")] string[]? tags = null,
        [Description("Filter by type: inbox | zettel | project | area | reference")] string? type = null,
        [Description("Start date YYYY-MM-DD (inclusive), or null for open start")] string? from = null,
        [Description("End date YYYY-MM-DD (inclusive), or null for open end")] string? to = null,
        [Description("Date field: created (default) or modified")] string? field = null,
        [Description("Tag match mode: all (default) or any")] string? match = null)
    {
        auth.Require(Role.Read);
        var args = new List<string>();
        foreach (var t in tags ?? Array.Empty<string>()) { args.Add("--tag"); args.Add(t); }
        if (type is not null) args.Add($"--type={type}");
        if (from is not null) args.Add($"--from={from}");
        if (to is not null) args.Add($"--to={to}");
        if (field is not null) args.Add($"--field={field}");
        if (match is not null) args.Add($"--match={match}");
        return mdex.Call("find-notes", args.ToArray());
    }

    [McpServerTool, MinRole(Role.Read), Description("File/dir tree under an optional vault-relative subpath. Default depth 1 (direct children only).")]
    public static object? List(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative subpath, or null for the vault root")] string? path = null,
        [Description("Depth: a positive integer or 'all' for full recursion. Default 1")] string? depth = null)
    {
        auth.Require(Role.Read);
        var args = new List<string>();
        if (depth is not null) args.Add($"--depth={depth}");
        if (path is not null) { args.Add("--"); args.Add(path); }
        return mdex.Call("list", args.ToArray());
    }

    [McpServerTool, MinRole(Role.Read), Description("All tags with their counts, sorted by frequency. Optional name-pattern filter and top-N cap.")]
    public static object? ListTags(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Filter tag names containing this substring (case-insensitive)")] string? pattern = null,
        [Description("Limit to the top N tags by count")] int? top = null)
    {
        auth.Require(Role.Read);
        var args = new List<string>();
        if (top is not null) args.Add($"--top={top.Value}");
        if (pattern is not null) { args.Add("--"); args.Add(pattern); }
        return mdex.Call("list-tags", args.ToArray());
    }

    [McpServerTool, MinRole(Role.Read), Description("Notes linked via [[wikilinks]]. Default: depth 1, both directions. Backlinks = direction 'in', depth 1.")]
    public static object? FindRelated(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath,
        [Description("Depth: a positive integer or 'all' for the full connected component. Default 1")] string? depth = null,
        [Description("Link direction: in (backlinks) | out (forward) | both (default)")] string? direction = null)
    {
        auth.Require(Role.Read);
        var args = new List<string>();
        if (depth is not null) args.Add($"--depth={depth}");
        if (direction is not null) args.Add($"--direction={direction}");
        args.Add("--");
        args.Add(slugOrPath);
        return mdex.Call("find-related", args.ToArray());
    }

    [McpServerTool, MinRole(Role.Read), Description("Collect checkboxes (- [ ] / - [x]) across all notes. Default: open items only.")]
    public static object? ListTodos(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Status filter: open (default), done, or all")] string? status = null,
        [Description("Vault-relative path prefix to restrict the search, or null for the whole vault")] string? scope = null)
    {
        auth.Require(Role.Read);
        var args = new List<string>();
        if (status is not null) args.Add($"--status={status}");
        if (scope is not null) args.Add($"--in={scope}");
        return mdex.Call("list-todos", args.ToArray());
    }
}
