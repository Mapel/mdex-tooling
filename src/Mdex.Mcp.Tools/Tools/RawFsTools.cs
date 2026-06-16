using System.ComponentModel;
using Mdex.Core.Security;
using ModelContextProtocol.Server;

namespace Mdex.Mcp.Tools.Tools;

[McpServerToolType]
public static class RawFsTools
{
    [McpServerTool, MinRole(Role.FullWrite), Description("Create or overwrite a file at a vault-relative path. Convention escape hatch — ignores frontmatter/templates.")]
    public static object? WriteFile(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path,
        [Description("Full file content")] string content)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("write-file", $"--content={content}", "--", path);
    }

    [McpServerTool, MinRole(Role.FullWrite), Description("Delete a file or directory (recursive); the only pre-commit irreversible operation.")]
    public static object? Delete(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("delete", "--", path);
    }
}
