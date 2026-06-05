# Plan B — `Mdex.Mcp.Tools` + `mdex-mcp` (local stdio MCP host) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the shared MCP tool library `Mdex.Mcp.Tools` (the 19 `[McpServerTool]` wrappers over `Mdex.Core`, each role-gated by `[MinRole]`) and the local stdio host `mdex-mcp`, so Claude Code / Cowork / Codex on the dev machine get a guarded, role-staged MCP surface over the **local checkout** — no network, no token, no ASP.NET.

**Architecture:** Tools call `Mdex.Core`'s `CommandEngine` **in-process** through a thin `MdexBackend` and return the operation's **`Data` object** (the MCP SDK serialises it — no JSON-envelope reparse; design §3). The role ladder (`read` ⊂ `write-intent` ⊂ `fullwrite`) and its authorizer live in `Mdex.Core` (ASP.NET-/SDK-free), fed by an `IRoleProvider`: locally a `FixedRoleProvider` set from a fail-closed `--role` launch flag (no default); remotely (Plan C) an HTTP-claim provider. `mdex-mcp` is a Generic-Host console packaged as a .NET global tool — it references **only** `Mdex.Mcp.Tools` (+ the core MCP SDK), never ASP.NET or `Mdex.Tokens`.

**Tech Stack:** .NET 10 · C# · xUnit · `ModelContextProtocol` (core MCP C# SDK, stdio transport) · `Microsoft.Extensions.Hosting`. No ASP.NET, no crypto. Spec: [2026-06-05-mdex-mcp-local-remote-design.md](2026-06-05-mdex-mcp-local-remote-design.md) §§1–5, §8.

> **Plan B of 3** for the local+remote MCP build. **Plan A** ([2026-06-05-mdex-core-extraction-plan.md](2026-06-05-mdex-core-extraction-plan.md)) is the prerequisite: it produced `Mdex.Core` with `CommandEngine` (`Run(verb, args, rootFlag) → CommandResult`, no `json` flag) and the renamed `mdex-cli`. **Plan C** ([written separately]) adds `Mdex.Tokens` + `mdex-token` + `mdex-gateway` and reuses the `Role` type and `ToolAuthorizer`/`IRoleProvider` seam introduced here.

> **Prerequisite check (run first):** Plan A has landed — `dotnet build "$REPO\Mdex.sln"` and `dotnet test "$REPO\Mdex.sln"` are green, `Mdex.Core` exists with `CommandEngine.Default()`, and the test project is `Mdex.Core.Tests`.

> **Security gates (Phase-2-Delta, see [security plan](2026-06-05-mdex-security-review-plan.md) §"Security-Gates …pro Phase"):**
> - **Plan-phase 1 (`Mdex.Mcp.Tools`):** every `[McpServerTool]` carries `[MinRole]` and takes a `ToolAuthorizer`, enforced by a CI reflection guard against the shared lib (D-2, N-3) — **Task 6**.
> - **Plan-phase 2 (`mdex-mcp` local):** `--role` is fail-closed (no default; host refuses to start without it) — **Task 8**; an end-to-end path-traversal test runs *through the tools* and a `read`-role caller is refused the write tools — **Task 7**.
> - **D-4 (no stack/abs-path leak to the AI client)** is implemented here in `MdexBackend.Call` (the tool layer the design moved it to, §3/§8) — **Task 2** — and is inherited by the gateway in Plan C.

---

## Conventions for every task

- Set `$REPO` at the **start of every shell call** — PowerShell variables do not persist between separate tool calls: `$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"`.
- Build: `dotnet build "$REPO\Mdex.sln"`. Test: `dotnet test "$REPO\Mdex.sln"` (add `--filter <Name>` to scope).
- Commit after each task with the message shown (`git -C $REPO commit -am "…"`); for tasks that add files use `git -C $REPO add -A` first.
- Do **not** use `cd` in compound commands (can trigger a permission prompt).

> **MCP C# SDK soft-spots (knowledge cutoff Jan 2026 — verify against the installed package):** the package is `ModelContextProtocol` (prerelease). The exact spelling of `WithStdioServerTransport()`, the `WithToolsFromAssembly(Assembly)` overload, and tool methods returning `object?` may differ in newer versions. Each is flagged inline with a fallback. Pin the version that restores; do not guess a version number.

---

## File Structure (end state of this plan)

```
src/
  Mdex.Core/
    Security/                         NEW — role vocabulary + authorizer (ASP.NET-/SDK-free)
      Role.cs                         Role enum (Read<WriteIntent<FullWrite) + Roles wire helper
      IRoleProvider.cs                IRoleProvider + FixedRoleProvider
      ToolAuthorizer.cs               Require(minimum) over IRoleProvider.Current
      MinRoleAttribute.cs             declares a tool's minimum role (guard-enforced)
      MdexToolException.cs            user-facing tool error (message + optional details)
  Mdex.Mcp.Tools/                     NEW class library — the ONE tool surface (refs Core + MCP SDK)
    Mdex.Mcp.Tools.csproj
    MdexBackend.cs                    in-process adapter: CommandEngine.Run → Data; sanitises errors (D-4)
    Tools/ReadTools.cs                9 read tools   [MinRole(Read)]
    Tools/IntentTools.cs              5 intent tools [MinRole(WriteIntent)]
    Tools/RawFsTools.cs              5 raw-fs tools [MinRole(FullWrite)]
  Mdex.Mcp/                           NEW console → mdex-mcp (global tool, stdio host, no ASP.NET)
    Mdex.Mcp.csproj
    LaunchOptions.cs                  fail-closed --role / --root parsing
    Program.cs                        Generic Host + stdio transport + DI wiring
tests/
  Mdex.Core.Tests/                    +RoleTests, +ToolAuthorizerTests
  Mdex.Mcp.Tools.Tests/               NEW — MdexBackend, guard, E2E tool calls, traversal, role-refusal
    TestVault.cs                      copy of the Core.Tests helper (namespace Mdex.Mcp.Tools.Tests)
  Mdex.Mcp.Tests/                     NEW — LaunchOptions (fail-closed)
```

