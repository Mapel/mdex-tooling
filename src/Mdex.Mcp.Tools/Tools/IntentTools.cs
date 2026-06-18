using System.ComponentModel;
using Mdex.Core.Security;
using ModelContextProtocol.Server;

namespace Mdex.Mcp.Tools.Tools;

[McpServerToolType]
public static class IntentTools
{
    [McpServerTool, MinRole(Role.WriteIntent), Description("Create a new inbox entry. Title is required (sets the H1 and derives inbox/YYYYMMDD-<slug>.md).")]
    public static object? CaptureInbox(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Title for the entry (required)")] string title,
        [Description("Body text of the entry")] string content)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("capture-inbox", $"--title={title}", $"--content={content}");
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Create a typed note (zettel|project|area|reference) with frontmatter + template.")]
    public static object? Create(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Type: zettel | project | area | reference")] string type,
        [Description("Note name (may include a subfolder for non-zettel types)")] string name,
        [Description("Initial tags")] string[]? tags = null,
        [Description("Optional body skeleton: a template name from list_templates (e.g. area, project-default). Omit for an empty body — the usual choice, since you normally write the body via replace_body/append_to_note.")] string? bodyTemplate = null)
    {
        auth.Require(Role.WriteIntent);
        var args = new List<string>();
        if (bodyTemplate is not null) args.Add($"--body-template={bodyTemplate}");
        foreach (var t in tags ?? Array.Empty<string>()) { args.Add("--tag"); args.Add(t); }
        args.Add("--");
        args.Add(type);
        args.Add(name);
        return mdex.Call("create", args.ToArray());
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Append a section to a note (by slug or path) and bump its modified date. Optionally add tags.")]
    public static object? AppendToNote(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath,
        [Description("Markdown to append")] string content,
        [Description("Tags to add (merged with existing)")] string[]? tags = null)
    {
        auth.Require(Role.WriteIntent);
        var args = new List<string> { $"--content={content}" };
        foreach (var t in tags ?? Array.Empty<string>()) { args.Add("--tag"); args.Add(t); }
        args.Add("--");
        args.Add(slugOrPath);
        return mdex.Call("append-to-note", args.ToArray());
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Add a dated bullet under a note's ## Log section (creating it if needed).")]
    public static object? AppendToLog(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Project note slug or vault-relative path")] string slugOrPath,
        [Description("Log entry text (becomes a bullet)")] string content)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("append-to-log", $"--content={content}", "--", slugOrPath);
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Move an inbox item into a typed folder, repairing missing/incomplete frontmatter.")]
    public static object? PromoteInboxItem(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Inbox item slug or path")] string inbox,
        [Description("Target type: zettel | project | area | reference")] string type,
        [Description("Optional new name, or null to keep the inbox slug")] string? name = null)
    {
        auth.Require(Role.WriteIntent);
        var args = new List<string>();
        if (name is not null) args.Add($"--name={name}");
        args.Add("--");
        args.Add(inbox);
        args.Add(type);
        return mdex.Call("promote-inbox-item", args.ToArray());
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Move or rename a file/directory within the vault. Re-derives type from the destination folder and bumps modified. Covers rename, move-to-archive, and general relocation.")]
    public static object? Move(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Source vault-relative path")] string from,
        [Description("Destination vault-relative path")] string to)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("move", "--", from, to);
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Replace a note's body while preserving its frontmatter (and bumping modified).")]
    public static object? ReplaceBody(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath,
        [Description("New body content")] string content)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("replace-body", $"--content={content}", "--", slugOrPath);
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Mutate a note's tags — the only KI path to frontmatter. Exactly one of add (union), remove (subtract), or set (replace all).")]
    public static object? SetTags(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath,
        [Description("Tags to add (union with existing)")] string[]? add = null,
        [Description("Tags to remove from existing")] string[]? remove = null,
        [Description("Replace all tags with this exact list")] string[]? set = null)
    {
        auth.Require(Role.WriteIntent);
        var args = new List<string>();
        string[]? values;
        if (add is not null) { args.Add("--add"); values = add; }
        else if (remove is not null) { args.Add("--remove"); values = remove; }
        else if (set is not null) { args.Add("--set"); values = set; }
        else throw new MdexToolException("set-tags needs one of add, remove, or set");
        args.Add("--");
        args.Add(slugOrPath);
        args.AddRange(values);
        return mdex.Call("set-tags", args.ToArray());
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Create a directory at a vault-relative path (deliberate empty-folder scaffolding).")]
    public static object? Mkdir(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("mkdir", "--", path);
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Set a todo checkbox's status by text-match. Fails on 0 or 2+ matches (the human disambiguates).")]
    public static object? SetTodo(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath,
        [Description("Text to match against the todo item (case-insensitive substring)")] string match,
        [Description("New status: open or done")] string status)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("set-todo", $"--match={match}", $"--status={status}", "--", slugOrPath);
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Atomically fetch/pull, commit all changes, and push. Use after a logical unit of work (semantic message, no auto: prefix).")]
    public static object? CommitAndPush(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Commit message")] string message)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("commit-and-push", $"--message={message}");
    }
}
