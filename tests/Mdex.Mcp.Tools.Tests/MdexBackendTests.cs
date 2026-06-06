using System.Text.Json;
using Mdex.Core.Cli;
using Mdex.Core.Security;
namespace Mdex.Mcp.Tools.Tests;

public class MdexBackendTests
{
    static MdexBackend Backend(string root, List<string>? log = null) =>
        new(CommandEngine.Default(), root, msg => log?.Add(msg));

    [Fact]
    public void Call_returns_the_data_object_not_a_json_string()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n\nHello world\n");
        var data = Backend(v.Root).Call("read-note", "--", "a");
        Assert.NotNull(data);
        Assert.IsNotType<string>(data);                       // it's the POCO/anon object, not an envelope string
        Assert.Contains("Hello world", JsonSerializer.Serialize(data));
    }

    [Fact]
    public void Call_maps_a_user_error_to_MdexToolException_with_message()
    {
        using var v = new TestVault();
        var ex = Assert.Throws<MdexToolException>(() => Backend(v.Root).Call("read-note", "--", "missing"));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void Call_unknown_verb_is_a_user_error()
    {
        using var v = new TestVault();
        Assert.Throws<MdexToolException>(() => Backend(v.Root).Call("nope"));
    }

    [Fact]
    public void Call_unexpected_exception_is_sanitised_and_logged_not_leaked()
    {
        // Put a FILE where write-file needs a directory → an IOException (not a CommandException)
        // must be swallowed into a generic "internal error" and logged server-side.
        using var v = new TestVault();
        v.WriteNote("blocker", "x");
        var log = new List<string>();
        var ex = Assert.Throws<MdexToolException>(
            () => Backend(v.Root, log).Call("write-file", "--content=hi", "--", "blocker/child"));
        Assert.Equal("internal error", ex.Message);
        Assert.DoesNotContain("blocker", ex.Message);         // no path leaked to the client
        Assert.NotEmpty(log);                                  // the detail went to the server log instead
    }
}