Reference rules (design §1): `Mdex.Mcp.Tools → Mdex.Core` + `ModelContextProtocol`. `Mdex.Mcp → Mdex.Mcp.Tools` (transitively Core + SDK) — **no** ASP.NET, **no** `Mdex.Tokens`. `Role`/`ToolAuthorizer` live in `Mdex.Core.Security` so Plan C's `Mdex.Tokens` can reuse `Role` by referencing only `Mdex.Core` (no MCP SDK pulled into the token admin).

---

## Task 1: Role ladder + authorizer in `Mdex.Core.Security`

The role vocabulary is shared by both hosts and by the token store (Plan C), and it must stay free of ASP.NET and the MCP SDK. It therefore lives in `Mdex.Core` (design §1: "+ Rollen-Authorizer"). `CommandEngine` stays role-free — roles are enforced one layer up, at the tools (Task 3–6).

**Files:**
- Create: `src/Mdex.Core/Security/Role.cs`, `IRoleProvider.cs`, `ToolAuthorizer.cs`, `MinRoleAttribute.cs`, `MdexToolException.cs`
- Test: `tests/Mdex.Core.Tests/RoleTests.cs`, `tests/Mdex.Core.Tests/ToolAuthorizerTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Mdex.Core.Tests/RoleTests.cs`:
```csharp
using Mdex.Core.Security;
namespace Mdex.Core.Tests;

public class RoleTests
{
    [Fact]
    public void Ladder_is_ordered_read_lt_intent_lt_full()
    {
        Assert.True(Role.Read < Role.WriteIntent);
        Assert.True(Role.WriteIntent < Role.FullWrite);
    }

    [Theory]
    [InlineData(Role.Read, "read")]
    [InlineData(Role.WriteIntent, "write-intent")]
    [InlineData(Role.FullWrite, "fullwrite")]
    public void Wire_roundtrip(Role role, string wire)
    {
        Assert.Equal(wire, Roles.ToWire(role));
        Assert.True(Roles.TryParse(wire, out var parsed));
        Assert.Equal(role, parsed);
    }

    [Fact]
    public void TryParse_rejects_garbage()
    {
        Assert.False(Roles.TryParse("admin", out _));
        Assert.False(Roles.TryParse(null, out _));
    }
}
```

`tests/Mdex.Core.Tests/ToolAuthorizerTests.cs`:
```csharp
using Mdex.Core.Security;
namespace Mdex.Core.Tests;

public class ToolAuthorizerTests
{
    static ToolAuthorizer With(Role role) => new(new FixedRoleProvider(role));

    [Fact]
    public void Allows_when_role_meets_or_exceeds_minimum()
    {
        With(Role.FullWrite).Require(Role.WriteIntent);   // no throw
        With(Role.Read).Require(Role.Read);               // no throw
    }

    [Fact]
    public void Blocks_when_role_below_minimum()
    {
        Assert.Throws<MdexToolException>(() => With(Role.WriteIntent).Require(Role.FullWrite));
        Assert.Throws<MdexToolException>(() => With(Role.Read).Require(Role.WriteIntent));
    }
}
```

- [ ] **Step 2: Run → expect FAIL** (types undefined)

Run: `dotnet test "$REPO\Mdex.sln" --filter "RoleTests|ToolAuthorizerTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3: Implement the five types**

`src/Mdex.Core/Security/Role.cs`:
```csharp
namespace Mdex.Core.Security;

/// <summary>Capability ladder: each level includes the ones below it.</summary>
public enum Role { Read = 0, WriteIntent = 1, FullWrite = 2 }

