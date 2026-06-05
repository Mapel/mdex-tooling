# Plan C — `Mdex.Tokens` + `mdex-token` + `mdex-gateway` (remote HTTP MCP host) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the server-only pieces — the hashed token store `Mdex.Tokens`, the token-admin CLI `mdex-token`, and the remote HTTP MCP host `mdex-gateway` (ASP.NET Core) — so off-machine clients (phone, web, Langdock) reach the **same** tool surface as the local host, gated by a Bearer token that maps to a role.

**Architecture:** `mdex-gateway` is the **sibling** of `mdex-mcp` (design §2, B2): it hosts the **same** `Mdex.Mcp.Tools` in-process over HTTP via `MapMcp().RequireAuthorization()`, not a bridge to the stdio host. A `BearerAuthHandler` turns a token into a `mdex-role` claim; an `HttpRoleProvider : IRoleProvider` (the gateway's adapter for the seam Plan B put in `Mdex.Core`) feeds that role to the shared `ToolAuthorizer`. `Mdex.Tokens` owns `tokens.json` (only SHA-256 hashes, atomic write, `0600`/`0700`, fixed-time compare, file-watch hot-reload); `mdex-token` writes it and the gateway watches it. Roles are reused from `Mdex.Core.Security` — `Mdex.Tokens` references **only** `Mdex.Core` (no MCP SDK), keeping the token admin lean.

**Tech Stack:** .NET 10 · C# · xUnit · `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (MCP C# SDK, Streamable HTTP) · ASP.NET Core / Kestrel · `System.Security.Cryptography` (SHA-256, RNG, `FixedTimeEquals`) · `Microsoft.AspNetCore.Mvc.Testing`. Spec: [2026-06-05-mdex-mcp-local-remote-design.md](2026-06-05-mdex-mcp-local-remote-design.md) §2, §4, §6, §8.

> **Plan C of 3.** Prerequisites: **Plan A** ([core extraction](2026-06-05-mdex-core-extraction-plan.md)) and **Plan B** ([tools + local host](2026-06-05-mdex-mcp-local-host-plan.md)) have landed. From Plan B this plan reuses, unchanged: `Mdex.Core.Security` (`Role`, `Roles`, `IRoleProvider`, `ToolAuthorizer`, `MdexToolException`), `Mdex.Mcp.Tools` (the 19 `[McpServerTool]` wrappers + `MdexBackend` with its D-4 sanitisation), and the `[MinRole]` default-deny reflection guard (already enforced against the shared lib — **not** repeated here).

> **What changed vs. the superseded Plan-2** ([2026-06-04-mdex-mcp-plan.md](2026-06-04-mdex-mcp-plan.md), source material): names (`mdex-mcpctl`→`mdex-token`, the remote `mdex-mcp`→`mdex-gateway`); `Role` moved from `Mdex.Tokens` into `Mdex.Core.Security`; the old ASP.NET-coupled `ToolAuthorizer` is replaced by the Core `ToolAuthorizer` + a gateway `HttpRoleProvider`; tools are referenced from `Mdex.Mcp.Tools`, not redefined; the `CliDispatcher.RunToJson` envelope is gone (the gateway uses `MdexBackend`); token-store path is `~/.config/mdex-gateway/tokens.json`.

---

## Conventions for every task

- Set `$REPO` at the **start of every shell call**: `$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"`.
- Build: `dotnet build "$REPO\Mdex.sln"`. Test: `dotnet test "$REPO\Mdex.sln"` (`--filter <Name>` to scope).
- Commit after each task (`git -C $REPO add -A; git -C $REPO commit -m "…"`).
- Do **not** use `cd` in compound commands.

> **Security gates carried by this plan (Phase-2-Delta + Phase-3, see [security plan](2026-06-05-mdex-security-review-plan.md)):**
> - **Plan-phase 3 (`Mdex.Tokens`):** `0600`/`0700` perms + atomic write (D-3/D-6) — **Task 3**; fixed-time hash compare (Phase-3 constant-time) — **Task 3**.
> - **Plan-phase 4 (`mdex-gateway`):** absorbs Plan-2 auth + hot-reload; D-4 is inherited from `MdexBackend` (Plan B); D-7 (`127.0.0.1`-only bind behind TLS proxy) — **Task 9**.
> - **Phase-3 code-check:** header-parsing edge-cases + corrupt-file hot-reload fail-closed → `401` — **Task 10**; no token in logs — **Task 8**; path-traversal through the tools — inherited from Plan B Task 7 (shared tools).

> **MCP SDK soft-spots (verify against the installed package):** `WithHttpTransport`'s `EnableLegacySse` option name, and the path `MapMcp()` maps to (`/` by default). Both flagged inline with fallbacks.

---

## File Structure (end state of this plan)

```
src/
  Mdex.Tokens/                       NEW classlib — token store (refs Mdex.Core for Role; NO MCP SDK)
    Mdex.Tokens.csproj
    TokenGenerator.cs                mdex_<base64url(32B)>
    TokenHasher.cs                   SHA-256 hex
    TokenEntry.cs                    record { Hash, Role(wire), Label, Created }
    TokenStore.cs                    load/save/add/remove/find + reload; atomic write; 0600/0700; fixed-time compare
    TokenStoreWatcher.cs             debounced FileSystemWatcher → store.Reload()
    TokenPaths.cs                    default ~/.config/mdex-gateway/tokens.json
    TokenAdmin.cs                    new/list/revoke
  Mdex.Token/                        NEW console → mdex-token
    Mdex.Token.csproj
    Program.cs
  Mdex.Gateway/                      NEW ASP.NET → mdex-gateway (refs Mdex.Mcp.Tools + Mdex.Tokens)
    Mdex.Gateway.csproj
    Auth/HttpRoleProvider.cs         IRoleProvider over the authenticated role claim
    Auth/BearerAuthHandler.cs        bearer token → role claim (no token logged)
    Program.cs                       host wiring + config
    appsettings.json
  deploy/mdex-gateway.service        NEW systemd unit
tests/
  Mdex.Tokens.Tests/                 NEW — crypto, store, reload, admin
  Mdex.Gateway.Tests/                NEW — auth gate, header edge-cases, corrupt-file fail-closed
```

---

## Task 1: Scaffold `Mdex.Tokens` (+ test project)

**Files:**
- Create: `src/Mdex.Tokens/Mdex.Tokens.csproj`, `tests/Mdex.Tokens.Tests/Mdex.Tokens.Tests.csproj`
- Modify: `Mdex.sln`

- [ ] **Step 1: Create the projects + references**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
dotnet new classlib -n Mdex.Tokens -o "$REPO\src\Mdex.Tokens"
Remove-Item "$REPO\src\Mdex.Tokens\Class1.cs"
dotnet new xunit -n Mdex.Tokens.Tests -o "$REPO\tests\Mdex.Tokens.Tests"
Remove-Item "$REPO\tests\Mdex.Tokens.Tests\UnitTest1.cs"
dotnet add "$REPO\src\Mdex.Tokens" reference "$REPO\src\Mdex.Core\Mdex.Core.csproj"
dotnet add "$REPO\tests\Mdex.Tokens.Tests" reference "$REPO\src\Mdex.Tokens\Mdex.Tokens.csproj"
dotnet add "$REPO\tests\Mdex.Tokens.Tests" reference "$REPO\src\Mdex.Core\Mdex.Core.csproj"
dotnet sln "$REPO\Mdex.sln" add "$REPO\src\Mdex.Tokens\Mdex.Tokens.csproj"
dotnet sln "$REPO\Mdex.sln" add "$REPO\tests\Mdex.Tokens.Tests\Mdex.Tokens.Tests.csproj"
```

- [ ] **Step 2: Set `src/Mdex.Tokens/Mdex.Tokens.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Tokens</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Mdex.Core\Mdex.Core.csproj" />
  </ItemGroup>
</Project>
```

> `Mdex.Tokens` references `Mdex.Core` **only** for the `Role`/`Roles` type — no MCP SDK, no ASP.NET. This is why `Role` lives in `Mdex.Core.Security` and not in `Mdex.Mcp.Tools` (Plan B Task 1): the token admin stays a lean console.

- [ ] **Step 3: Build** — Run: `dotnet build "$REPO\Mdex.sln"` → Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "chore: scaffold Mdex.Tokens library + tests"
```

---

## Task 2: Token generation + hashing

**Files:**
- Create: `src/Mdex.Tokens/TokenGenerator.cs`, `src/Mdex.Tokens/TokenHasher.cs`
- Test: `tests/Mdex.Tokens.Tests/TokenCryptoTests.cs`

- [ ] **Step 1: Write the failing test** — `tests/Mdex.Tokens.Tests/TokenCryptoTests.cs`:

```csharp
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

- [ ] **Step 2: Run → expect FAIL** — Run: `dotnet test "$REPO\Mdex.sln" --filter TokenCryptoTests` → FAIL (types undefined).

- [ ] **Step 3: Implement**

`src/Mdex.Tokens/TokenGenerator.cs`:
```csharp
using System.Security.Cryptography;

namespace Mdex.Tokens;

public static class TokenGenerator
{
    /// <summary>A fresh opaque token: <c>mdex_</c> + url-safe base64 of 32 random bytes (256-bit entropy).</summary>
    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "mdex_" + Base64Url(bytes);
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

`src/Mdex.Tokens/TokenHasher.cs`:
```csharp
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

- [ ] **Step 4: Run → expect PASS** — Run: `dotnet test "$REPO\Mdex.sln" --filter TokenCryptoTests` → PASS.

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(tokens): token generation + SHA-256 hashing"
```

---

## Task 3: `TokenEntry` + `TokenStore` (atomic, 0600/0700, fixed-time compare)

**Files:**
- Create: `src/Mdex.Tokens/TokenEntry.cs`, `src/Mdex.Tokens/TokenStore.cs`
- Test: `tests/Mdex.Tokens.Tests/TokenStoreTests.cs`

- [ ] **Step 1: Write the failing test** — `tests/Mdex.Tokens.Tests/TokenStoreTests.cs`:

```csharp
using Mdex.Core.Security;
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
        store.Add(new TokenEntry(TokenHasher.Hash(raw), Roles.ToWire(Role.WriteIntent), "langdock", "2026-06-05"));
        Assert.Equal(Role.WriteIntent, store.FindRole(raw));
        Assert.Null(store.FindRole("mdex_wrong"));
    }

    [Fact]
    public void Add_persists_across_reopen_and_stores_only_hash()
    {
        var raw = "mdex_secret";
        new TokenStore(_path).Add(new TokenEntry(TokenHasher.Hash(raw), Roles.ToWire(Role.Read), "claude", "2026-06-05"));
        var text = File.ReadAllText(_path);
        Assert.DoesNotContain(raw, text);                 // raw token never written
        Assert.Equal(Role.Read, new TokenStore(_path).FindRole(raw));
    }

    [Fact]
    public void Remove_by_label_deletes_matching_entry()
    {
        var store = new TokenStore(_path);
        store.Add(new TokenEntry(TokenHasher.Hash("mdex_a"), Roles.ToWire(Role.Read), "a", "2026-06-05"));
        Assert.True(store.Remove("a"));
        Assert.False(store.Remove("a"));
        Assert.Null(store.FindRole("mdex_a"));
    }

    [Fact]
    public void Saved_file_is_owner_only_on_posix()   // D-3
    {
        if (OperatingSystem.IsWindows()) return;
        var store = new TokenStore(_path);
        store.Add(new TokenEntry(TokenHasher.Hash("mdex_x"), Roles.ToWire(Role.Read), "x", "2026-06-05"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_path)); // 0600
    }
}
```

- [ ] **Step 2: Run → expect FAIL** — Run: `dotnet test "$REPO\Mdex.sln" --filter TokenStoreTests` → FAIL (types undefined).

- [ ] **Step 3: Implement**

`src/Mdex.Tokens/TokenEntry.cs`:
```csharp
namespace Mdex.Tokens;

/// <summary>One stored credential. <see cref="Role"/> is the wire string ("read"/"write-intent"/"fullwrite").</summary>
public sealed record TokenEntry(string Hash, string Role, string Label, string Created);
```

`src/Mdex.Tokens/TokenStore.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mdex.Core.Security;

namespace Mdex.Tokens;

/// <summary>
/// In-memory snapshot of the on-disk token file. Stores only hashes. Mutations (Add/Remove) persist
/// immediately (atomic temp+rename, owner-only perms); <see cref="Reload"/> re-reads the file (the
/// gateway's watcher calls it). <see cref="FindRole"/> compares hashes in fixed time.
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
        // Fixed-time compare: defense-in-depth against timing attacks on the hash check. (Token entropy
        // is 256 bits so a timing oracle on the stored hash is of little practical use, but constant-time
        // comparison is cheap and removes the question.) All SHA-256 hex hashes are 64 chars → equal length.
        foreach (var e in _entries)
            if (FixedTimeEquals(e.Hash, hash) && Roles.TryParse(e.Role, out var role))
                return role;
        return null;
    }

    static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
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
        HardenPermissions(dir, isDirectory: true);            // 0700 (D-3)

        // Atomic: serialize to a temp file in the same dir, then rename over the target, so the
        // watching gateway never reads a half-written file (D-6).
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
            return new List<TokenEntry>();   // corrupt/partial file → treat as empty → fail-closed (all 401)
        }
    }
}
```

- [ ] **Step 4: Run → expect PASS** — Run: `dotnet test "$REPO\Mdex.sln" --filter TokenStoreTests` → PASS.

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(tokens): hashed token store — atomic write, 0600/0700, fixed-time compare"
```

---

## Task 4: Hot-reload — `Reload` contract test + `TokenStoreWatcher`

**Files:**
- Create: `src/Mdex.Tokens/TokenStoreWatcher.cs`
- Test: `tests/Mdex.Tokens.Tests/TokenReloadTests.cs`

- [ ] **Step 1: Write the test (Reload reflects external writes)** — `tests/Mdex.Tokens.Tests/TokenReloadTests.cs`:

```csharp
using Mdex.Core.Security;
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
        var admin = new TokenStore(_path);                 // a second handle = mdex-token
        admin.Add(new TokenEntry(TokenHasher.Hash("mdex_x"), Roles.ToWire(Role.Read), "x", "2026-06-05"));

        server.Reload();
        Assert.Equal(Role.Read, server.FindRole("mdex_x"));

        admin.Remove("x");                                 // revoke happens out-of-process
        server.Reload();
        Assert.Null(server.FindRole("mdex_x"));            // server no longer honours it
    }
}
```

- [ ] **Step 2: Run → expect PASS** — Run: `dotnet test "$REPO\Mdex.sln" --filter TokenReloadTests` → PASS immediately (Reload exists from Task 3; this locks the contract the watcher depends on).

- [ ] **Step 3: Implement the watcher** — `src/Mdex.Tokens/TokenStoreWatcher.cs`:

```csharp
namespace Mdex.Tokens;

