# mdex-mcp & mdex-mcpctl Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the remote MCP server `mdex-mcp` (ASP.NET Core) and the token-admin CLI `mdex-mcpctl`, both on top of the existing `mdext` CLI, so AI clients get a guarded, role-gated, mdex-aware HTTP tool surface over the vault.

**Architecture:** The server calls mdext operations **in-process** through a shared `CliDispatcher` that returns the exact same `--json` envelope as the CLI (no drift, no subprocess). A small `Mdex.Tokens` library owns the hashed token store (`tokens.json`) and a three-level role ladder (`read` ⊂ `write-intent` ⊂ `fullwrite`); `mdex-mcpctl` writes that store and the server watches it for hot-reload. MCP tools are thin methods that map a tool call to a CLI verb and enforce the caller's role.

**Tech Stack:** .NET 10 · C# · xUnit · `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (MCP C# SDK) · ASP.NET Core / Kestrel · System.Security.Cryptography (SHA-256, RNG). Spec: [2026-06-04-mdex-mcp-design.md](2026-06-04-mdex-mcp-design.md).

> **Design deviations carried from the spec:** in-process instead of subprocess (supersedes 2026-06-01 §5); three roles instead of two (supersedes §2).

> **PREREQUISITE — run the mdext security fixes first:** This plan assumes [2026-06-05-mdext-security-fixes-plan.md](2026-06-05-mdext-security-fixes-plan.md) has already landed. That plan (a) hardens `ArgReader` (`--key=value` + `--` end-of-options) and (b) closes the Phase-1 vault-escape findings (HIGH-1 slug/path bypass, empty/root targets, symlinks) **on which this role ladder depends** — without them, `read` = "read any `.md` on the host." Task 1B no longer lives here; it is Task 1 of the prerequisite plan.

> **Robust argument passing (from the prerequisite):** With the hardened `ArgReader`, the MCP tools build args by one uniform rule: **value-flags as `--key=value`, boolean flags bare, then `--`, then positionals** — bulletproof for any content (newlines, `=`, leading `--`, empty). Backward compatible: the space form `--content "text"` still works.

---

## Prerequisites

- `dotnet --version` → `10.*`.
- **The prerequisite plan [2026-06-05-mdext-security-fixes-plan.md](2026-06-05-mdext-security-fixes-plan.md) has landed** (hardened `ArgReader` + closed vault-escape findings). This plan's tools and the Task 1 sanitization test assume it.
- Repo is `mdex-tooling` with the existing `Mdex.sln`, `src/Mdex.Cli`, `tests/Mdex.Cli.Tests`; `dotnet test` is green before starting.
- Commands below use repo-root-relative paths; run them from the repo root.

---

## File Structure

```
src/
  Mdex.Cli/
    Cli/CliDispatcher.cs        NEW  reusable in-process entry → JSON envelope (broad catch, sanitized)
    Program.cs                  MOD  use CliDispatcher.BuildContext
    (ArgReader + PathGuard already hardened by the prerequisite plan)
  Mdex.Tokens/                  NEW  library
    Mdex.Tokens.csproj
    Role.cs                     Role enum + Roles wire-format helper
    TokenGenerator.cs           mdex_<base64url(32)>
    TokenHasher.cs              SHA-256 hex
    TokenEntry.cs               record { Hash, Role, Label, Created }
    TokenStore.cs               load/save/add/remove/find + reload
    TokenStoreWatcher.cs        FileSystemWatcher → store.Reload()
    TokenPaths.cs               default tokens.json path
    TokenAdmin.cs               new/list/revoke admin logic
  Mdex.Mcpctl/                  NEW  console → mdex-mcpctl
    Mdex.Mcpctl.csproj
    Program.cs                  arg parsing → TokenAdmin + console output
  Mdex.Mcp/                     NEW  ASP.NET Core → mdex-mcp
    Mdex.Mcp.csproj
    MdexBackend.cs              in-process call into CliDispatcher
    Auth/MinRoleAttribute.cs    declares each tool's minimum role (default-deny guard)
    Auth/BearerAuthHandler.cs   bearer → role claim
    Auth/ToolAuthorizer.cs      per-tool role gate
    Tools/ReadTools.cs          9 read tools
    Tools/IntentTools.cs        5 intent tools
    Tools/RawFsTools.cs         5 raw-fs tools
    Program.cs                  host wiring + config
    appsettings.json
  deploy/mdex-mcp.service       NEW  systemd unit
tests/
  Mdex.Tokens.Tests/            NEW
  Mdex.Mcp.Tests/               NEW
```

---

## Task 1: Extract `CliDispatcher` from `Program.cs`

Pull the per-command "build context, run, render envelope" logic into a reusable class the MCP server can call in-process. Behaviour of the CLI is unchanged.

**Files:**
- Create: `src/Mdex.Cli/Cli/CliDispatcher.cs`
- Modify: `src/Mdex.Cli/Program.cs:67-95`
- Test: `tests/Mdex.Cli.Tests/CliDispatcherTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Cli.Tests/CliDispatcherTests.cs
using Mdex.Cli.Cli;
namespace Mdex.Cli.Tests;

public class CliDispatcherTests
{
    [Fact]
    public void RunToJson_unknown_command_returns_error_envelope()
    {
        var json = CliDispatcher.RunToJson("nope", Array.Empty<string>());
        Assert.Contains("\"ok\": false", json);
        Assert.Contains("unknown command", json);
    }

    [Fact]
    public void RunToJson_version_returns_ok_envelope()
    {
        var json = CliDispatcher.RunToJson("version", Array.Empty<string>());
        Assert.Contains("\"ok\": true", json);
        Assert.Contains("0.1.0", json);
    }

    [Fact]
    public void RunToJson_unexpected_exception_is_sanitized_not_leaked()
    {
        // A non-CommandException (here: Directory.CreateDirectory over a *file*) must be
        // caught and returned as a generic envelope — no stack trace, no absolute path.
        var root = Path.Combine(Path.GetTempPath(), "mdex-disp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "blocker"), "x");   // a FILE where a dir is needed
        try
        {
            var json = CliDispatcher.RunToJson("write-file",
                new[] { "--content=hi", "--", "blocker/child" }, root);
            Assert.Contains("\"ok\": false", json);
            Assert.Contains("internal error", json);
            Assert.DoesNotContain("blocker", json);              // no path leaked
            Assert.DoesNotContain("Exception", json);            // no stack/type leaked
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Cli.Tests --filter CliDispatcherTests`
Expected: FAIL — `CliDispatcher` does not exist (compile error). The sanitization test additionally relies on the prerequisite plan's hardened `ArgReader` (`--content=` / `--`), which must already be landed.

- [ ] **Step 3: Create `CliDispatcher`**