public static class Roles
{
    public static string ToWire(Role role) => role switch
    {
        Role.Read => "read",
        Role.WriteIntent => "write-intent",
        Role.FullWrite => "fullwrite",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static bool TryParse(string? wire, out Role role)
    {
        switch (wire)
        {
            case "read": role = Role.Read; return true;
            case "write-intent": role = Role.WriteIntent; return true;
            case "fullwrite": role = Role.FullWrite; return true;
            default: role = default; return false;
        }
    }
}
```

`src/Mdex.Core/Security/IRoleProvider.cs`:
```csharp
namespace Mdex.Core.Security;

/// <summary>
/// Supplies the current caller's role to <see cref="ToolAuthorizer"/>. Local host: a
/// <see cref="FixedRoleProvider"/> set once from the <c>--role</c> launch flag. Remote
/// host (Plan C): an HTTP adapter reading the authenticated token's role claim per request.
/// </summary>
public interface IRoleProvider
{
    Role Current { get; }
}

/// <summary>A role fixed for the whole process (the local stdio session model).</summary>
public sealed class FixedRoleProvider(Role role) : IRoleProvider
{
    public Role Current { get; } = role;
}
```

`src/Mdex.Core/Security/ToolAuthorizer.cs`:
```csharp
namespace Mdex.Core.Security;

/// <summary>Enforces a per-tool minimum role against the caller's <see cref="IRoleProvider.Current"/>.
/// Default-deny: anything below the minimum throws. Host-agnostic (no ASP.NET, no MCP SDK).</summary>
public sealed class ToolAuthorizer(IRoleProvider provider)
{
    public void Require(Role minimum)
    {
        if (provider.Current < minimum)
            throw new MdexToolException($"this tool requires the '{Roles.ToWire(minimum)}' role", null);
    }
}
```

`src/Mdex.Core/Security/MinRoleAttribute.cs`:
```csharp
namespace Mdex.Core.Security;

/// <summary>Declares the minimum role a tool requires. Presence on every <c>[McpServerTool]</c>
/// is enforced by the reflection guard test (Plan B Task 6), so a tool that forgets it fails the
/// build (default-deny). Mirrors the in-method <c>auth.Require(...)</c> call.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MinRoleAttribute(Role minimum) : Attribute
{
    public Role Minimum { get; } = minimum;
}
```

`src/Mdex.Core/Security/MdexToolException.cs`:
```csharp
namespace Mdex.Core.Security;

/// <summary>A user-facing tool error: a sanitised message (safe to return to the AI client)
/// plus optional structured details (e.g. candidate slugs). Thrown by <see cref="ToolAuthorizer"/>
/// and by the tool backend; never carries a stack trace or an absolute path.</summary>
public sealed class MdexToolException(string message, object? details = null) : Exception(message)
{
    public object? Details { get; } = details;
}
```

- [ ] **Step 4: Run → expect PASS**

Run: `dotnet test "$REPO\Mdex.sln" --filter "RoleTests|ToolAuthorizerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(core): add role ladder + ToolAuthorizer in Mdex.Core.Security"
```

---

## Task 2: Scaffold `Mdex.Mcp.Tools` + `MdexBackend` (in-process adapter, sanitised)

`MdexBackend` is the single seam every tool calls: it pins the vault root, runs the verb through `CommandEngine`, returns the `Data` **object**, and maps errors — a `CommandException` becomes a user-facing `MdexToolException` (message + details preserved); **any other** exception is logged server-side and replaced by a generic `"internal error"` so no stack trace / absolute path ever reaches the AI client (design §3, security D-4).

**Files:**
- Create: `src/Mdex.Mcp.Tools/Mdex.Mcp.Tools.csproj`, `src/Mdex.Mcp.Tools/MdexBackend.cs`
- Create: `tests/Mdex.Mcp.Tools.Tests/Mdex.Mcp.Tools.Tests.csproj`, `tests/Mdex.Mcp.Tools.Tests/TestVault.cs`, `tests/Mdex.Mcp.Tools.Tests/MdexBackendTests.cs`
- Modify: `Mdex.sln`

- [ ] **Step 1: Create the two projects and wire references**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
dotnet new classlib -n Mdex.Mcp.Tools -o "$REPO\src\Mdex.Mcp.Tools"
Remove-Item "$REPO\src\Mdex.Mcp.Tools\Class1.cs"
dotnet new xunit -n Mdex.Mcp.Tools.Tests -o "$REPO\tests\Mdex.Mcp.Tools.Tests"
Remove-Item "$REPO\tests\Mdex.Mcp.Tools.Tests\UnitTest1.cs"
dotnet add "$REPO\src\Mdex.Mcp.Tools" reference "$REPO\src\Mdex.Core\Mdex.Core.csproj"
dotnet add "$REPO\src\Mdex.Mcp.Tools" package ModelContextProtocol --prerelease
dotnet add "$REPO\tests\Mdex.Mcp.Tools.Tests" reference "$REPO\src\Mdex.Mcp.Tools\Mdex.Mcp.Tools.csproj"
dotnet add "$REPO\tests\Mdex.Mcp.Tools.Tests" reference "$REPO\src\Mdex.Core\Mdex.Core.csproj"
dotnet sln "$REPO\Mdex.sln" add "$REPO\src\Mdex.Mcp.Tools\Mdex.Mcp.Tools.csproj"
dotnet sln "$REPO\Mdex.sln" add "$REPO\tests\Mdex.Mcp.Tools.Tests\Mdex.Mcp.Tools.Tests.csproj"
```

> If `ModelContextProtocol` fails to restore, check the current version on nuget.org and pin it explicitly (`-v <version>`). Do not invent a version.

- [ ] **Step 2: Set `src/Mdex.Mcp.Tools/Mdex.Mcp.Tools.csproj`** (keep the `ModelContextProtocol` `PackageReference` the previous step added):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Mcp.Tools</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Mdex.Core\Mdex.Core.csproj" />
    <!-- PackageReference Include="ModelContextProtocol" Version="…" added by dotnet add package -->
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add the `TestVault` helper to the new test project** (copy of the Core.Tests helper, only the namespace changes) — `tests/Mdex.Mcp.Tools.Tests/TestVault.cs`:

```csharp
using System.Text;

namespace Mdex.Mcp.Tools.Tests;

/// <summary>Disposable temp vault: BRAID folders + the real template files.</summary>
public sealed class TestVault : IDisposable
{
    public string Root { get; }

    public TestVault()
    {
        Root = Path.Combine(Path.GetTempPath(), "mdex-test-" + Guid.NewGuid().ToString("N"));
        foreach (var d in new[] { "inbox", "zettel", "projects", "areas",
                                  "resources/references", "resources/templates" })
            Directory.CreateDirectory(Path.Combine(Root, d.Replace('/', Path.DirectorySeparatorChar)));

        WriteTemplate("frontmatter-inbox.md",
            "---\ntype: inbox\ncreated: {{created}}\nmodified: {{modified}}\nbacklinks: []\n---\n");
        WriteTemplate("frontmatter-zettel.md",
            "---\ntype: zettel\ncreated: {{created}}\nmodified: {{modified}}\ntags: []\nbacklinks: []\nsource:\n---\n");
        WriteTemplate("frontmatter-project.md",
            "---\ntype: project\ncreated: {{created}}\nmodified: {{modified}}\ntags: []\nbacklinks: []\n---\n");
        WriteTemplate("frontmatter-area.md",
            "---\ntype: area\ncreated: {{created}}\nmodified: {{modified}}\ntags: []\nbacklinks: []\n---\n");
        WriteTemplate("frontmatter-reference.md",
            "---\ntype: reference\ncreated: {{created}}\nmodified: {{modified}}\nauthor:\nsource:\nbacklinks: []\n---\n");
        WriteTemplate("body-project-default.md",
            "## Ziel\n\n## Kontext\n\n## Tasks\n- [ ]\n\n## Log\n### YYYY-MM-DD\n-\n\n---\nVerbundene Zettel:\n-\n");
    }

    void WriteTemplate(string name, string content) =>
        File.WriteAllText(Path.Combine(Root, "resources", "templates", name), content, new UTF8Encoding(false));

    public string WriteNote(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    public string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}
```

- [ ] **Step 4: Write the failing `MdexBackend` test** — `tests/Mdex.Mcp.Tools.Tests/MdexBackendTests.cs`:

```csharp
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
```

> The last test relies on Plan A / the prerequisite security fixes (`--content=` and `--` in `ArgReader`). If `write-file` does not throw an `IOException` for a file-as-directory on this platform, substitute any verb+arg pair that triggers a non-`CommandException`; the assertion under test is the sanitisation, not the specific IO failure.

- [ ] **Step 5: Run → expect FAIL** (`MdexBackend` missing)

Run: `dotnet test "$REPO\Mdex.sln" --filter MdexBackendTests`
Expected: FAIL (does not compile).

- [ ] **Step 6: Implement `MdexBackend`** — `src/Mdex.Mcp.Tools/MdexBackend.cs`:

```csharp
using Mdex.Core.Cli;
using Mdex.Core.Security;

namespace Mdex.Mcp.Tools;

/// <summary>
/// The single in-process seam every MCP tool calls. Pins the vault root, runs a verb through
/// <see cref="CommandEngine"/>, and returns the operation's <c>Data</c> object (the MCP SDK
/// serialises it — no intermediate JSON). Error mapping is the tool layer's D-4 boundary:
/// a <see cref="CommandException"/> surfaces as a user-facing <see cref="MdexToolException"/>
/// (message + details); any other exception is logged and replaced by a generic message so no
/// stack trace or absolute path ever reaches the (untrusted) AI caller.
/// </summary>
public sealed class MdexBackend(CommandEngine engine, string? vaultRoot, Action<string>? logError = null)
{
    public object? Call(string verb, params string[] args)
    {
        try
        {
            return engine.Run(verb, args, vaultRoot).Data;
        }
        catch (CommandException ex)
        {
            throw new MdexToolException(ex.Message, ex.Details);
        }
        catch (Exception ex)
        {
            (logError ?? (_ => { }))($"[mdex] unexpected error in '{verb}': {ex}");
            throw new MdexToolException("internal error");
        }
    }
}
```

- [ ] **Step 7: Run → expect PASS**

Run: `dotnet test "$REPO\Mdex.sln" --filter MdexBackendTests`
Expected: PASS (4 tests).

- [ ] **Step 8: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(tools): scaffold Mdex.Mcp.Tools + sanitised in-process MdexBackend"
```

---

## Task 3: Read tools (9)

Each tool is a static method: it takes `MdexBackend` and `ToolAuthorizer` from DI plus its typed args, requires its role, and forwards to a verb. Args use the uniform rule from the prerequisite (`--key=value`, booleans bare, then `--`, then positionals) so any content round-trips. No per-tool unit test here — behaviour is covered by `Mdex.Core.Tests` (per verb) and Task 7 (E2E through the tools).

**Files:**
- Create: `src/Mdex.Mcp.Tools/Tools/ReadTools.cs`

- [ ] **Step 1: Implement** — `src/Mdex.Mcp.Tools/Tools/ReadTools.cs`:

```csharp
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
```

- [ ] **Step 2: Build** — Run: `dotnet build "$REPO\Mdex.sln"` → Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(tools): add 9 read MCP tools"
```

---

## Task 4: Intent tools (5)

**Files:**
- Create: `src/Mdex.Mcp.Tools/Tools/IntentTools.cs`

- [ ] **Step 1: Implement** — `src/Mdex.Mcp.Tools/Tools/IntentTools.cs`:

```csharp
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
```

- [ ] **Step 2: Build** — Run: `dotnet build "$REPO\Mdex.sln"` → Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(tools): add 5 intent MCP tools"
```

---

## Task 5: Raw FS tools (5)

**Files:**
- Create: `src/Mdex.Mcp.Tools/Tools/RawFsTools.cs`

- [ ] **Step 1: Implement** — `src/Mdex.Mcp.Tools/Tools/RawFsTools.cs`:

```csharp
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
```

- [ ] **Step 2: Build** — Run: `dotnet build "$REPO\Mdex.sln"` → Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(tools): add 5 raw-fs MCP tools"
```

---

## Task 6: Default-deny guard (security gate — Plan-phase 1; D-2, N-3)

The per-method `auth.Require(...)` calls are opt-in: a future tool that forgets one would be silently open to any caller (D-2). This makes "every tool declares **and** can enforce a minimum role" a CI invariant by reflecting over the shared `Mdex.Mcp.Tools` assembly. It also locks the tool count to the engine's command set, catching drift in either direction (N-3).

**Files:**
- Create: `tests/Mdex.Mcp.Tools.Tests/ToolGateGuardTests.cs`

- [ ] **Step 1: Write the guard test** — `tests/Mdex.Mcp.Tools.Tests/ToolGateGuardTests.cs`:

```csharp
using System.Reflection;
using Mdex.Core.Cli;
using Mdex.Core.Security;
using ModelContextProtocol.Server;
namespace Mdex.Mcp.Tools.Tests;

public class ToolGateGuardTests
{
    static IEnumerable<MethodInfo> Tools() =>
        typeof(MdexBackend).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    [Fact]
    public void There_are_nineteen_tools()   // 9 read + 5 intent + 5 raw-fs
        => Assert.Equal(19, Tools().Count());

    [Fact]
    public void Every_tool_declares_a_minimum_role()
    {
        var missing = Tools().Where(m => m.GetCustomAttribute<MinRoleAttribute>() is null)
                             .Select(m => m.Name).ToArray();
        Assert.True(missing.Length == 0, "tools without [MinRole]: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_tool_takes_the_authorizer_it_needs_to_gate()
    {
        var ungated = Tools().Where(m => m.GetParameters().All(p => p.ParameterType != typeof(ToolAuthorizer)))
                            .Select(m => m.Name).ToArray();
        Assert.True(ungated.Length == 0, "tools without ToolAuthorizer param: " + string.Join(", ", ungated));
    }

    [Fact]
    public void Tool_count_matches_engine_commands_minus_meta()
    {
        // One tool per runnable verb (the meta 'version' verb is not exposed as a tool).
        var verbs = CommandEngine.Default().Commands.Count(c => c.Name != "version");
        Assert.Equal(verbs, Tools().Count());
    }
}
```

> **On the count `19`:** the live surface is **9 + 5 + 5 = 19** (design §6), one per runnable command in `CommandFactory.BuildRegistry()` (minus the `version` meta verb). The `Tool_count_matches_engine_commands_minus_meta` test is the durable invariant; the literal `19` is a tripwire if someone adds a tool without a command (or vice versa).

- [ ] **Step 2: Run → expect PASS**

Run: `dotnet test "$REPO\Mdex.sln" --filter ToolGateGuardTests`
Expected: PASS (4 tests). If `There_are_nineteen_tools` / the count test disagree, a tool was added without its `[MinRole]`+`ToolAuthorizer`, or a command exists with no tool — reconcile before continuing (this is the gate).

- [ ] **Step 3: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "test(tools): default-deny guard — every tool declares [MinRole] (D-2)"
```

---

## Task 7: End-to-end tool tests — containment + role refusal (security gate — Plan-phase 2)

These invoke the **real** static tool methods against a real temp vault through the real `MdexBackend`/`CommandEngine`/`PathGuard` — the design's "E2E-Traversal-Test durch die Tools" and "`read` verweigert Write-Tools" gates. No MCP server is booted; the tool method *is* the unit under test, so the gate is exercised independently of the stdio host.

**Files:**
- Create: `tests/Mdex.Mcp.Tools.Tests/ToolEndToEndTests.cs`

- [ ] **Step 1: Write the tests** — `tests/Mdex.Mcp.Tools.Tests/ToolEndToEndTests.cs`:

```csharp
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
        var data = ReadTools.ReadNote(Backend(v.Root), Auth(Role.Read), "a");
        Assert.Contains("Hello world", JsonSerializer.Serialize(data));
    }

    [Fact]
    public void Read_tool_cannot_escape_the_vault()   // containment through the tool layer
    {
        using var v = new TestVault();
        var ex = Assert.Throws<MdexToolException>(
            () => ReadTools.ReadNote(Backend(v.Root), Auth(Role.Read), "../../../etc/passwd"));
        Assert.DoesNotContain(":\\", ex.Message);     // no leaked absolute path
        Assert.DoesNotContain("/etc/passwd", ex.Message);
    }

    [Fact]
    public void Read_role_is_refused_the_write_tools()   // ladder enforced at the tool
    {
        using var v = new TestVault();
        var backend = Backend(v.Root);
        var read = Auth(Role.Read);
        Assert.Throws<MdexToolException>(() => IntentTools.CaptureInbox(backend, read, "hello"));
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
        var data = IntentTools.CaptureInbox(Backend(v.Root), Auth(Role.WriteIntent), "a captured thought");
        Assert.NotNull(data);                          // succeeds (no throw) and returns the Data object
    }
}
```

> The traversal assertions only check that the tool **throws** and leaks no path; the actual jail is `PathGuard` in `Mdex.Core` (Plan-A gate N-2). This test proves the jail is still reached through the tool wrapper (no bypass introduced at the tool layer).

- [ ] **Step 2: Run → expect PASS**

Run: `dotnet test "$REPO\Mdex.sln" --filter ToolEndToEndTests`
Expected: PASS (5 tests).

- [ ] **Step 3: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "test(tools): E2E containment + role-refusal through the tools (gate phase 2)"
```