/// <summary>
/// Watches the token file and calls <see cref="TokenStore.Reload"/> shortly after any change, so
/// <c>mdex-token</c> edits (e.g. revoke) take effect without a gateway restart. Debounced because a
/// single logical save can fire several FS events.
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

- [ ] **Step 4: Build** — Run: `dotnet build "$REPO\Mdex.sln"` → PASS (`System.Threading.Lock` is .NET 9+).

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(tokens): debounced token-store file watcher"
```

---

## Task 5: `TokenPaths` + `TokenAdmin`

**Files:**
- Create: `src/Mdex.Tokens/TokenPaths.cs`, `src/Mdex.Tokens/TokenAdmin.cs`
- Test: `tests/Mdex.Tokens.Tests/TokenAdminTests.cs`

- [ ] **Step 1: Write the failing test** — `tests/Mdex.Tokens.Tests/TokenAdminTests.cs`:

```csharp
using Mdex.Core.Security;
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

- [ ] **Step 2: Run → expect FAIL** — Run: `dotnet test "$REPO\Mdex.sln" --filter TokenAdminTests` → FAIL (types undefined).

- [ ] **Step 3: Implement**

`src/Mdex.Tokens/TokenPaths.cs`:
```csharp
namespace Mdex.Tokens;

public static class TokenPaths
{
    /// <summary>Default token-store location: env <c>MDEX_GATEWAY_TOKENS</c>, else per-OS config dir
    /// under <c>mdex-gateway</c> (the path follows the gateway binary name).</summary>
    public static string Default()
    {
        var env = Environment.GetEnvironmentVariable("MDEX_GATEWAY_TOKENS");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "mdex-gateway", "tokens.json");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "mdex-gateway", "tokens.json");
    }
}
```

