using System.ComponentModel;
using Mdex.Core.Security;
using ModelContextProtocol.Server;

namespace Mdex.Mcp.Tools.Tools;

[McpServerToolType]
public static class IntentTools
{
    [McpServerTool, MinRole(Role.WriteIntent), Description("Append a new timestamped inbox entry (inbox/YYYYMMDD-NN.md).")]
    public static object? CaptureInbox(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Entry text")] string content)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("capture-inbox", $"--content={content}");
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Create a typed note (zettel|project|area|reference) with frontmatter + template.")]
    public static object? CreateNote(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Type: zettel | project | area | reference")] string type,
        [Description("Note name (may include a subfolder for non-zettel types)")] string name,
        [Description("Omit the body template")] bool noBody = false,
        [Description("Named body template override, or null for the default")] string? body = null)
    {
        auth.Require(Role.WriteIntent);
        var args = new List<string>();
        if (noBody) args.Add("--no-body");
        if (body is not null) args.Add($"--body={body}");
        args.Add("--");
        args.Add(type);
        args.Add(name);
        return mdex.Call("create", args.ToArray());
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Append a section to a note (by slug or path) and bump its modified date.")]
    public static object? AppendToNote(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath,
        [Description("Markdown to append")] string content)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("append-to-note", $"--content={content}", "--", slugOrPath);
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Add a dated bullet under a note's ## Log section (creating it if needed).")]
    public static object? AppendToLog(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Project note slug or vault-relative path")] string slugOrPath,
        [Description("Log entry text (becomes a bullet)")] string content)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("append-to-log", $"--content={content}", "--", slugOrPath);
    }

    [McpServerTool, MinRole(Role.WriteIntent), Description("Move an inbox item into a typed folder, rebuilding its frontmatter.")]
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
}