```csharp
// src/Mdex.Cli/Cli/CliDispatcher.cs
using Mdex.Cli.Commands;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Cli;

/// <summary>
/// Single in-process entry point shared by the CLI <c>Main</c> and the MCP server.
/// Runs a verb with already-cleaned args and returns the stable JSON envelope string,
/// so the server cannot drift from the CLI's output.
/// </summary>
public static class CliDispatcher
{
    public static string RunToJson(string verb, string[] args, string? rootFlag = null)
    {
        var registry = CommandFactory.BuildRegistry();
        var command = registry.Get(verb);
        if (command is null) return JsonEnvelope.Err(verb, $"unknown command '{verb}'");
        try
        {
            var ctx = BuildContext(command, json: true, rootFlag: rootFlag);
            var result = command.Run(args, ctx);
            return JsonEnvelope.Ok(verb, result.Data);
        }
        catch (CommandException ex)
        {
            return JsonEnvelope.Err(verb, ex.Message, ex.Details);
        }
        catch (Exception ex)
        {
            // Never leak a .NET stack trace / absolute path to the (untrusted) MCP caller.
            // Log the detail server-side; return a generic envelope. (Phase-2 D-4 / Phase-1 MEDIUM-5.)
            Console.Error.WriteLine($"[mdex] unexpected error in '{verb}': {ex}");
            return JsonEnvelope.Err(verb, "internal error");
        }
    }

    /// <summary>Builds the CliContext for a command (vault-backed or a bare shell).</summary>
    public static CliContext BuildContext(ICommand command, bool json, string? rootFlag)
    {
        if (!command.NeedsVault)
            return new CliContext { VaultRoot = "", Json = json, Repo = null!, Guard = null!, Templates = null! };

        var root = VaultResolver.Resolve(rootFlag);
        var guard = new PathGuard(root);
        return new CliContext
        {
            VaultRoot = root, Json = json,
            Repo = new NoteRepository(root, guard),
            Guard = guard, Templates = new TemplateService(root),
        };
    }
}
```

- [ ] **Step 4: Refactor `Program.cs` to reuse `BuildContext`**

Replace the `try { … }` body's context construction (currently `src/Mdex.Cli/Program.cs:69-90`) so the inline `if (command.NeedsVault) { … } else { … }` block becomes a single call. The resulting `try` block:

```csharp
try
{
    var ctx = CliDispatcher.BuildContext(command, json, rootFlag);
    var result = command.Run(cleaned.ToArray(), ctx);
    Console.WriteLine(json ? JsonEnvelope.Ok(verb, result.Data) : result.HumanText);
    return 0;
}
catch (CommandException ex)
{
    Console.Error.WriteLine(json ? JsonEnvelope.Err(verb, ex.Message, ex.Details) : $"Error: {ex.Message}");
    return 1;
}
```

- [ ] **Step 5: Run all CLI tests to verify nothing regressed**

Run: `dotnet test tests/Mdex.Cli.Tests`
Expected: PASS — all existing tests plus the two new ones.

- [ ] **Step 6: Commit**

```bash
git add src/Mdex.Cli/Cli/CliDispatcher.cs src/Mdex.Cli/Program.cs tests/Mdex.Cli.Tests/CliDispatcherTests.cs
git commit -m "Refactor: extract CliDispatcher for in-process reuse"
```

---

## Task 1B: ~~Harden `ArgReader`~~ → moved to the prerequisite plan

`ArgReader` hardening (`--key=value` + `--` end-of-options) now lives in
[2026-06-05-mdext-security-fixes-plan.md](2026-06-05-mdext-security-fixes-plan.md) **Task 1**,
alongside the vault-escape fixes it shares a file-area with. It is a prerequisite for this plan —
nothing to do here. The MCP tools (Tasks 13–15) build args assuming it is in place.

---

## Task 2: Scaffold `Mdex.Tokens` library + test project

**Files:**
- Create: `src/Mdex.Tokens/Mdex.Tokens.csproj`, `tests/Mdex.Tokens.Tests/Mdex.Tokens.Tests.csproj`

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -n Mdex.Tokens -o src/Mdex.Tokens -f net10.0
dotnet new xunit -n Mdex.Tokens.Tests -o tests/Mdex.Tokens.Tests -f net10.0
rm src/Mdex.Tokens/Class1.cs tests/Mdex.Tokens.Tests/UnitTest1.cs
dotnet add tests/Mdex.Tokens.Tests reference src/Mdex.Tokens
dotnet sln add src/Mdex.Tokens tests/Mdex.Tokens.Tests
```

- [ ] **Step 2: Enable nullable + implicit usings in `src/Mdex.Tokens/Mdex.Tokens.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Tokens</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/Mdex.Tokens`
Expected: PASS — build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Mdex.Tokens tests/Mdex.Tokens.Tests Mdex.sln
git commit -m "Scaffold Mdex.Tokens library + tests"
```

---

## Task 3: Role ladder

**Files:**
- Create: `src/Mdex.Tokens/Role.cs`
- Test: `tests/Mdex.Tokens.Tests/RoleTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Tokens.Tests/RoleTests.cs
using Mdex.Tokens;
namespace Mdex.Tokens.Tests;

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

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Tokens.Tests --filter RoleTests`
Expected: FAIL — `Role` / `Roles` undefined.

- [ ] **Step 3: Implement**

```csharp
// src/Mdex.Tokens/Role.cs
namespace Mdex.Tokens;

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

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mdex.Tokens.Tests --filter RoleTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Tokens/Role.cs tests/Mdex.Tokens.Tests/RoleTests.cs
git commit -m "Add ordered role ladder with wire format"
```

---

## Task 4: Token generation + hashing

**Files:**
- Create: `src/Mdex.Tokens/TokenGenerator.cs`, `src/Mdex.Tokens/TokenHasher.cs`
- Test: `tests/Mdex.Tokens.Tests/TokenCryptoTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Tokens.Tests/TokenCryptoTests.cs
using Mdex.Tokens;
namespace Mdex.Tokens.Tests;

public class TokenCryptoTests
{
    [Fact]
    public void New_token_has_prefix_and_is_unique()
    {
        var a = TokenGenerator.New();
        var b = TokenGenerator.New();
        Assert.StartsWith("mdex_", a);
        Assert.NotEqual(a, b);
        Assert.True(a.Length > 20);
    }

    [Fact]
    public void Hash_is_stable_64_hex_and_differs_per_input()
    {
        var hash = TokenHasher.Hash("mdex_abc");
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, TokenHasher.Hash("mdex_abc"));
        Assert.NotEqual(hash, TokenHasher.Hash("mdex_abd"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Tokens.Tests --filter TokenCryptoTests`
Expected: FAIL — types undefined.

- [ ] **Step 3: Implement**

```csharp
// src/Mdex.Tokens/TokenGenerator.cs
using System.Security.Cryptography;

namespace Mdex.Tokens;

public static class TokenGenerator
{
    /// <summary>A fresh opaque token: <c>mdex_</c> + url-safe base64 of 32 random bytes.</summary>
    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "mdex_" + Base64Url(bytes);
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

```csharp
// src/Mdex.Tokens/TokenHasher.cs
using System.Security.Cryptography;
using System.Text;

namespace Mdex.Tokens;