`src/Mdex.Tokens/TokenAdmin.cs`:
```csharp
using Mdex.Core.Security;

namespace Mdex.Tokens;

/// <summary>Admin operations over a <see cref="TokenStore"/>. Used by <c>mdex-token</c>.</summary>
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

- [ ] **Step 4: Run → expect PASS** — Run: `dotnet test "$REPO\Mdex.sln" --filter TokenAdminTests` → PASS.

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(tokens): token path resolver + admin operations"
```

---

## Task 6: `mdex-token` console

**Files:**
- Create: `src/Mdex.Token/Mdex.Token.csproj`, `src/Mdex.Token/Program.cs`
- Modify: `Mdex.sln`

- [ ] **Step 1: Create the project**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
dotnet new console -n Mdex.Token -o "$REPO\src\Mdex.Token"
Remove-Item "$REPO\src\Mdex.Token\Program.cs"
dotnet add "$REPO\src\Mdex.Token" reference "$REPO\src\Mdex.Tokens\Mdex.Tokens.csproj"
dotnet add "$REPO\src\Mdex.Token" reference "$REPO\src\Mdex.Core\Mdex.Core.csproj"
dotnet sln "$REPO\Mdex.sln" add "$REPO\src\Mdex.Token\Mdex.Token.csproj"
```

- [ ] **Step 2: Set `src/Mdex.Token/Mdex.Token.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Token</RootNamespace>
    <AssemblyName>mdex-token</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Mdex.Tokens\Mdex.Tokens.csproj" />
    <ProjectReference Include="..\Mdex.Core\Mdex.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Implement `Program.cs`** — `src/Mdex.Token/Program.cs`:

