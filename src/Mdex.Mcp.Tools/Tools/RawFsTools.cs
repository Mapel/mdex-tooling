using System.ComponentModel;
using Mdex.Core.Security;
using ModelContextProtocol.Server;

namespace Mdex.Mcp.Tools.Tools;

[McpServerToolType]
public static class RawFsTools
{
    [McpServerTool, MinRole(Role.FullWrite), Description("Create or overwrite a file at a vault-relative path.")]
    public static object? WriteFile(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path,
        [Description("Full file content")] string content)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("write-file", $"--content={content}", "--", path);
    }

    [McpServerTool, MinRole(Role.FullWrite), Description("Append raw text to a file at a vault-relative path (creates it if missing).")]
    public static object? AppendFile(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path,
        [Description("Text to append")] string content)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("append-file", $"--content={content}", "--", path);
    }

    [McpServerTool, MinRole(Role.FullWrite), Description("Move or rename a file/directory within the vault (no overwrite).")]
    public static object? Move(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Source vault-relative path")] string from,
        [Description("Destination vault-relative path")] string to)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("move", "--", from, to);
    }

    [McpServerTool, MinRole(Role.FullWrite), Description("Create a directory at a vault-relative path.")]
    public static object? Mkdir(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("mkdir", "--", path);
    }

    [McpServerTool, MinRole(Role.FullWrite), Description("Delete a file or directory (recursive); revertible via git history.")]
    public static object? Delete(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("delete", "--", path);
    }
}