---

## Task 8: `mdex-mcp` console + fail-closed `--role` parsing (security gate — Plan-phase 2)

Scaffold the local host and extract its launch-flag parsing into a testable `LaunchOptions` so the fail-closed contract (no `--role` ⇒ refuse to start) is a unit test, not a comment.

**Files:**
- Create: `src/Mdex.Mcp/Mdex.Mcp.csproj`, `src/Mdex.Mcp/LaunchOptions.cs`
- Create: `tests/Mdex.Mcp.Tests/Mdex.Mcp.Tests.csproj`, `tests/Mdex.Mcp.Tests/LaunchOptionsTests.cs`
- Modify: `Mdex.sln`

- [ ] **Step 1: Create the projects and references**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
dotnet new console -n Mdex.Mcp -o "$REPO\src\Mdex.Mcp"
Remove-Item "$REPO\src\Mdex.Mcp\Program.cs"
dotnet new xunit -n Mdex.Mcp.Tests -o "$REPO\tests\Mdex.Mcp.Tests"
Remove-Item "$REPO\tests\Mdex.Mcp.Tests\UnitTest1.cs"
dotnet add "$REPO\src\Mdex.Mcp" reference "$REPO\src\Mdex.Mcp.Tools\Mdex.Mcp.Tools.csproj"
dotnet add "$REPO\src\Mdex.Mcp" package ModelContextProtocol --prerelease
dotnet add "$REPO\src\Mdex.Mcp" package Microsoft.Extensions.Hosting
dotnet add "$REPO\tests\Mdex.Mcp.Tests" reference "$REPO\src\Mdex.Mcp\Mdex.Mcp.csproj"
dotnet sln "$REPO\Mdex.sln" add "$REPO\src\Mdex.Mcp\Mdex.Mcp.csproj"
dotnet sln "$REPO\Mdex.sln" add "$REPO\tests\Mdex.Mcp.Tests\Mdex.Mcp.Tests.csproj"
```

- [ ] **Step 2: Set `src/Mdex.Mcp/Mdex.Mcp.csproj`** (global tool; keep the two `PackageReference`s the previous step added):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Mcp</RootNamespace>
    <AssemblyName>mdex-mcp</AssemblyName>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>mdex-mcp</ToolCommandName>
    <PackageId>Mdex.Mcp</PackageId>
    <Version>0.1.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Mdex.Mcp.Tools\Mdex.Mcp.Tools.csproj" />
    <!-- PackageReferences ModelContextProtocol + Microsoft.Extensions.Hosting added above -->
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the failing `LaunchOptions` test** — `tests/Mdex.Mcp.Tests/LaunchOptionsTests.cs`:

```csharp
using Mdex.Core.Security;
using Mdex.Mcp;
namespace Mdex.Mcp.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Missing_role_is_fail_closed()
    {
        var ex = Assert.Throws<LaunchOptionsException>(() => LaunchOptions.Parse(Array.Empty<string>()));
        Assert.Contains("--role", ex.Message);
    }

    [Fact]
    public void Invalid_role_is_rejected()
        => Assert.Throws<LaunchOptionsException>(() => LaunchOptions.Parse(new[] { "--role", "admin" }));

    [Theory]
    [InlineData("read", Role.Read)]
    [InlineData("write-intent", Role.WriteIntent)]
    [InlineData("fullwrite", Role.FullWrite)]
    public void Valid_role_is_parsed(string wire, Role expected)
    {
        var opts = LaunchOptions.Parse(new[] { "--role", wire });
        Assert.Equal(expected, opts.Role);
        Assert.Null(opts.Root);
    }

    [Fact]
    public void Root_flag_is_captured()
    {
        var opts = LaunchOptions.Parse(new[] { "--role", "read", "--root", @"C:\vault" });
        Assert.Equal(@"C:\vault", opts.Root);
    }
}
```

- [ ] **Step 4: Run → expect FAIL** (`LaunchOptions` missing)

Run: `dotnet test "$REPO\Mdex.sln" --filter LaunchOptionsTests`
Expected: FAIL (does not compile).

- [ ] **Step 5: Implement `LaunchOptions`** — `src/Mdex.Mcp/LaunchOptions.cs`:

```csharp
using Mdex.Core.Security;

