using System.Text.Json;
using Mdex.Core.Cli;
using Mdex.Core.Security;
using Mdex.Mcp.Tools.Tools;
namespace Mdex.Mcp.Tools.Tests;

public class ToolEndToEndTests
{
    static MdexBackend Backend(string root) => new(CommandEngine.Default(), root, _ => { });
    static ToolAuthorizer Auth(Role role) => new(new FixedRoleProvider(role));

    [Fact]
    public void Read_tool_returns_data_for_a_real_note()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n\nHello world\n");
        var data = ReadTools.ReadNote(Backend(v.Root), Auth(Role.Read), new[] { "a" });
        Assert.Contains("Hello world", JsonSerializer.Serialize(data));
    }

    [Fact]
    public void Read_tool_cannot_escape_the_vault()   // containment through the tool layer
    {
        using var v = new TestVault();
        // A read-role caller asks for a path that climbs out of the vault. The jail refuses it:
        // the tool throws (it does NOT return the outside file), and the sanitised message leaks
        // neither the server's absolute vault path nor the target file's contents. (The caller's
        // own input string may appear in a "not found" message — that is an echo, not a leak.)
        var ex = Assert.Throws<MdexToolException>(
            () => ReadTools.ReadNote(Backend(v.Root), Auth(Role.Read), new[] { "../../../etc/passwd" }));
        Assert.DoesNotContain(v.Root, ex.Message);    // no server-side absolute path leaked (D-4)
        Assert.DoesNotContain("root:", ex.Message);   // proves /etc/passwd was never actually read
    }

    [Fact]
    public void Read_role_is_refused_the_write_tools()   // ladder enforced at the tool
    {
        using var v = new TestVault();
        var backend = Backend(v.Root);
        var read = Auth(Role.Read);
        Assert.Throws<MdexToolException>(() => IntentTools.CaptureInbox(backend, read, "title", "hello"));
        Assert.Throws<MdexToolException>(() => RawFsTools.Delete(backend, read, "zettel/a.md"));
    }

    [Fact]
    public void WriteIntent_role_is_refused_the_raw_fs_tools()
    {
        using var v = new TestVault();
        Assert.Throws<MdexToolException>(
            () => RawFsTools.Delete(Backend(v.Root), Auth(Role.WriteIntent), "zettel/a.md"));
    }

    [Fact]
    public void Intent_tool_runs_for_write_intent_role()
    {
        using var v = new TestVault();
        var data = IntentTools.CaptureInbox(Backend(v.Root), Auth(Role.WriteIntent), "a thought", "a captured thought");
        Assert.NotNull(data);                          // succeeds (no throw) and returns the Data object
    }
}