```csharp
using Mdex.Core.Security;
using Mdex.Tokens;

var store = new TokenStore(TokenPaths.Default());
var admin = new TokenAdmin(store);

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("mdex-token new --role <read|write-intent|fullwrite> --label <name>");
    Console.WriteLine("mdex-token list");
    Console.WriteLine("mdex-token revoke <label>");
    Console.WriteLine();
    Console.WriteLine($"Token store: {TokenPaths.Default()}");
    return 0;
}

try
{
    if (args is ["new", ..])
    {
        var role = ParseRole(FlagValue(args, "--role"));
        var label = FlagValue(args, "--label") ?? throw new AppError("new needs --label <name>");
        var raw = admin.New(role, label);
        Console.WriteLine($"Token created for '{label}' (role {Roles.ToWire(role)}). Shown once — store it now:");
        Console.WriteLine();
        Console.WriteLine($"    {raw}");
        Console.WriteLine();
        Console.WriteLine("The gateway picks up the new token automatically (file watch); no restart needed.");
        return 0;
    }

    if (args is ["list"])
    {
        var entries = admin.List();
        if (entries.Count == 0) { Console.WriteLine("No tokens."); return 0; }
        foreach (var e in entries)
            Console.WriteLine($"{e.Label,-20}  {e.Role,-12}  {e.Created}");
        return 0;
    }

    if (args is ["revoke", var label])
    {
        if (!admin.Revoke(label)) throw new AppError($"no token labelled '{label}'");
        Console.WriteLine($"Revoked '{label}'. The gateway drops it automatically (file watch); no restart needed.");
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
    if (wire is null) throw new AppError("new needs --role <read|write-intent|fullwrite>");
    if (!Roles.TryParse(wire, out var role)) throw new AppError($"invalid role '{wire}'");
    return role;
}

sealed class AppError(string message) : Exception(message);
```

- [ ] **Step 4: Smoke-test the round-trip against a temp store**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
$env:MDEX_GATEWAY_TOKENS = Join-Path $env:TEMP "mdex-token-smoke.json"
dotnet run --project "$REPO\src\Mdex.Token" -- new --role read --label smoke
dotnet run --project "$REPO\src\Mdex.Token" -- list
dotnet run --project "$REPO\src\Mdex.Token" -- revoke smoke
Remove-Item $env:MDEX_GATEWAY_TOKENS -ErrorAction SilentlyContinue
Remove-Item Env:\MDEX_GATEWAY_TOKENS
```
Expected: `new` prints a `mdex_…` token; `list` shows one `smoke  read  <date>` row; `revoke` prints "Revoked 'smoke'".

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(token): add mdex-token admin CLI"
```

---

## Task 7: Scaffold `Mdex.Gateway` (+ test project)

**Files:**
- Create: `src/Mdex.Gateway/Mdex.Gateway.csproj`, `tests/Mdex.Gateway.Tests/Mdex.Gateway.Tests.csproj`
- Modify: `Mdex.sln`