namespace Mdex.Mcp;

public sealed class LaunchOptionsException(string message) : Exception(message);

/// <summary>Parsed launch flags for the local stdio host. <c>--role</c> is required (fail-closed):
/// permission must never be implicit, so the host refuses to start without it (design §4).</summary>
public sealed record LaunchOptions(Role Role, string? Root)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? roleArg = null, rootArg = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--role" && i + 1 < args.Length) roleArg = args[++i];
            else if (args[i] == "--root" && i + 1 < args.Length) rootArg = args[++i];
        }

        if (roleArg is null)
            throw new LaunchOptionsException(
                "missing required --role <read|write-intent|fullwrite> (no default — set it explicitly)");
        if (!Roles.TryParse(roleArg, out var role))
            throw new LaunchOptionsException($"invalid --role '{roleArg}' (expected read|write-intent|fullwrite)");

        return new LaunchOptions(role, rootArg);
    }
}
```

- [ ] **Step 6: Run → expect PASS**

Run: `dotnet test "$REPO\Mdex.sln" --filter LaunchOptionsTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(mcp): scaffold mdex-mcp + fail-closed --role parsing"
```

---

## Task 9: Wire the stdio host (`Program.cs`)

Build the Generic Host: parse the flags (fail-closed), pin the vault root, register the engine, the role provider (from the flag), the backend and the authorizer, then start the MCP server over **stdio** with the tools discovered from `Mdex.Mcp.Tools`.

> **stdio gotcha:** the MCP stdio transport uses **stdout** as the protocol channel. The default console logger also writes to stdout and would corrupt the stream. The wiring below routes **all** logging to **stderr**.

**Files:**
- Create: `src/Mdex.Mcp/Program.cs`

- [ ] **Step 1: Write `Program.cs`** — `src/Mdex.Mcp/Program.cs`:

```csharp
using Mdex.Core.Cli;
using Mdex.Core.Security;
using Mdex.Mcp;
using Mdex.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// --- fail-closed launch flags (no --role ⇒ refuse to start) ---
LaunchOptions opts;
try
{
    opts = LaunchOptions.Parse(args);
}
catch (LaunchOptionsException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine("Usage: mdex-mcp --role <read|write-intent|fullwrite> [--root <vault>]");
    return 1;
}

