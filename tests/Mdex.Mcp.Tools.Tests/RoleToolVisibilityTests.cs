using Mdex.Core.Security;
using ModelContextProtocol.Protocol;
using Role = Mdex.Core.Security.Role;
namespace Mdex.Mcp.Tools.Tests;

public class RoleToolVisibilityTests
{
    // The lookup is built from the tools assembly using the SDK's own name derivation, so its keys
    // are the exact protocol names a client sees in tools/list (PascalCase method → snake_case).
    [Fact]
    public void Lookup_covers_every_tool_with_its_declared_minimum_role()
    {
        var map = RoleToolVisibility.MinRoleByName;

        Assert.Equal(21, map.Count);                       // 8 read + 11 intent + 2 raw-fs
        Assert.Equal(Role.Read, map["search"]);
        Assert.Equal(Role.Read, map["list_templates"]);
        Assert.Equal(Role.Read, map["list_tags"]);
        Assert.Equal(Role.Read, map["find_notes"]);
        Assert.Equal(Role.Read, map["list_todos"]);
        Assert.Equal(Role.WriteIntent, map["capture_inbox"]);
        Assert.Equal(Role.WriteIntent, map["append_to_note"]);
        Assert.Equal(Role.WriteIntent, map["move"]);        // pulled up from fullwrite
        Assert.Equal(Role.WriteIntent, map["mkdir"]);       // pulled up from fullwrite
        Assert.Equal(Role.WriteIntent, map["set_tags"]);
        Assert.Equal(Role.WriteIntent, map["set_todo"]);
        Assert.Equal(Role.WriteIntent, map["commit_and_push"]);
        Assert.Equal(Role.FullWrite, map["write_file"]);
        Assert.Equal(Role.FullWrite, map["delete"]);
    }

    static List<Tool> AllTools() =>
        RoleToolVisibility.MinRoleByName.Keys.Select(n => new Tool { Name = n }).ToList();

    static string[] NamesAfter(Role role)
    {
        var tools = AllTools();
        RoleToolVisibility.Apply(tools, role);
        return tools.Select(t => t.Name).OrderBy(n => n).ToArray();
    }

    [Fact]
    public void Read_role_sees_only_the_eight_read_tools()
    {
        var names = NamesAfter(Role.Read);
        Assert.Equal(8, names.Length);
        Assert.Contains("search", names);
        Assert.DoesNotContain("capture_inbox", names);   // write-intent hidden
        Assert.DoesNotContain("write_file", names);      // fullwrite hidden
    }

    [Fact]
    public void WriteIntent_role_sees_read_and_intent_but_not_raw_fs()
    {
        var names = NamesAfter(Role.WriteIntent);
        Assert.Equal(19, names.Length);                  // 8 read + 11 intent
        Assert.Contains("capture_inbox", names);
        Assert.Contains("move", names);                  // pulled up from fullwrite
        Assert.DoesNotContain("delete", names);          // fullwrite hidden
    }

    [Fact]
    public void FullWrite_role_sees_every_tool()
    {
        Assert.Equal(21, NamesAfter(Role.FullWrite).Length);
    }

    [Fact]
    public void Unknown_tool_names_are_removed_fail_closed()
    {
        var tools = new List<Tool> { new() { Name = "search" }, new() { Name = "not_a_real_tool" } };
        RoleToolVisibility.Apply(tools, Role.FullWrite);
        Assert.Equal(new[] { "search" }, tools.Select(t => t.Name).ToArray());
    }
}