- [ ] **Step 1: Create the projects + references**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
dotnet new web -n Mdex.Gateway -o "$REPO\src\Mdex.Gateway"
dotnet new xunit -n Mdex.Gateway.Tests -o "$REPO\tests\Mdex.Gateway.Tests"
Remove-Item "$REPO\tests\Mdex.Gateway.Tests\UnitTest1.cs"
dotnet add "$REPO\src\Mdex.Gateway" reference "$REPO\src\Mdex.Mcp.Tools\Mdex.Mcp.Tools.csproj"
dotnet add "$REPO\src\Mdex.Gateway" reference "$REPO\src\Mdex.Tokens\Mdex.Tokens.csproj"
dotnet add "$REPO\src\Mdex.Gateway" package ModelContextProtocol.AspNetCore --prerelease
dotnet add "$REPO\tests\Mdex.Gateway.Tests" reference "$REPO\src\Mdex.Gateway\Mdex.Gateway.csproj"
dotnet add "$REPO\tests\Mdex.Gateway.Tests" reference "$REPO\src\Mdex.Tokens\Mdex.Tokens.csproj"
dotnet add "$REPO\tests\Mdex.Gateway.Tests" reference "$REPO\src\Mdex.Core\Mdex.Core.csproj"
dotnet add "$REPO\tests\Mdex.Gateway.Tests" package Microsoft.AspNetCore.Mvc.Testing
dotnet sln "$REPO\Mdex.sln" add "$REPO\src\Mdex.Gateway\Mdex.Gateway.csproj"
dotnet sln "$REPO\Mdex.sln" add "$REPO\tests\Mdex.Gateway.Tests\Mdex.Gateway.Tests.csproj"
```

> `Mdex.Gateway` transitively gets the core MCP SDK + `Mdex.Core` via `Mdex.Mcp.Tools`; it adds `ModelContextProtocol.AspNetCore` for the HTTP transport. The 19 tools are **referenced**, not redefined — `Mdex.Gateway` defines no `[McpServerTool]`.

- [ ] **Step 2: Set server csproj metadata** — keep the generated `Sdk="Microsoft.NET.Sdk.Web"` and package references; ensure `src/Mdex.Gateway/Mdex.Gateway.csproj` has:

```xml
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Gateway</RootNamespace>
    <AssemblyName>mdex-gateway</AssemblyName>
  </PropertyGroup>
```

- [ ] **Step 3: Build** (still has the default template `Program.cs`) — Run: `dotnet build "$REPO\src\Mdex.Gateway"` → PASS. If `ModelContextProtocol.AspNetCore` fails to restore, pin the current version from nuget.org.

- [ ] **Step 4: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "chore: scaffold Mdex.Gateway server + tests"
```

---

## Task 8: `HttpRoleProvider` + `BearerAuthHandler`

The gateway's two auth pieces: `HttpRoleProvider` adapts the authenticated role claim to the `IRoleProvider` seam (so the shared `ToolAuthorizer` works unchanged), and `BearerAuthHandler` turns a bearer token into that claim. The handler **never logs the token** (Phase-3 token-leak check).

**Files:**
- Create: `src/Mdex.Gateway/Auth/HttpRoleProvider.cs`, `src/Mdex.Gateway/Auth/BearerAuthHandler.cs`
- Test: `tests/Mdex.Gateway.Tests/HttpRoleProviderTests.cs`

- [ ] **Step 1: Write the failing test** — `tests/Mdex.Gateway.Tests/HttpRoleProviderTests.cs`:

```csharp
using System.Security.Claims;
using Mdex.Core.Security;
using Mdex.Gateway.Auth;
using Microsoft.AspNetCore.Http;
namespace Mdex.Gateway.Tests;

public class HttpRoleProviderTests
{
    static HttpRoleProvider WithClaim(string? wire)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        if (wire is not null)
            accessor.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(HttpRoleProvider.RoleClaim, wire) }, "Bearer"));
        return new HttpRoleProvider(accessor);
    }

    [Fact]
    public void Reads_the_role_from_the_claim()
        => Assert.Equal(Role.WriteIntent, WithClaim("write-intent").Current);

    [Fact]
    public void Missing_claim_is_denied()
        => Assert.Throws<MdexToolException>(() => WithClaim(null).Current);

    [Fact]
    public void Garbage_claim_is_denied()
        => Assert.Throws<MdexToolException>(() => WithClaim("admin").Current);
}
```

- [ ] **Step 2: Run → expect FAIL** — Run: `dotnet test "$REPO\Mdex.sln" --filter HttpRoleProviderTests` → FAIL.

- [ ] **Step 3: Implement**

`src/Mdex.Gateway/Auth/HttpRoleProvider.cs`:
```csharp
using Mdex.Core.Security;
using Microsoft.AspNetCore.Http;

namespace Mdex.Gateway.Auth;

/// <summary>Feeds the shared <see cref="ToolAuthorizer"/> the caller's role from the authenticated
/// principal's claim (per request). Defense-in-depth: a missing/garbage claim is denied even though
/// <c>RequireAuthorization</c> already gates the endpoint.</summary>
public sealed class HttpRoleProvider(IHttpContextAccessor http) : IRoleProvider
{
    public const string RoleClaim = "mdex-role";

    public Role Current
    {
        get
        {
            var wire = http.HttpContext?.User.FindFirst(RoleClaim)?.Value;
            if (wire is null || !Roles.TryParse(wire, out var role))
                throw new MdexToolException("not authenticated");
            return role;
        }
    }
}
```