// --- pin the vault root (flag → $MDEX_ROOT → config), shared by every call this session ---
var engine = CommandEngine.Default();
var vaultRoot = VaultResolver.Resolve(opts.Root);

var builder = Host.CreateApplicationBuilder();   // no args: we parse our own flags

// stdout is the MCP transport — send every log line to stderr instead.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(engine);
builder.Services.AddSingleton(new MdexBackend(engine, vaultRoot,
    msg => Console.Error.WriteLine(msg)));
builder.Services.AddSingleton<IRoleProvider>(new FixedRoleProvider(opts.Role));
builder.Services.AddScoped<ToolAuthorizer>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(ReadTools).Assembly);

await builder.Build().RunAsync();
return 0;
```

> **SDK fallbacks (verify against the installed package):**
> - If `WithToolsFromAssembly(Assembly)` has no assembly-taking overload, register each type explicitly: `.WithTools<ReadTools>().WithTools<IntentTools>().WithTools<RawFsTools>()` (using `Mdex.Mcp.Tools.Tools`).
> - If `WithStdioServerTransport()` is renamed, inspect `IMcpServerBuilder` extension methods and pick the stdio one.
> - If a tool method returning `object?` produces no/incorrect output schema, change the tool return type to `string` and serialise in `MdexBackend.Call` (`JsonSerializer.Serialize(data)`) — but prefer the object form (design §3: the SDK serialises the `Data` object).
> - Confirm the SDK turns a thrown `MdexToolException` into an `isError` tool result **without** appending a stack trace; if it does append one, configure its error serialisation off (D-4). `MdexBackend` already guarantees the *message* is safe.

- [ ] **Step 2: Build** — Run: `dotnet build "$REPO\Mdex.sln"` → Expected: `Build succeeded`.

- [ ] **Step 3: Fail-closed smoke test (automated check)**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
dotnet run --project "$REPO\src\Mdex.Mcp" -- 2>&1 | Write-Output
$LASTEXITCODE
```
Expected: prints `Error: missing required --role …` and the usage line; `$LASTEXITCODE` is `1`. (No `--role` ⇒ the host refuses to start — the Plan-phase-2 fail-closed gate, end to end.)