public static class TokenHasher
{
    /// <summary>Lower-case hex SHA-256 of the raw token. Only the hash is ever stored.</summary>
    public static string Hash(string rawToken)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(digest);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mdex.Tokens.Tests --filter TokenCryptoTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Tokens/TokenGenerator.cs src/Mdex.Tokens/TokenHasher.cs tests/Mdex.Tokens.Tests/TokenCryptoTests.cs
git commit -m "Add token generation and SHA-256 hashing"
```

---

## Task 5: `TokenEntry` + `TokenStore` (load/save/find/add/remove)

**Files:**
- Create: `src/Mdex.Tokens/TokenEntry.cs`, `src/Mdex.Tokens/TokenStore.cs`
- Test: `tests/Mdex.Tokens.Tests/TokenStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Tokens.Tests/TokenStoreTests.cs
using Mdex.Tokens;
namespace Mdex.Tokens.Tests;

public class TokenStoreTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), "mdex-tokens-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void Missing_file_loads_empty()
    {
        var store = new TokenStore(_path);
        Assert.Empty(store.Entries);
        Assert.Null(store.FindRole("mdex_whatever"));
    }

    [Fact]
    public void Add_then_find_by_raw_token_returns_role()
    {
        var store = new TokenStore(_path);
        var raw = "mdex_secret";
        store.Add(new TokenEntry(TokenHasher.Hash(raw), Roles.ToWire(Role.WriteIntent), "langdock", "2026-06-04"));
        Assert.Equal(Role.WriteIntent, store.FindRole(raw));
        Assert.Null(store.FindRole("mdex_wrong"));
    }

    [Fact]
    public void Add_persists_across_reopen_and_stores_only_hash()
    {
        var raw = "mdex_secret";
        new TokenStore(_path).Add(new TokenEntry(TokenHasher.Hash(raw), Roles.ToWire(Role.Read), "claude", "2026-06-04"));
        var text = File.ReadAllText(_path);
        Assert.DoesNotContain(raw, text);                 // raw token never written
        Assert.Equal(Role.Read, new TokenStore(_path).FindRole(raw));
    }

    [Fact]
    public void Remove_by_label_deletes_matching_entry()
    {
        var store = new TokenStore(_path);
        store.Add(new TokenEntry(TokenHasher.Hash("mdex_a"), Roles.ToWire(Role.Read), "a", "2026-06-04"));
        Assert.True(store.Remove("a"));
        Assert.False(store.Remove("a"));
        Assert.Null(store.FindRole("mdex_a"));
    }

    [Fact]
    public void Saved_file_is_owner_only_on_posix()   // Phase-2 D-3
    {
        if (OperatingSystem.IsWindows()) return;
        var store = new TokenStore(_path);
        store.Add(new TokenEntry(TokenHasher.Hash("mdex_x"), Roles.ToWire(Role.Read), "x", "2026-06-04"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_path)); // 0600
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Tokens.Tests --filter TokenStoreTests`
Expected: FAIL — types undefined.

- [ ] **Step 3: Implement**

```csharp
// src/Mdex.Tokens/TokenEntry.cs
namespace Mdex.Tokens;

/// <summary>One stored credential. <see cref="Role"/> is the wire string ("read"/"write-intent"/"fullwrite").</summary>
public sealed record TokenEntry(string Hash, string Role, string Label, string Created);
```

```csharp
// src/Mdex.Tokens/TokenStore.cs
using System.Text.Json;

namespace Mdex.Tokens;

/// <summary>
/// In-memory snapshot of the on-disk token file. Stores only hashes. Mutations (Add/Remove)
/// persist immediately; <see cref="Reload"/> re-reads the file (used by the server's watcher).
/// </summary>
public sealed class TokenStore
{
    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    readonly string _path;
    List<TokenEntry> _entries;

    public TokenStore(string path)
    {
        _path = path;
        _entries = Load(_path);
    }

    public IReadOnlyList<TokenEntry> Entries => _entries;

    public void Reload() => _entries = Load(_path);

    public Role? FindRole(string rawToken)
    {
        var hash = TokenHasher.Hash(rawToken);
        var match = _entries.FirstOrDefault(e => e.Hash == hash);
        if (match is null) return null;
        return Roles.TryParse(match.Role, out var role) ? role : null;
    }

    public void Add(TokenEntry entry)
    {
        _entries.Add(entry);
        Save();
    }

    public bool Remove(string label)
    {
        var removed = _entries.RemoveAll(e => e.Label == label) > 0;
        if (removed) Save();
        return removed;
    }

    void Save()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(dir);
        HardenPermissions(dir, isDirectory: true);            // 0700 (Phase-2 D-3)

        // Atomic write: serialize to a temp file in the same dir, then rename over the target,
        // so the watching server never reads a half-written file (Phase-2 D-6).
        var tmp = Path.Combine(dir, Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, Opts));
        HardenPermissions(tmp, isDirectory: false);           // 0600 before it becomes the live file
        File.Move(tmp, _path, overwrite: true);
    }

    static void HardenPermissions(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows()) return;              // POSIX-only; on Windows rely on ACL inheritance
        var mode = isDirectory ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                               : UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try { File.SetUnixFileMode(path, mode); } catch { /* best-effort: FS may not support it */ }
    }

    static List<TokenEntry> Load(string path)
    {
        if (!File.Exists(path)) return new List<TokenEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<TokenEntry>>(File.ReadAllText(path), Opts) ?? new();
        }
        catch
        {
            return new List<TokenEntry>();   // corrupt/partial file → treat as empty rather than crash
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mdex.Tokens.Tests --filter TokenStoreTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Tokens/TokenEntry.cs src/Mdex.Tokens/TokenStore.cs tests/Mdex.Tokens.Tests/TokenStoreTests.cs
git commit -m "Add hashed token store with persistence"
```

---

## Task 6: Hot-reload — `Reload` test + `TokenStoreWatcher`

**Files:**
- Create: `src/Mdex.Tokens/TokenStoreWatcher.cs`
- Test: `tests/Mdex.Tokens.Tests/TokenReloadTests.cs`

- [ ] **Step 1: Write the failing test (Reload picks up external writes)**

```csharp
// tests/Mdex.Tokens.Tests/TokenReloadTests.cs
using Mdex.Tokens;
namespace Mdex.Tokens.Tests;

public class TokenReloadTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), "mdex-tokens-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void Reload_reflects_external_revoke()
    {
        var server = new TokenStore(_path);
        var admin = new TokenStore(_path);                 // a second handle = mcpctl
        admin.Add(new TokenEntry(TokenHasher.Hash("mdex_x"), Roles.ToWire(Role.Read), "x", "2026-06-04"));

        server.Reload();
        Assert.Equal(Role.Read, server.FindRole("mdex_x"));

        admin.Remove("x");                                 // revoke happens out-of-process
        server.Reload();
        Assert.Null(server.FindRole("mdex_x"));            // server no longer honours it
    }
}
```

- [ ] **Step 2: Run test to verify it fails, then passes**

Run: `dotnet test tests/Mdex.Tokens.Tests --filter TokenReloadTests`
Expected: PASS immediately (Reload already exists from Task 5). This test locks the contract the watcher depends on. If it fails, fix `Reload` before continuing.

- [ ] **Step 3: Implement the watcher**

```csharp
// src/Mdex.Tokens/TokenStoreWatcher.cs
namespace Mdex.Tokens;

/// <summary>
/// Watches the token file and calls <see cref="TokenStore.Reload"/> shortly after any change,
/// so <c>mdex-mcpctl</c> edits (e.g. revoke) take effect without a server restart. Debounced
/// because editors/writes can fire several events for one logical save.
/// </summary>
public sealed class TokenStoreWatcher : IDisposable
{
    readonly TokenStore _store;
    readonly FileSystemWatcher _fsw;
    readonly Lock _gate = new();
    Timer? _debounce;