`src/Mdex.Gateway/Auth/BearerAuthHandler.cs`:
```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Mdex.Core.Security;
using Mdex.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mdex.Gateway.Auth;

/// <summary>Maps a <c>Bearer</c> token to a <see cref="HttpRoleProvider.RoleClaim"/>. The token value
/// is never logged; failures log only the reason. Lookup is fixed-time (see <see cref="TokenStore"/>).</summary>
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
        if (token.Length == 0)
            return Task.FromResult(AuthenticateResult.Fail("empty bearer token"));

        var role = _store.FindRole(token);
        if (role is null)
            return Task.FromResult(AuthenticateResult.Fail("invalid token"));   // token value not logged

        var identity = new ClaimsIdentity(
            new[] { new Claim(HttpRoleProvider.RoleClaim, Roles.ToWire(role.Value)) }, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

- [ ] **Step 4: Run → expect PASS** — Run: `dotnet test "$REPO\Mdex.sln" --filter HttpRoleProviderTests` → PASS.

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(gateway): bearer auth handler + HttpRoleProvider over the shared authorizer"
```

---

## Task 9: Gateway host wiring + config

**Files:**
- Modify: `src/Mdex.Gateway/Program.cs` (replace template)
- Create: `src/Mdex.Gateway/appsettings.json`

- [ ] **Step 1: Replace `Program.cs`** — `src/Mdex.Gateway/Program.cs`:

```csharp
using Mdex.Core.Cli;
using Mdex.Core.Security;
using Mdex.Gateway.Auth;
using Mdex.Mcp.Tools;
using Mdex.Mcp.Tools.Tools;
using Mdex.Tokens;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// --- configuration: vault root + token store path (env wins over appsettings) ---
var vaultRoot = builder.Configuration["Mdex:Root"]
    ?? Environment.GetEnvironmentVariable("MDEX_ROOT")
    ?? throw new InvalidOperationException("Set Mdex:Root in config or MDEX_ROOT env var.");
var tokenPath = builder.Configuration["Mdex:TokenStore"];
if (string.IsNullOrWhiteSpace(tokenPath)) tokenPath = TokenPaths.Default();

// --- shared singletons: engine, in-process backend (server vault root), token store + watcher ---
var engine = CommandEngine.Default();
var tokenStore = new TokenStore(tokenPath);

builder.Services.AddSingleton(engine);
builder.Services.AddSingleton(sp => new MdexBackend(engine, vaultRoot,
    msg => sp.GetRequiredService<ILoggerFactory>().CreateLogger("mdex").LogError("{Detail}", msg)));
builder.Services.AddSingleton(tokenStore);
builder.Services.AddSingleton(new TokenStoreWatcher(tokenStore, tokenPath));

// --- role seam: per-request role from the authenticated claim → shared ToolAuthorizer ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRoleProvider, HttpRoleProvider>();
builder.Services.AddScoped<ToolAuthorizer>();

// --- auth: bearer token → role claim ---
builder.Services.AddAuthentication(BearerAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BearerAuthHandler>(BearerAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

// --- MCP server over HTTP, tools from the SHARED Mdex.Mcp.Tools assembly ---
builder.Services.AddMcpServer()
    .WithHttpTransport(options => { options.EnableLegacySse = true; })
    .WithToolsFromAssembly(typeof(ReadTools).Assembly);

var app = builder.Build();

app.Services.GetRequiredService<TokenStoreWatcher>();   // touch the singleton so watching starts at boot

app.UseAuthentication();
app.UseAuthorization();
app.MapMcp().RequireAuthorization();

app.Run();

public partial class Program { }   // expose entry point to WebApplicationFactory in tests
```

> **SDK fallbacks:** if `EnableLegacySse` is renamed, inspect `HttpServerTransportOptions` and adjust that one line (or drop legacy SSE — Streamable HTTP is the default). If `WithToolsFromAssembly(Assembly)` has no assembly overload, register `.WithTools<ReadTools>().WithTools<IntentTools>().WithTools<RawFsTools>()`.

- [ ] **Step 2: Write `appsettings.json`** — `src/Mdex.Gateway/appsettings.json`:

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

> **Bind invariant (D-7):** Kestrel binds `127.0.0.1` only — never a public interface without the TLS reverse-proxy in front. A bearer token over plain HTTP on a public interface would travel in clear text. The loopback hop (proxy → Kestrel) is the only place plain HTTP is acceptable. Empty `Mdex:Root`/`Mdex:TokenStore` mean "fall back to env / default path".

- [ ] **Step 3: Build** — Run: `dotnet build "$REPO\Mdex.sln"` → PASS.

- [ ] **Step 4: Smoke-test boot + anonymous rejection**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
$env:MDEX_ROOT = Join-Path $env:TEMP ("mdex-gw-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $env:MDEX_ROOT | Out-Null
$p = Start-Process dotnet -ArgumentList "run --project `"$REPO\src\Mdex.Gateway`"" -PassThru
try {
  Start-Sleep -Seconds 4
  (Invoke-WebRequest -Uri http://127.0.0.1:3001/ -Method Post -Body "{}" `
     -ContentType "application/json" -SkipHttpErrorCheck).StatusCode
} finally {
  Stop-Process -Id $p.Id -Force
  Remove-Item $env:MDEX_ROOT -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item Env:\MDEX_ROOT
}
```
Expected: prints `401` (no token → rejected before any tool runs).

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "feat(gateway): wire host — bearer auth, shared tools, hot-reload, 127.0.0.1 bind"
```

---

