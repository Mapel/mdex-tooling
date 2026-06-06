using System.ComponentModel;
using Mdex.Core.Security;
using ModelContextProtocol.Server;

namespace Mdex.Mcp.Tools.Tools;

[McpServerToolType]
public static class ReadTools
{
    [McpServerTool, MinRole(Role.Read), Description("Full-text substring search over note titles and bodies; one hit per note.")]
    public static object? Search(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Search query")] string query)
    {
        auth.Require(Role.Read);
        return mdex.Call("search", "--", query);
    }

    [McpServerTool, MinRole(Role.Read), Description("Return a note's frontmatter and body by slug or vault-relative path.")]
    public static object? ReadNote(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath)
    {
        auth.Require(Role.Read);
        return mdex.Call("read-note", "--", slugOrPath);
    }

    [McpServerTool, MinRole(Role.Read), Description("Notes carrying all of the given tags.")]
    public static object? GetByTag(MdexBackend mdex, ToolAuthorizer auth,
        [Description("One or more tag names")] string[] tags)
    {
        auth.Require(Role.Read);
        var args = new List<string> { "--" };
        args.AddRange(tags);
        return mdex.Call("get-by-tag", args.ToArray());
    }

    [McpServerTool, MinRole(Role.Read), Description("Notes whose frontmatter type matches (inbox|zettel|project|area|reference).")]
    public static object? GetByType(MdexBackend mdex, ToolAuthorizer auth,
        [Description("BRAID type")] string type)
    {
        auth.Require(Role.Read);
        return mdex.Call("get-by-type", "--", type);
    }

    [McpServerTool, MinRole(Role.Read), Description("Notes in a created/modified date range. One bound may be omitted (open-ended).")]
    public static object? GetByDateRange(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Start date YYYY-MM-DD, or null for open start")] string? from,
        [Description("End date YYYY-MM-DD, or null for open end")] string? to,
        [Description("Which date field: created or modified")] string? field)
    {
        auth.Require(Role.Read);
        var args = new List<string>();
        if (from is not null) args.Add($"--from={from}");
        if (to is not null) args.Add($"--to={to}");
        if (field is not null) args.Add($"--field={field}");
        return mdex.Call("get-by-date-range", args.ToArray());
    }

    [McpServerTool, MinRole(Role.Read), Description("All tags with their counts.")]
    public static object? ListTags(MdexBackend mdex, ToolAuthorizer auth)
    {
        auth.Require(Role.Read);
        return mdex.Call("list-tags");
    }

    [McpServerTool, MinRole(Role.Read), Description("Tags containing the given substring.")]
    public static object? SearchTags(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Substring to match against tag names")] string pattern)
    {
        auth.Require(Role.Read);
        return mdex.Call("search-tags", "--", pattern);
    }

    [McpServerTool, MinRole(Role.Read), Description("Recursive file/dir tree under an optional vault-relative subpath.")]
    public static object? List(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative subpath, or null for the whole vault")] string? path)
    {
        auth.Require(Role.Read);
        return path is null ? mdex.Call("list") : mdex.Call("list", "--", path);
    }

    [McpServerTool, MinRole(Role.Read), Description("Notes linked via [[wikilinks]] in both directions; depth 1 by default.")]
    public static object? FindRelated(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath,
        [Description("Max hops to walk; null for 1 (or unlimited with recursive)")] int? maxHops,
        [Description("Walk the whole connected component")] bool recursive = false)
    {
        auth.Require(Role.Read);
        var args = new List<string>();
        if (maxHops is not null) args.Add($"--maxhops={maxHops.Value}");
        if (recursive) args.Add("--recursive");
        args.Add("--");
        args.Add(slugOrPath);
        return mdex.Call("find-related", args.ToArray());
    }
}