- [ ] **Step 4: Boot smoke test (manual, MCP client)**

The full positive path is a stdio JSON-RPC handshake; verify it with a real client rather than a brittle scripted pipe. Point Claude Code at the host (see Task 10 `.mcp.json`) with `--role read` against a throwaway vault and confirm `tools/list` returns the 19 tools and a `read-note` call works. Tool *behaviour* is already covered automatically by Task 7; this step confirms only the transport/handshake.

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(mcp): wire stdio host (Generic Host, stderr logging, role from flag)"
```

---

## Task 10: Document `mdex-mcp` + client wiring

**Files:**
- Modify: `README.md`
- Create: `docs/examples/mcp.json` (a copy-paste `.mcp.json` snippet for Claude Code)

- [ ] **Step 1: Add a README section.** Append under the existing command docs:

````markdown
## mdex-mcp (local MCP server)

`mdex-mcp` exposes the mdex operations as MCP tools over **stdio** for AI agents that run
on this machine against the **local checkout** (Claude Code, Cowork, Codex CLI). It calls
`Mdex.Core` in-process — no network, no token, no ASP.NET — and stages every tool by role.

**Roles (ladder, fail-closed — you must pass `--role`):** `read` ⊂ `write-intent` ⊂ `fullwrite`
- `read` — the 9 retrieval tools.
- `write-intent` — adds the 5 intent tools (capture/create/append/promote; non-destructive).
- `fullwrite` — adds the 5 raw-fs tools (write/append/move/mkdir/delete).

There is **no default role**: launching without `--role` is an error, so you always choose
consciously what the AI may do to the local vault.

```
mdex-mcp --role <read|write-intent|fullwrite> [--root <vault>]
```

The vault root resolves from `--root`, else `$MDEX_ROOT`, else config.

**Client wiring (local only):** register `mdex-mcp` in the agent's local config against the
checkout it works in; do **not** register the remote (`mdex-gateway`) there. See
`docs/examples/mcp.json` and the topology rules in
[the design](docs/2026-06-05-mdex-mcp-local-remote-design.md) §5.
````

- [ ] **Step 2: Write the example client config** — `docs/examples/mcp.json`:

```json
{
  "mcpServers": {
    "mdex": {
      "command": "mdex-mcp",
      "args": ["--role", "write-intent", "--root", "C:\\Users\\you\\repos\\mdex"]
    }
  }
}
```

> One logical name (`mdex`) bound to the local stdio host. Per design §5: where a checkout
> exists, the agent uses the local host and the remote is **not** configured here (no remote
> bearer token on the dev machine — credential hygiene, finding N-4).

- [ ] **Step 3: Full solution test run**

Run: `dotnet test "$REPO\Mdex.sln"`
Expected: **all tests green** — Plan A's suite + the new `RoleTests`, `ToolAuthorizerTests`, `MdexBackendTests`, `ToolGateGuardTests`, `ToolEndToEndTests`, `LaunchOptionsTests`.

- [ ] **Step 4: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "docs: document mdex-mcp local host + client wiring example"
```