## Task 10: Integration tests — auth gate, header edge-cases, corrupt-file fail-closed

Boots the real host with `WebApplicationFactory<Program>` over a temp vault + temp token store, and checks the HTTP-level gate. Covers the Phase-3 header-parsing edge-cases and the corrupt-`tokens.json` fail-closed behaviour. Tool-level role enforcement and path containment are already covered by Plan B (`ToolAuthorizerTests`, `ToolEndToEndTests`) over the same shared tools.

**Files:**
- Create: `tests/Mdex.Gateway.Tests/AuthIntegrationTests.cs`

- [ ] **Step 1: Write the tests** — `tests/Mdex.Gateway.Tests/AuthIntegrationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Mdex.Core.Security;
using Mdex.Tokens;
using Microsoft.AspNetCore.Mvc.Testing;
namespace Mdex.Gateway.Tests;

public class AuthIntegrationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "mdex-gw-it-" + Guid.NewGuid().ToString("N"));
    readonly string _tokens = Path.Combine(Path.GetTempPath(), "mdex-gw-it-" + Guid.NewGuid().ToString("N") + ".json");
    const string RawToken = "mdex_integration";

    public AuthIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        new TokenStore(_tokens).Add(new TokenEntry(
            TokenHasher.Hash(RawToken), Roles.ToWire(Role.Read), "it", "2026-06-05"));
    }
    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { File.Delete(_tokens); } catch { }
    }

    WebApplicationFactory<Program> Factory(string? tokensOverride = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Mdex:Root", _root).UseSetting("Mdex:TokenStore", tokensOverride ?? _tokens));

    static HttpRequestMessage McpPost(string? rawAuthHeader)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", Encoding.UTF8, "application/json"),
        };
        if (rawAuthHeader is not null)
            req.Headers.TryAddWithoutValidation("Authorization", rawAuthHeader);
        return req;
    }

    [Fact]
    public async Task No_header_is_401()
    {
        using var client = Factory().CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(McpPost(null))).StatusCode);
    }

    [Theory]
    [InlineData("Bearer mdex_wrong")]     // unknown token
    [InlineData("Bearer ")]               // empty token
    [InlineData("Basic abc")]             // wrong scheme
    [InlineData("mdex_integration")]      // no scheme prefix
    public async Task Malformed_or_invalid_header_is_401(string header)
    {
        using var client = Factory().CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(McpPost(header))).StatusCode);
    }

    [Fact]
    public async Task Valid_token_is_accepted_not_401()
    {
        using var client = Factory().CreateClient();
        var res = await client.SendAsync(McpPost("Bearer " + RawToken));
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Corrupt_token_file_fails_closed_401()   // hot-reload robustness
    {
        var corrupt = Path.Combine(Path.GetTempPath(), "mdex-gw-it-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(corrupt, "{ this is not valid json");
        try
        {
            using var client = Factory(corrupt).CreateClient();
            // corrupt file → store loads empty → even a well-formed bearer is unknown → 401
            var res = await client.SendAsync(McpPost("Bearer " + RawToken));
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
        finally { File.Delete(corrupt); }
    }
}
```

> **Endpoint path:** `MapMcp()` maps at `/` by default; the requests post there. If a newer SDK maps under `/mcp`, update the path in `McpPost`. The 401-vs-not-401 assertions are path-correct as long as the request reaches the authorized MCP endpoint.

- [ ] **Step 2: Run → expect PASS** — Run: `dotnet test "$REPO\Mdex.sln" --filter AuthIntegrationTests` → PASS. If "valid token" still returns 401, confirm `Mdex:TokenStore` is honoured in `Program.cs` and the seeded hash matches `TokenHasher.Hash(RawToken)`.

- [ ] **Step 3: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "test(gateway): auth gate, header edge-cases, corrupt-file fail-closed"
```

---

## Task 11: Deployment artifacts + docs

**Files:**
- Create: `deploy/mdex-gateway.service`
- Modify: `README.md`
- Modify: `docs/2026-06-04-mdex-mcp-design.md` (append a pointer that this plan landed the gateway form)

- [ ] **Step 1: systemd unit** — `deploy/mdex-gateway.service`:

```ini
# deploy/mdex-gateway.service
[Unit]
Description=mdex gateway (remote MCP server)
After=network.target

[Service]
Type=simple
User=mdadmin
Environment=MDEX_ROOT=/home/mdadmin/repos/mdex
Environment=ASPNETCORE_ENVIRONMENT=Production
ExecStart=/opt/mdex-gateway/mdex-gateway
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

- [ ] **Step 2: Add a README section.** Append under the existing docs (and the `mdex-mcp` section from Plan B):

````markdown
## mdex-gateway (remote MCP server)

`mdex-gateway` exposes the **same** mdex tools as `mdex-mcp`, but over HTTP for off-machine
clients (phone, web, Langdock). It hosts `Mdex.Mcp.Tools` in-process and gates every tool by
the role carried by the caller's bearer token. It is the **sibling** of the local stdio host —
same tools, different transport (design §2).

**Roles (ladder):** `read` ⊂ `write-intent` ⊂ `fullwrite` (9 read / +5 intent / +5 raw-fs tools).
**Transport:** Streamable HTTP (default) + legacy SSE.
**Config:** `MDEX_ROOT` (vault), `Mdex:TokenStore` or `MDEX_GATEWAY_TOKENS`
(default `~/.config/mdex-gateway/tokens.json`), Kestrel URL in `appsettings.json`.
Run behind a TLS reverse proxy; the server binds `127.0.0.1` only — never a public interface
without the proxy (a bearer token over plain HTTP would be in clear text).