    public TokenStoreWatcher(TokenStore store, string path)
    {
        _store = store;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        _fsw = new FileSystemWatcher(dir, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _fsw.Changed += OnChanged;
        _fsw.Created += OnChanged;
        _fsw.Deleted += OnChanged;
        _fsw.Renamed += OnChanged;
    }

    void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ => _store.Reload(), null, 300, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        _fsw.Dispose();
        lock (_gate) _debounce?.Dispose();
    }
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/Mdex.Tokens`
Expected: PASS. (`System.Threading.Lock` is .NET 9+.)

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Tokens/TokenStoreWatcher.cs tests/Mdex.Tokens.Tests/TokenReloadTests.cs
git commit -m "Add debounced token-store file watcher"
```

---

## Task 7: `TokenPaths` + `TokenAdmin`

**Files:**
- Create: `src/Mdex.Tokens/TokenPaths.cs`, `src/Mdex.Tokens/TokenAdmin.cs`
- Test: `tests/Mdex.Tokens.Tests/TokenAdminTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Tokens.Tests/TokenAdminTests.cs
using Mdex.Tokens;
namespace Mdex.Tokens.Tests;

public class TokenAdminTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), "mdex-tokens-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void New_returns_raw_token_that_authenticates_with_its_role()
    {
        var store = new TokenStore(_path);
        var admin = new TokenAdmin(store);
        var raw = admin.New(Role.FullWrite, "server");
        Assert.StartsWith("mdex_", raw);
        Assert.Equal(Role.FullWrite, store.FindRole(raw));
        Assert.Single(admin.List());
    }

    [Fact]
    public void Revoke_removes_by_label()
    {
        var admin = new TokenAdmin(new TokenStore(_path));
        admin.New(Role.Read, "tmp");
        Assert.True(admin.Revoke("tmp"));
        Assert.Empty(admin.List());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Tokens.Tests --filter TokenAdminTests`
Expected: FAIL — types undefined.

- [ ] **Step 3: Implement**

```csharp
// src/Mdex.Tokens/TokenPaths.cs
namespace Mdex.Tokens;

public static class TokenPaths
{
    /// <summary>Default token-store location: env <c>MDEX_MCP_TOKENS</c>, else per-OS config dir.</summary>
    public static string Default()
    {
        var env = Environment.GetEnvironmentVariable("MDEX_MCP_TOKENS");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "mdex-mcp", "tokens.json");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "mdex-mcp", "tokens.json");
    }
}
```

```csharp
// src/Mdex.Tokens/TokenAdmin.cs
namespace Mdex.Tokens;

/// <summary>Admin operations over a <see cref="TokenStore"/>. Used by <c>mdex-mcpctl</c>.</summary>
public sealed class TokenAdmin(TokenStore store)
{
    /// <summary>Creates a token, stores its hash, and returns the raw token (show once).</summary>
    public string New(Role role, string label)
    {
        var raw = TokenGenerator.New();
        store.Add(new TokenEntry(TokenHasher.Hash(raw), Roles.ToWire(role), label,
            DateTime.Now.ToString("yyyy-MM-dd")));
        return raw;
    }

    public IReadOnlyList<TokenEntry> List() => store.Entries;

    public bool Revoke(string label) => store.Remove(label);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mdex.Tokens.Tests --filter TokenAdminTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Tokens/TokenPaths.cs src/Mdex.Tokens/TokenAdmin.cs tests/Mdex.Tokens.Tests/TokenAdminTests.cs
git commit -m "Add token path resolver and admin operations"
```

---

## Task 8: `mdex-mcpctl` console

**Files:**
- Create: `src/Mdex.Mcpctl/Mdex.Mcpctl.csproj`, `src/Mdex.Mcpctl/Program.cs`

- [ ] **Step 1: Create the project**

```bash
dotnet new console -n Mdex.Mcpctl -o src/Mdex.Mcpctl -f net10.0
rm src/Mdex.Mcpctl/Program.cs
dotnet add src/Mdex.Mcpctl reference src/Mdex.Tokens
dotnet sln add src/Mdex.Mcpctl
```

- [ ] **Step 2: Set csproj metadata** (`src/Mdex.Mcpctl/Mdex.Mcpctl.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Mcpctl</RootNamespace>
    <AssemblyName>mdex-mcpctl</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Mdex.Tokens\Mdex.Tokens.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Implement `Program.cs`**

```csharp
// src/Mdex.Mcpctl/Program.cs
using Mdex.Tokens;

var store = new TokenStore(TokenPaths.Default());
var admin = new TokenAdmin(store);

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("mdex-mcpctl token new --role <read|write-intent|fullwrite> --label <name>");
    Console.WriteLine("mdex-mcpctl token list");
    Console.WriteLine("mdex-mcpctl token revoke <label>");
    Console.WriteLine();
    Console.WriteLine($"Token store: {TokenPaths.Default()}");
    return 0;
}

try
{
    if (args is ["token", "new", ..])
    {
        var role = ParseRole(FlagValue(args, "--role"));
        var label = FlagValue(args, "--label") ?? throw new AppError("token new needs --label <name>");
        var raw = admin.New(role, label);
        Console.WriteLine($"Token created for '{label}' (role {Roles.ToWire(role)}). Shown once — store it now:");
        Console.WriteLine();
        Console.WriteLine($"    {raw}");
        Console.WriteLine();
        Console.WriteLine("The server picks up the new token automatically (file watch); no restart needed.");
        return 0;
    }

    if (args is ["token", "list"])
    {
        var entries = admin.List();
        if (entries.Count == 0) { Console.WriteLine("No tokens."); return 0; }
        foreach (var e in entries)
            Console.WriteLine($"{e.Label,-20}  {e.Role,-12}  {e.Created}");
        return 0;
    }

    if (args is ["token", "revoke", var label])
    {
        if (!admin.Revoke(label)) throw new AppError($"no token labelled '{label}'");
        Console.WriteLine($"Revoked '{label}'. The server drops it automatically (file watch); no restart needed.");
        return 0;
    }

    throw new AppError($"unknown command: {string.Join(' ', args)}");
}
catch (AppError ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static string? FlagValue(string[] args, string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static Role ParseRole(string? wire)
{
    if (wire is null) throw new AppError("token new needs --role <read|write-intent|fullwrite>");
    if (!Roles.TryParse(wire, out var role)) throw new AppError($"invalid role '{wire}'");
    return role;
}

sealed class AppError(string message) : Exception(message);
```

- [ ] **Step 4: Smoke-test the round-trip against a temp store**

Run:
```bash
MDEX_MCP_TOKENS=/tmp/mcpctl-smoke.json dotnet run --project src/Mdex.Mcpctl -- token new --role read --label smoke
MDEX_MCP_TOKENS=/tmp/mcpctl-smoke.json dotnet run --project src/Mdex.Mcpctl -- token list
MDEX_MCP_TOKENS=/tmp/mcpctl-smoke.json dotnet run --project src/Mdex.Mcpctl -- token revoke smoke
rm -f /tmp/mcpctl-smoke.json
```
Expected: `new` prints a `mdex_…` token; `list` shows one `smoke  read  <date>` row; `revoke` prints "Revoked 'smoke'".

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Mcpctl Mdex.sln
git commit -m "Add mdex-mcpctl token admin CLI"
```

---

## Task 9: Scaffold `Mdex.Mcp` server + test project

**Files:**
- Create: `src/Mdex.Mcp/Mdex.Mcp.csproj`, `tests/Mdex.Mcp.Tests/Mdex.Mcp.Tests.csproj`

- [ ] **Step 1: Create the projects**

```bash
dotnet new web -n Mdex.Mcp -o src/Mdex.Mcp -f net10.0
dotnet new xunit -n Mdex.Mcp.Tests -o tests/Mdex.Mcp.Tests -f net10.0
rm tests/Mdex.Mcp.Tests/UnitTest1.cs
dotnet add src/Mdex.Mcp reference src/Mdex.Cli src/Mdex.Tokens
dotnet add src/Mdex.Mcp package ModelContextProtocol --prerelease
dotnet add src/Mdex.Mcp package ModelContextProtocol.AspNetCore --prerelease
dotnet add tests/Mdex.Mcp.Tests reference src/Mdex.Mcp src/Mdex.Tokens
dotnet add tests/Mdex.Mcp.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet sln add src/Mdex.Mcp tests/Mdex.Mcp.Tests
```

- [ ] **Step 2: Set server csproj metadata** (`src/Mdex.Mcp/Mdex.Mcp.csproj`)

Keep the generated `Sdk="Microsoft.NET.Sdk.Web"` and package references; ensure these properties:

```xml
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Mcp</RootNamespace>
    <AssemblyName>mdex-mcp</AssemblyName>
  </PropertyGroup>
```

- [ ] **Step 3: Verify it builds** (server still has the default template `Program.cs`)

Run: `dotnet build src/Mdex.Mcp`
Expected: PASS. If the MCP packages fail to restore, check the exact current package version on nuget.org and pin it.

- [ ] **Step 4: Commit**

```bash
git add src/Mdex.Mcp tests/Mdex.Mcp.Tests Mdex.sln
git commit -m "Scaffold Mdex.Mcp server + tests"
```

---

## Task 10: `MdexBackend` (in-process verb dispatch)

**Files:**
- Create: `src/Mdex.Mcp/MdexBackend.cs`
- Test: `tests/Mdex.Mcp.Tests/MdexBackendTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Mcp.Tests/MdexBackendTests.cs
using System.Text;
using Mdex.Mcp;
namespace Mdex.Mcp.Tests;

public class MdexBackendTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "mdex-mcp-" + Guid.NewGuid().ToString("N"));
    public MdexBackendTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zettel"));
        File.WriteAllText(Path.Combine(_root, "zettel", "a.md"),
            "---\ntype: zettel\n---\n\n# A\n\nHello world\n", new UTF8Encoding(false));
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Call_read_note_returns_data_json()
    {
        var backend = new MdexBackend(_root);
        var data = backend.Call("read-note", "a");
        Assert.Contains("Hello world", data);
        Assert.Contains("\"slug\": \"a\"", data);
    }

    [Fact]
    public void Call_failure_throws_with_cli_error_message()
    {
        var backend = new MdexBackend(_root);
        var ex = Assert.Throws<MdexToolException>(() => backend.Call("read-note", "missing"));
        Assert.Contains("note not found", ex.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Mcp.Tests --filter MdexBackendTests`
Expected: FAIL — types undefined.

- [ ] **Step 3: Implement**

```csharp
// src/Mdex.Mcp/MdexBackend.cs
using System.Text.Json;
using Mdex.Cli.Cli;

namespace Mdex.Mcp;

/// <summary>Thrown when a CLI verb returns an error envelope; carries the message and optional details.</summary>
public sealed class MdexToolException(string message, string? details) : Exception(message)
{
    public string? Details { get; } = details;
}

/// <summary>
/// Runs mdext verbs in-process via <see cref="CliDispatcher"/> and returns the <c>data</c> JSON.
/// On an error envelope it throws <see cref="MdexToolException"/>. One code path with the CLI.
/// </summary>
public sealed class MdexBackend(string vaultRoot)
{
    public string Call(string verb, params string[] args)
    {
        var json = CliDispatcher.RunToJson(verb, args, vaultRoot);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("ok").GetBoolean())
            return root.GetProperty("data").GetRawText();

        var error = root.GetProperty("error").GetString() ?? "error";
        var details = root.TryGetProperty("details", out var d) && d.ValueKind != JsonValueKind.Null
            ? d.GetRawText() : null;
        throw new MdexToolException(error, details);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mdex.Mcp.Tests --filter MdexBackendTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Mcp/MdexBackend.cs tests/Mdex.Mcp.Tests/MdexBackendTests.cs
git commit -m "Add in-process MdexBackend over CliDispatcher"
```

---

## Task 11: `ToolAuthorizer` (per-tool role gate)

**Files:**
- Create: `src/Mdex.Mcp/Auth/ToolAuthorizer.cs`
- Test: `tests/Mdex.Mcp.Tests/ToolAuthorizerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Mcp.Tests/ToolAuthorizerTests.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Mdex.Mcp.Auth;
using Mdex.Tokens;
namespace Mdex.Mcp.Tests;

public class ToolAuthorizerTests
{
    static ToolAuthorizer WithRole(Role? role)
    {
        var http = new HttpContextAccessor();
        var ctx = new DefaultHttpContext();
        if (role is not null)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ToolAuthorizer.RoleClaim, Roles.ToWire(role.Value)) }, "Bearer"));
        http.HttpContext = ctx;
        return new ToolAuthorizer(http);
    }

    [Fact]
    public void Allows_when_role_meets_minimum()
    {
        WithRole(Role.FullWrite).Require(Role.WriteIntent);   // no throw
        WithRole(Role.Read).Require(Role.Read);               // no throw
    }

    [Fact]
    public void Blocks_when_role_below_minimum()
    {
        Assert.Throws<MdexToolException>(() => WithRole(Role.WriteIntent).Require(Role.FullWrite));
        Assert.Throws<MdexToolException>(() => WithRole(Role.Read).Require(Role.WriteIntent));
    }

    [Fact]
    public void Blocks_when_no_role_claim()
    {
        Assert.Throws<MdexToolException>(() => WithRole(null).Require(Role.Read));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Mcp.Tests --filter ToolAuthorizerTests`
Expected: FAIL — `ToolAuthorizer` undefined.

- [ ] **Step 3: Implement**

```csharp
// src/Mdex.Mcp/Auth/ToolAuthorizer.cs
using Microsoft.AspNetCore.Http;
using Mdex.Tokens;

namespace Mdex.Mcp.Auth;

/// <summary>Reads the caller's role from the authenticated principal and enforces a per-tool minimum.</summary>
public sealed class ToolAuthorizer(IHttpContextAccessor http)
{
    public const string RoleClaim = "mdex-role";

    public void Require(Role minimum)
    {
        var wire = http.HttpContext?.User.FindFirst(RoleClaim)?.Value;
        if (wire is null || !Roles.TryParse(wire, out var role) || role < minimum)
            throw new MdexToolException($"this tool requires the '{Roles.ToWire(minimum)}' role", null);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mdex.Mcp.Tests --filter ToolAuthorizerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Mcp/Auth/ToolAuthorizer.cs tests/Mdex.Mcp.Tests/ToolAuthorizerTests.cs
git commit -m "Add per-tool role authorizer"
```

---

## Task 12: `BearerAuthHandler` (bearer token → role claim)

**Files:**
- Create: `src/Mdex.Mcp/Auth/BearerAuthHandler.cs`

This handler has no standalone unit test (it is exercised by the integration tests in Task 16). It is a thin ASP.NET `AuthenticationHandler` over `TokenStore`.

- [ ] **Step 1: Implement**

```csharp
// src/Mdex.Mcp/Auth/BearerAuthHandler.cs
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mdex.Tokens;

namespace Mdex.Mcp.Auth;

public sealed class BearerAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";
    readonly TokenStore _store;

    public BearerAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, TokenStore store)
        : base(options, logger, encoder)
    {
        _store = store;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("missing bearer token"));

        var token = header["Bearer ".Length..].Trim();
        var role = _store.FindRole(token);
        if (role is null)
            return Task.FromResult(AuthenticateResult.Fail("invalid token"));

        var identity = new ClaimsIdentity(
            new[] { new Claim(ToolAuthorizer.RoleClaim, Roles.ToWire(role.Value)) }, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Mdex.Mcp`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/Mdex.Mcp/Auth/BearerAuthHandler.cs
git commit -m "Add bearer-token authentication handler"
```

---

## Task 13: Read tools (9)

**Files:**
- Create: `src/Mdex.Mcp/Tools/ReadTools.cs`

All tools are static methods that receive `MdexBackend` and `ToolAuthorizer` from DI plus their typed parameters, enforce the role, and forward to a CLI verb. No test here (covered by Task 16 integration + the per-verb behaviour already tested in `Mdex.Cli.Tests`).

- [ ] **Step 1: Implement**

```csharp
// src/Mdex.Mcp/Tools/ReadTools.cs
using System.ComponentModel;
using ModelContextProtocol.Server;
using Mdex.Mcp.Auth;
using Mdex.Tokens;

namespace Mdex.Mcp.Tools;

[McpServerToolType]
public static class ReadTools
{
    [McpServerTool, Description("Full-text substring search over note titles and bodies; one hit per note.")]
    public static string Search(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Search query")] string query)
    {
        auth.Require(Role.Read);
        return mdex.Call("search", "--", query);   // -- guards a query that starts with --
    }

    [McpServerTool, Description("Return a note's frontmatter and body by slug or vault-relative path.")]
    public static string ReadNote(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath)
    {
        auth.Require(Role.Read);
        return mdex.Call("read-note", "--", slugOrPath);
    }

    [McpServerTool, Description("Notes carrying all of the given tags.")]
    public static string GetByTag(MdexBackend mdex, ToolAuthorizer auth,
        [Description("One or more tag names")] string[] tags)
    {
        auth.Require(Role.Read);
        var args = new List<string> { "--" };
        args.AddRange(tags);
        return mdex.Call("get-by-tag", args.ToArray());
    }

    [McpServerTool, Description("Notes whose frontmatter type matches (inbox|zettel|project|area|reference).")]
    public static string GetByType(MdexBackend mdex, ToolAuthorizer auth,
        [Description("BRAID type")] string type)
    {
        auth.Require(Role.Read);
        return mdex.Call("get-by-type", "--", type);
    }

    [McpServerTool, Description("Notes in a created/modified date range. One bound may be omitted (open-ended).")]
    public static string GetByDateRange(MdexBackend mdex, ToolAuthorizer auth,
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

    [McpServerTool, Description("All tags with their counts.")]
    public static string ListTags(MdexBackend mdex, ToolAuthorizer auth)
    {
        auth.Require(Role.Read);
        return mdex.Call("list-tags");
    }

    [McpServerTool, Description("Tags containing the given substring.")]
    public static string SearchTags(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Substring to match against tag names")] string pattern)
    {
        auth.Require(Role.Read);
        return mdex.Call("search-tags", "--", pattern);
    }

    [McpServerTool, Description("Recursive file/dir tree under an optional vault-relative subpath.")]
    public static string List(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative subpath, or null for the whole vault")] string? path)
    {
        auth.Require(Role.Read);
        return path is null ? mdex.Call("list") : mdex.Call("list", "--", path);
    }

    [McpServerTool, Description("Notes linked via [[wikilinks]] in both directions; depth 1 by default.")]
    public static string FindRelated(MdexBackend mdex, ToolAuthorizer auth,
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

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Mdex.Mcp`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/Mdex.Mcp/Tools/ReadTools.cs
git commit -m "Add 9 read MCP tools"
```

---

## Task 14: Intent tools (5)

**Files:**
- Create: `src/Mdex.Mcp/Tools/IntentTools.cs`

- [ ] **Step 1: Implement**

```csharp
// src/Mdex.Mcp/Tools/IntentTools.cs
using System.ComponentModel;
using ModelContextProtocol.Server;
using Mdex.Mcp.Auth;
using Mdex.Tokens;

namespace Mdex.Mcp.Tools;

[McpServerToolType]
public static class IntentTools
{
    [McpServerTool, Description("Append a new timestamped inbox entry (inbox/YYYYMMDD-NN.md).")]
    public static string CaptureInbox(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Entry text")] string content)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("capture-inbox", $"--content={content}");
    }

    [McpServerTool, Description("Create a typed note (zettel|project|area|reference) with frontmatter + template.")]
    public static string CreateNote(MdexBackend mdex, ToolAuthorizer auth,
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

    [McpServerTool, Description("Append a section to a note (by slug or path) and bump its modified date.")]
    public static string AppendToNote(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Note slug or vault-relative path")] string slugOrPath,
        [Description("Markdown to append")] string content)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("append-to-note", $"--content={content}", "--", slugOrPath);
    }

    [McpServerTool, Description("Add a dated bullet under a note's ## Log section (creating it if needed).")]
    public static string AppendToLog(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Project note slug or vault-relative path")] string slugOrPath,
        [Description("Log entry text (becomes a bullet)")] string content)
    {
        auth.Require(Role.WriteIntent);
        return mdex.Call("append-to-log", $"--content={content}", "--", slugOrPath);
    }

    [McpServerTool, Description("Move an inbox item into a typed folder, rebuilding its frontmatter.")]
    public static string PromoteInboxItem(MdexBackend mdex, ToolAuthorizer auth,
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

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Mdex.Mcp`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/Mdex.Mcp/Tools/IntentTools.cs
git commit -m "Add 5 intent MCP tools"
```

---

## Task 15: Raw FS tools (5)

**Files:**
- Create: `src/Mdex.Mcp/Tools/RawFsTools.cs`

- [ ] **Step 1: Implement**

```csharp
// src/Mdex.Mcp/Tools/RawFsTools.cs
using System.ComponentModel;
using ModelContextProtocol.Server;
using Mdex.Mcp.Auth;
using Mdex.Tokens;

namespace Mdex.Mcp.Tools;

[McpServerToolType]
public static class RawFsTools
{
    [McpServerTool, Description("Create or overwrite a file at a vault-relative path.")]
    public static string WriteFile(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path,
        [Description("Full file content")] string content)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("write-file", $"--content={content}", "--", path);
    }

    [McpServerTool, Description("Append raw text to a file at a vault-relative path (creates it if missing).")]
    public static string AppendFile(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path,
        [Description("Text to append")] string content)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("append-file", $"--content={content}", "--", path);
    }

    [McpServerTool, Description("Move or rename a file/directory within the vault (no overwrite).")]
    public static string Move(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Source vault-relative path")] string from,
        [Description("Destination vault-relative path")] string to)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("move", "--", from, to);
    }

    [McpServerTool, Description("Create a directory at a vault-relative path.")]
    public static string Mkdir(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("mkdir", "--", path);
    }

    [McpServerTool, Description("Delete a file or directory (recursive); revertible via git history.")]
    public static string Delete(MdexBackend mdex, ToolAuthorizer auth,
        [Description("Vault-relative path")] string path)
    {
        auth.Require(Role.FullWrite);
        return mdex.Call("delete", "--", path);
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Mdex.Mcp`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/Mdex.Mcp/Tools/RawFsTools.cs
git commit -m "Add 5 raw-fs MCP tools"
```

---

## Task 15B: Default-deny guard — `[MinRole]` + reflection invariant (D-2)

The per-method `auth.Require(...)` calls in Tasks 13–15 are *opt-in*: a future tool that forgets the call is silently open to any authenticated token (Phase-2 D-2). This task makes "every tool declares and enforces a minimum role" a **CI invariant**: each tool carries a machine-readable `[MinRole]`, and a reflection test fails the build if any `[McpServerTool]` lacks it or lacks the `ToolAuthorizer` it needs to gate. The attribute is also the single declared source a central invocation filter can read later (see the follow-up note).

**Files:**
- Create: `src/Mdex.Mcp/Auth/MinRoleAttribute.cs`
- Modify: `src/Mdex.Mcp/Tools/ReadTools.cs`, `IntentTools.cs`, `RawFsTools.cs` (annotate each tool)
- Test: `tests/Mdex.Mcp.Tests/ToolGateGuardTests.cs`

- [ ] **Step 1: Add the attribute**

```csharp
// src/Mdex.Mcp/Auth/MinRoleAttribute.cs
using Mdex.Tokens;

namespace Mdex.Mcp.Auth;

/// <summary>Declares the minimum role a tool requires. Presence is enforced by ToolGateGuardTests,
/// so a tool without it fails the build (default-deny). Mirrors the in-method auth.Require(...) call.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MinRoleAttribute(Role minimum) : Attribute
{
    public Role Minimum { get; } = minimum;
}
```

- [ ] **Step 2: Annotate every tool method**

Add the attribute directly above each `[McpServerTool]` (the role mirrors the existing `auth.Require(...)` first line), per group:
- `Tools/ReadTools.cs` — every method: `[McpServerTool, MinRole(Role.Read)]`
- `Tools/IntentTools.cs` — every method: `[McpServerTool, MinRole(Role.WriteIntent)]`
- `Tools/RawFsTools.cs` — every method: `[McpServerTool, MinRole(Role.FullWrite)]`

Each file already has `using Mdex.Tokens;`; ensure `using Mdex.Mcp.Auth;` is present (ReadTools/IntentTools/RawFsTools already import it for `ToolAuthorizer`).

- [ ] **Step 3: Write the guard test**

```csharp
// tests/Mdex.Mcp.Tests/ToolGateGuardTests.cs
using System.Reflection;
using ModelContextProtocol.Server;
using Mdex.Mcp.Auth;
namespace Mdex.Mcp.Tests;

public class ToolGateGuardTests
{
    static IEnumerable<MethodInfo> Tools() =>
        typeof(MdexBackend).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    [Fact]
    public void There_are_tools_to_check()
        => Assert.Equal(19, Tools().Count());   // 9 read + 5 intent + 5 raw-fs

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
}
```

- [ ] **Step 4: Run the guard test**

Run: `dotnet test tests/Mdex.Mcp.Tests --filter ToolGateGuardTests`
Expected: PASS once every tool is annotated. If `There_are_tools_to_check` reports a different count, reconcile against the 14-tool spec (note: `get_by_date_range` etc. count once each; the total method count is 19 across the three groups).

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Mcp/Auth/MinRoleAttribute.cs src/Mdex.Mcp/Tools tests/Mdex.Mcp.Tests/ToolGateGuardTests.cs
git commit -m "Enforce default-deny: every MCP tool must declare [MinRole]"
```

> **Follow-up (stronger enforcement, optional):** Once the installed MCP SDK's tool-invocation
> hook is confirmed (an `IMcpServer*Filter`/middleware that wraps each call), move enforcement into a
> single filter that reads `[MinRole]` and calls `ToolAuthorizer.Require` centrally, then delete the
> per-method `auth.Require(...)` lines. The attribute and guard test already in place make that a
> mechanical change; until then, the guard test is the default-deny invariant.

---

## Task 16: Server host wiring + config

**Files:**
- Modify: `src/Mdex.Mcp/Program.cs` (replace template)
- Create: `src/Mdex.Mcp/appsettings.json`

- [ ] **Step 1: Replace `Program.cs`**

```csharp
// src/Mdex.Mcp/Program.cs
using Mdex.Mcp;
using Mdex.Mcp.Auth;
using Mdex.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- configuration: vault root + token store path (env wins over appsettings) ---
var vaultRoot = builder.Configuration["Mdex:Root"]
    ?? Environment.GetEnvironmentVariable("MDEX_ROOT")
    ?? throw new InvalidOperationException("Set Mdex:Root in config or MDEX_ROOT env var.");
var tokenPath = builder.Configuration["Mdex:TokenStore"] ?? TokenPaths.Default();

// --- shared singletons: token store (+ file watch for hot-reload) and the backend ---
var tokenStore = new TokenStore(tokenPath);
builder.Services.AddSingleton(tokenStore);
builder.Services.AddSingleton(new TokenStoreWatcher(tokenStore, tokenPath));
builder.Services.AddSingleton(new MdexBackend(vaultRoot));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ToolAuthorizer>();

// --- auth: bearer token → role claim ---
builder.Services.AddAuthentication(BearerAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BearerAuthHandler>(
        BearerAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

// --- MCP server: both transports (Streamable HTTP default + legacy SSE), tools from this assembly ---
builder.Services.AddMcpServer()
    .WithHttpTransport(options => { options.EnableLegacySse = true; })
    .WithToolsFromAssembly();

var app = builder.Build();

// Touch the watcher singleton so file-watching starts at boot.
app.Services.GetRequiredService<TokenStoreWatcher>();

app.UseAuthentication();
app.UseAuthorization();
app.MapMcp().RequireAuthorization();

app.Run();

public partial class Program { }   // expose entry point to WebApplicationFactory in tests
```

> **SDK verification:** `WithHttpTransport` takes an `Action<HttpServerTransportOptions>`; `EnableLegacySse` is the documented switch for the legacy `/sse` + `/message` endpoints. If the installed SDK version names this property differently, inspect `HttpServerTransportOptions` members and adjust this one line.

- [ ] **Step 2: Write `appsettings.json`**

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://127.0.0.1:3001" } } },
  "Mdex": {
    "Root": "",
    "TokenStore": ""
  }
}
```

> Empty `Mdex:Root`/`Mdex:TokenStore` mean "fall back to env / default path". On the server, `MDEX_ROOT` is set by systemd (Task 18) and `TokenStore` defaults to `~/.config/mdex-mcp/tokens.json`.

> **Bind invariant (Phase-2 D-7):** Kestrel binds `127.0.0.1` only — never a public interface without the TLS reverse-proxy in front. A bearer token over plain HTTP on a public interface would travel in clear text. The loopback hop (proxy → Kestrel) is the only place plain HTTP is acceptable.

- [ ] **Step 3: Build**

Run: `dotnet build src/Mdex.Mcp`
Expected: PASS.

- [ ] **Step 4: Smoke-test the server boots and rejects anonymous calls**

Run:
```bash
MDEX_ROOT=$(mktemp -d) dotnet run --project src/Mdex.Mcp &
sleep 3
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://127.0.0.1:3001/ -H "Content-Type: application/json" -d '{}'
kill %1
```
Expected: prints `401`.

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Mcp/Program.cs src/Mdex.Mcp/appsettings.json
git commit -m "Wire MCP server host: auth, tools, both transports, hot-reload"
```

---

## Task 17: Integration tests (auth gate end-to-end)

**Files:**
- Create: `tests/Mdex.Mcp.Tests/AuthIntegrationTests.cs`

Uses `WebApplicationFactory<Program>` to boot the real host with a temp vault and a temp token store seeded with a known token, then checks the HTTP-level auth gate (401 vs accepted). Tool-level role enforcement is already covered by `ToolAuthorizerTests` (Task 11).

- [ ] **Step 1: Write the test**

```csharp
// tests/Mdex.Mcp.Tests/AuthIntegrationTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Mdex.Tokens;
namespace Mdex.Mcp.Tests;

public class AuthIntegrationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "mdex-mcp-it-" + Guid.NewGuid().ToString("N"));
    readonly string _tokens = Path.Combine(Path.GetTempPath(), "mdex-mcp-it-" + Guid.NewGuid().ToString("N") + ".json");
    const string RawToken = "mdex_integration";

    public AuthIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        new TokenStore(_tokens).Add(new TokenEntry(
            TokenHasher.Hash(RawToken), Roles.ToWire(Role.Read), "it", "2026-06-04"));
    }
    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { File.Delete(_tokens); } catch { }
    }

    WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Mdex:Root", _root).UseSetting("Mdex:TokenStore", _tokens));

    static HttpRequestMessage McpPost(string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", Encoding.UTF8, "application/json"),
        };
        if (token is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task No_token_is_rejected_401()
    {
        using var client = Factory().CreateClient();
        var res = await client.SendAsync(McpPost(null));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_token_is_rejected_401()
    {
        using var client = Factory().CreateClient();
        var res = await client.SendAsync(McpPost("mdex_wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Valid_token_is_accepted_not_401()
    {
        using var client = Factory().CreateClient();
        var res = await client.SendAsync(McpPost(RawToken));
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/Mdex.Mcp.Tests --filter AuthIntegrationTests`
Expected: PASS. If "valid token" still returns 401, confirm `Mdex:TokenStore` is honoured in `Program.cs` and the seeded hash matches `TokenHasher.Hash(RawToken)`.

> **Note on the MCP endpoint path:** `MapMcp()` maps at the app root by default; the POST target above is `/`. If a future SDK version maps under `/mcp`, update the request path in `McpPost` accordingly. The assertions (401 vs not-401) are path-correct as long as the request reaches the authorized MCP endpoint.

- [ ] **Step 3: Commit**

```bash
git add tests/Mdex.Mcp.Tests/AuthIntegrationTests.cs
git commit -m "Add auth-gate integration tests"
```

---

## Task 18: Deployment artifacts + docs

**Files:**
- Create: `deploy/mdex-mcp.service`
- Modify: `README.md`
- Modify: `docs/2026-06-04-mdex-mcp-design.md` (mark plan landed)

- [ ] **Step 1: systemd unit**

```ini
# deploy/mdex-mcp.service
[Unit]
Description=mdex MCP server
After=network.target

[Service]
Type=simple
User=mdadmin
Environment=MDEX_ROOT=/home/mdadmin/repos/mdex
Environment=ASPNETCORE_ENVIRONMENT=Production
ExecStart=/opt/mdex-mcp/mdex-mcp
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

- [ ] **Step 2: Add a README section** documenting the server + token admin. Append under the existing command docs:

```markdown
## mdex-mcp (remote MCP server)

`mdex-mcp` exposes the `mdext` operations as MCP tools over HTTP for AI clients.
It calls `mdext` in-process (same `--json` envelope) and gates every tool by role.

**Roles (ladder):** `read` ⊂ `write-intent` ⊂ `fullwrite`
- `read` — the 9 retrieval tools.
- `write-intent` — adds the 5 intent tools (capture/create/append/promote; non-destructive).
- `fullwrite` — adds the 5 raw-fs tools (write/append/move/mkdir/delete).

**Transports:** Streamable HTTP (default) and legacy SSE (`/sse` + `/message`).

**Config:** `MDEX_ROOT` (vault), `Mdex:TokenStore` or `MDEX_MCP_TOKENS`
(default `~/.config/mdex-mcp/tokens.json`), Kestrel URL in `appsettings.json`.
Run behind a TLS reverse proxy; the server binds `127.0.0.1` only — never a public
interface without the proxy (a bearer token over plain HTTP would be in clear text).

## mdex-mcpctl (token admin)

```
mdex-mcpctl token new --role <read|write-intent|fullwrite> --label <name>
mdex-mcpctl token list
mdex-mcpctl token revoke <label>
```

Only the SHA-256 hash is stored; the raw token is shown once. The server watches
the token file and picks up changes within ~1s — no restart needed.
```

- [ ] **Step 3: Mark the spec landed** — append to `docs/2026-06-04-mdex-mcp-design.md`:

```markdown

---

## Status (2026-06-04): implemented

Plan [2026-06-04-mdex-mcp-plan.md](2026-06-04-mdex-mcp-plan.md) executed. Source of
truth is the code + `mdex-mcpctl --help` + README. Both deviations from the
2026-06-01 design (in-process backend; three roles) are in effect.
```

- [ ] **Step 4: Full solution test run**

Run: `dotnet test`
Expected: PASS — all projects green.

- [ ] **Step 5: Commit**

```bash
git add deploy/mdex-mcp.service README.md docs/2026-06-04-mdex-mcp-design.md
git commit -m "Add deployment unit and document mdex-mcp + mdex-mcpctl"
```

---

## Self-Review notes (for the implementer)

- **Spec coverage:** A=Tasks 1,8,9; B=Task 10; C=Tasks 13–15; D(roles)=Task 3; D(store)=Tasks 5–7; D(auth)=Tasks 11–12,16; D(hot-reload)=Task 6; E=Task 8; F=Task 16; G=Task 18; H=Tasks 10,11,17 + per-verb behaviour in existing `Mdex.Cli.Tests`.
- **Security-review coverage (Phase 2):** threat-model + the mdext containment prerequisite = [2026-06-05-mdext-security-fixes-plan.md](2026-06-05-mdext-security-fixes-plan.md) (D-1); default-deny guard = Task 15B (D-2); token-file `0600`/`0700` + atomic write = Task 5 (D-3, D-6); sanitized dispatcher catch = Task 1 (D-4); root-target reject reaches MCP via the prerequisite (D-5); `127.0.0.1` bind = Task 16 (D-7); no-expiry tradeoff = documented in the spec threat-model (D-8).
- **Task 1B moved out:** `ArgReader` hardening now lives in the prerequisite plan (its Task 1), bundled with the vault-escape fixes. **Run the prerequisite plan before Task 1 here.**
- **Two known SDK soft spots, both flagged inline with a fallback instruction rather than a guess:** the `EnableLegacySse` property name (Task 16) and the `MapMcp` endpoint path (Task 17). A third, optional, is the central invocation filter (Task 15B follow-up). All are localized adjustments if the installed SDK differs.
- **Robust arg passing:** the prerequisite plan hardens `ArgReader` (`--key=value` + `--` end-of-options); Tasks 13–15 build args by the uniform rule (value-flags `--key=value`, booleans bare, then `--`, then positionals), so any content or positional — including values beginning with `--` — round-trips intact. The low-level `MdexBackend` tests (Task 10) pass plain positionals (no leading `--`), which need no separator.
```