---

## Done-condition for Plan B

- `dotnet build "$REPO\Mdex.sln"` succeeds; `dotnet test "$REPO\Mdex.sln"` reports **all tests green**.
- `Mdex.Core.Security` holds the role ladder + `ToolAuthorizer` + `IRoleProvider` (ASP.NET-/SDK-free, reusable by Plan C's `Mdex.Tokens`).
- `Mdex.Mcp.Tools` exposes the 19 role-gated tools over the in-process `MdexBackend`, returning `Data` objects (no envelope reparse).
- `mdex-mcp` is a global-tool stdio host that refuses to start without `--role` and serves the tools against the local checkout.
- **Security gates met:** Plan-phase 1 — `[MinRole]` + reflection guard on the shared lib (Task 6, D-2/N-3); Plan-phase 2 — fail-closed `--role` (Task 8) + E2E traversal containment + `read` refuses write tools (Task 7); D-4 sanitisation in `MdexBackend` (Task 2).
- No ASP.NET and no `Mdex.Tokens` reference anywhere in `Mdex.Mcp` / `Mdex.Mcp.Tools` (verify: `dotnet list "$REPO\src\Mdex.Mcp" reference` shows only `Mdex.Mcp.Tools`).

---

## Self-Review (against the design + security plan)

- **§1 (project structure / reference rules):** `Mdex.Mcp.Tools → Core + SDK`; `Mdex.Mcp → Mdex.Mcp.Tools` only; role vocab in Core so `Mdex.Tokens` reuses `Role` without the SDK. ✔ — *deviation from the design's literal placement:* the design text lists "Rollen-Authorizer" inside `Mdex.Core` (line 81) and the tools "tragen [MinRole]" in `Mdex.Mcp.Tools` (line 84); this plan follows that exactly (authorizer + `Role` + `MinRoleAttribute` in Core; the `[McpServerTool]` wrappers in `Mdex.Mcp.Tools`). `MdexBackend` is placed in `Mdex.Mcp.Tools` (its only consumers are the tools); it needs no SDK, so it could equally sit in Core — chosen co-location keeps Core's surface to the role vocabulary the design names.
- **§2 (B2, two sibling hosts):** stdio host built here; HTTP host is Plan C; both consume the same `Mdex.Mcp.Tools`. ✔
- **§3 (Data object, no envelope reparse; typed exceptions; tool-layer sanitisation):** `MdexBackend.Call` returns `Data` and maps `CommandException`→`MdexToolException`, unexpected→generic. ✔
- **§4 (roles: ladder geerbt; local = launch flag, no default):** ladder in Core; `FixedRoleProvider` from `LaunchOptions` (fail-closed). ✔
- **§5 (topology):** Task 10 documents local-only wiring + the no-remote-token rule (N-4). ✔
- **Security gates:** Plan-phase 1 (Task 6) and Plan-phase 2 (Tasks 7, 8) + D-4 (Task 2) — all present and named in their tasks. ✔
- **Out of scope (correct):** tokens, auth handler, hot-reload, ASP.NET, the central invocation filter, `mdex-gateway` — all Plan C. The Phase-3 *code* review (constant-time compare, header edge-cases, token-leak) targets the gateway and runs after Plan C.
- **Type consistency:** `Role`/`Roles`/`ToolAuthorizer`/`IRoleProvider`/`FixedRoleProvider`/`MinRoleAttribute`/`MdexToolException` (Core.Security); `MdexBackend.Call(verb, params args) → object?`; `LaunchOptions(Role, string? Root)` — used consistently across Tasks 1–9. Verb strings match `CommandFactory` command `Name`s (verified against the source). ✔
- **Placeholder scan:** none — every step has exact files, code and commands. SDK uncertainties are flagged with concrete fallbacks rather than left vague.
- **Honest test-coverage note:** per-verb argument correctness inside each of the 19 tools is not exhaustively E2E-tested (only representative tools in Task 7); the underlying verbs are fully covered in `Mdex.Core.Tests`, and the count guard (Task 6) catches tool↔command drift. Full per-tool E2E would be redundant with Core coverage (YAGNI).

---

## Execution Handoff

Plan complete and saved to `docs/2026-06-05-mdex-mcp-local-host-plan.md`. Two execution options:

1. **Subagent-Driven (recommended)** — a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute the tasks in this session with checkpoints for review.

Which approach? (Per the agreed sequencing, execution comes after Plan C is written and you've reviewed both.)