**Credential hygiene (N-4):** configure the gateway's bearer token **only** in off-machine
clients. On a machine with a local checkout, use `mdex-mcp` (local) — never put the remote
token in a local agent config.

## mdex-token (token admin)

```
mdex-token new --role <read|write-intent|fullwrite> --label <name>
mdex-token list
mdex-token revoke <label>
```

Only the SHA-256 hash is stored; the raw token is shown once. The gateway watches the token
file and picks up changes within ~1s — no restart needed.
````

- [ ] **Step 3: Pointer on the superseded Plan-2 design** — append to `docs/2026-06-04-mdex-mcp-design.md`:

```markdown

---

## Status (2026-06-05): superseded form implemented as `mdex-gateway`

The remote server this doc described as `mdex-mcp` was renamed `mdex-gateway` and built per
[2026-06-05-mdex-gateway-tokens-plan.md](2026-06-05-mdex-gateway-tokens-plan.md) (Plan C). The
in-process backend and three-role ladder are in effect; tools are shared with the local host
via `Mdex.Mcp.Tools`. Source of truth is the code + `mdex-token --help` + README.
```

- [ ] **Step 4: Full solution test run** — Run: `dotnet test "$REPO\Mdex.sln"` → Expected: **all projects green** (Plans A + B + C).

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "docs: deploy unit + document mdex-gateway + mdex-token"
```

---

## Done-condition for Plan C

- `dotnet build "$REPO\Mdex.sln"` succeeds; `dotnet test "$REPO\Mdex.sln"` reports **all tests green** across Plans A + B + C.
- `Mdex.Tokens` owns the hashed store (atomic, `0600`/`0700`, fixed-time compare, hot-reload) and references **only** `Mdex.Core` (no MCP SDK).
- `mdex-token` creates/lists/revokes tokens against `~/.config/mdex-gateway/tokens.json`.
- `mdex-gateway` serves the shared 19 tools over HTTP behind bearer auth, `127.0.0.1`-bound, with role from the token claim via `HttpRoleProvider` → the shared `ToolAuthorizer`.
- **Security gates met:** D-3/D-6 (Task 3), fixed-time compare (Task 3), D-7 bind (Task 9), D-4 inherited from `MdexBackend` (Plan B), header edge-cases + corrupt-file fail-closed (Task 10), no token in logs (Task 8). Path containment + `[MinRole]` default-deny inherited from Plan B over the shared tools.
- Four binaries exist and deploy disjointly: `mdex-cli` + `mdex-mcp` (local global tools), `mdex-gateway` + `mdex-token` (server-only).

---

## Self-Review (against the design + security plan)

- **§2 (B2 sibling host, no bridge):** gateway hosts the **same** `Mdex.Mcp.Tools` over HTTP via `MapMcp`; no proxy to the stdio host. ✔
- **§3 (in-process Data object):** gateway uses the shared `MdexBackend` (Plan B) — no `RunToJson` envelope. ✔
- **§4 (role source = token, per request):** `BearerAuthHandler` → claim → `HttpRoleProvider` (scoped) → shared `ToolAuthorizer`. The ladder itself is reused from `Mdex.Core.Security`, not redefined. ✔
- **§6 (inherited from Plan 2):** token store (hashes only, `mdex_<base64url(32B)>`, atomic, `0600`/`0700`), auth + hot-reload, token admin, both transports, `~/.config/mdex-gateway/tokens.json`. ✔
- **§8 / security gates:** D-3/D-6 (Task 3), D-7 (Task 9), D-4 inherited (Plan B `MdexBackend`); Phase-3 — constant-time (Task 3), header edge-cases + corrupt-file fail-closed (Task 10), no-token-logging (Task 8), path-traversal through tools (inherited, Plan B Task 7). ✔
- **Deviations from Plan-2 (intentional, all named):** `Role` in Core not Tokens; ASP.NET-free `ToolAuthorizer` + gateway `HttpRoleProvider`; tools referenced from the shared lib; `mdex-mcpctl`→`mdex-token`, remote `mdex-mcp`→`mdex-gateway`; token path under `mdex-gateway`. ✔
- **Type consistency:** `TokenEntry(Hash, Role, Label, Created)`; `TokenStore.FindRole(raw) → Role?`; `TokenAdmin.New(Role, label) → string`; `HttpRoleProvider.RoleClaim = "mdex-role"` used by both auth pieces; `BearerAuthHandler.SchemeName = "Bearer"`. Consistent across Tasks 1–11. ✔
- **Placeholder scan:** none — exact files, code, commands throughout; SDK uncertainties carry concrete fallbacks.
- **Out of scope (correct, reversible nachrüst-Pfade per design):** OAuth metadata, Infisical, token expiry (D-8 accepted tradeoff), `sync` tool, bootstrapper. After Plan C: run the security-plan **Phase-3 code review** (`security-review` skill on the branch, `code-review` on the PR) against both hosts.

---

## Execution Handoff

Plan complete and saved to `docs/2026-06-05-mdex-gateway-tokens-plan.md`. Two execution options:

1. **Subagent-Driven (recommended)** — a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute the tasks in this session with checkpoints for review.

Which approach? (Per the agreed sequencing, execution begins after you've reviewed Plans A, B and C.)
