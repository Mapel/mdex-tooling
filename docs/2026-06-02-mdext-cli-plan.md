# mdext-CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `mdext`, a vault-only .NET CLI that performs every mdex content operation (search, read, list, create, append, promote, raw FS) in two output modes — human-readable and `--json` — so it can serve as both a terminal tool and the stable subprocess backend for the later `mdex-mcp` server.

**Architecture:** A single console binary (`Mdex.Cli` → `mdext`). A minimal hand-rolled command dispatcher maps a verb to an `ICommand`; every command returns a `CommandResult` (data object + human text), and the dispatcher renders it either as a stable JSON envelope (`--json`) or as human text. Shared services do the heavy lifting: `VaultResolver` (find the vault root), `PathGuard` (jail every path inside the root), `FrontmatterParser` + `NoteRepository` (fresh, index-free scan of the vault on each run), `Slugifier`, and template-based file generation that reuses the vault's own `resources/templates/`.

**Tech Stack:** .NET 10 SDK (LTS) · C# · xUnit (tests) · YamlDotNet (frontmatter *parsing* only — writes use targeted line edits) · System.Text.Json (output). No index, no database, no web framework (that comes in the `mdex-mcp` plan).

> **Build order context:** This is **Plan 1 of 3** for the `mdex-tooling` repo. `mdex-mcpctl` (token admin) and `mdex-mcp` (server) get their own plans *after* this CLI is built and its `--json` output is stable. The `--json` shape defined here is a **contract** the MCP server depends on — do not change it casually.

> **SDK note:** This plan targets `net10.0` (LTS). Install the .NET 10 SDK before
> Task 1 and confirm `dotnet --version` reports `10.x`. Nothing else in the plan
> depends on the exact version.

---

## Prerequisites & Goal condition (autonomous `/goal` run)

This plan is written to be executed autonomously via `/goal`. Before arming the
goal, ensure the environment is ready; while running, never rely on shell state.

**Paths (machine-independent — substitute your own):**
- `$REPO` = root of the `mdex-tooling` git repo (you create this yourself: empty
  folder + `git init`). In prose written as `<REPO>`.
- `$VAULT` = root of the mdex vault (the content repo). In prose written as `<VAULT>`.
- **No path is hard-coded.** Set `$REPO` (and `$VAULT` where needed) at the start
  of *each* shell call — PowerShell variables do **not** persist between separate
  tool calls. Example to prepend per call: `$REPO = "<your-path>\mdex-tooling"`.
- The C# code and tests are already fully path-free: tests use temp dirs, the
  vault is resolved at runtime via `--root` / `MDEX_ROOT` / config.

**Prerequisites (verify first, fail loudly if missing):**
- `dotnet --version` → `10.*` (install the .NET 10 SDK if not).
- `git --version` succeeds.
- `$REPO` exists, is a **fresh/empty git repo** (you ran the folder creation +
  `git init`), and contains **no** `Mdex.sln` yet. If a solution already exists
  there, stop and ask — Task 1 only scaffolds into an empty repo.

**Goal condition (the `/goal` stop predicate):**
> In the `mdex-tooling` repo (`$REPO`) the `mdext` CLI is implemented per this
> plan (Tasks 1–22), `dotnet test "$REPO\Mdex.sln"` reports **all tests passing**,
> and `git -C $REPO status` is clean (every task committed).

Verify it at the end by actually running those two commands — evidence, not assertion.

**Autonomous-execution rules (no `cd`, no prompts):**
- All commands address the repo **explicitly** via `$REPO` — `dotnet test "$REPO\Mdex.sln" …`, `git -C $REPO …`, `dotnet build "$REPO\Mdex.sln"`. The per-task `dotnet test --filter X` / `git commit -am "…"` lines below are shorthand for these `$REPO`-qualified forms.
- Do **not** use `cd` in compound commands (it can trigger a permission prompt that would block the goal).

**Task ordering for clean compilation:** implement in the order
**1 → 2 → 3 → 4 → 5 → 6 → 14 → 8 → 7 → 9 …**. (TemplateService/FrontmatterEditor
and NoteRepository exist before the CLI backbone in Task 7 references them, so
every task compiles on its own — the Task 7 "comment out" note then never applies.)

---

## File Structure

The code lives **outside the vault** in its own repo `mdex-tooling` = `<REPO>` (spec §9), typically a sibling of the vault `<VAULT>`. The vault is reached only at runtime via `--root` / `MDEX_ROOT` / config — never hard-coded.

```
mdex-tooling/                         (new git repo, NOT inside the vault)
  Mdex.sln
  src/Mdex.Cli/
    Mdex.Cli.csproj
    Program.cs                        Entry point: parse global flags, resolve vault, dispatch
    Cli/
      ICommand.cs                     ICommand, CommandResult, CommandException, CliContext
      CommandRegistry.cs              verb -> ICommand lookup
      JsonEnvelope.cs                 Stable { ok, command, data | error } serializer
      ArgReader.cs                    Tiny positional/flag/stdin arg helper
    Vault/
      VaultResolver.cs                --root > MDEX_ROOT > config file
      PathGuard.cs                    Resolve + jail paths inside the vault root
      Slugifier.cs                    Port of new.sh slugify()
      Frontmatter.cs                  Frontmatter model + Note + NoteSummary
      FrontmatterParser.cs            Split frontmatter/body, parse YAML (read only)
      FrontmatterEditor.cs            Targeted line edits (set type/modified) for writes
      NoteRepository.cs               Fresh scan: LoadAll / FindBySlugOrPath
      TemplateService.cs              Read resources/templates, substitute placeholders
    Commands/
      Read/  SearchCommand.cs ReadNoteCommand.cs ListCommand.cs GetByTagCommand.cs
             GetByTypeCommand.cs GetByDateRangeCommand.cs ListTagsCommand.cs
             SearchTagsCommand.cs FindRelatedCommand.cs
      Write/ CaptureInboxCommand.cs CreateCommand.cs AppendToNoteCommand.cs
             AppendToLogCommand.cs PromoteInboxItemCommand.cs
      Fs/    RawFsCommands.cs         write-file / append-file / move / mkdir / delete
  tests/Mdex.Cli.Tests/
    Mdex.Cli.Tests.csproj
    TestVault.cs                      Disposable temp vault fixture (writes its own templates)
    <one test file per command/service>
  README.md
```

Each file has one responsibility. Commands are thin: they parse args, call a service, return a `CommandResult`. All filesystem mutation goes through `PathGuard`.

---

## Per-task rhythm

Every implementation task follows the TDD cycle. To keep the plan readable, each task lists the **failing test**, the **implementation**, and the **exact commands**; the rhythm is always:

1. Write the failing test → 2. `dotnet test --filter <Name>` (expect FAIL) → 3. Write minimal implementation → 4. `dotnet test --filter <Name>` (expect PASS) → 5. Commit.

Run tests against `"$REPO\Mdex.sln"`. Commit after each task with the message shown (`git -C $REPO`).

---

## Phase 0 — Scaffolding & shared services

### Task 1: Scaffold the solution and projects

> **You create the repo yourself** beforehand: an empty `<REPO>` folder with
> `git init` already run. This task scaffolds the .NET solution *into* it. It does
> **not** create the folder or run `git init`.

**Files (created into the existing `$REPO`):**
- Create: `Mdex.sln`, `src/Mdex.Cli/Mdex.Cli.csproj`, `tests/Mdex.Cli.Tests/Mdex.Cli.Tests.csproj`, `.gitignore`, `README.md`

- [ ] **Step 1: Scaffold solution and projects**

Set `$REPO` to your repo path, then run with explicit paths (no `cd`):

```powershell
$REPO = "<your-path>\mdex-tooling"   # the empty git repo you created
dotnet new sln    -o $REPO -n Mdex
dotnet new console -n Mdex.Cli       -o "$REPO\src\Mdex.Cli"
dotnet new xunit   -n Mdex.Cli.Tests -o "$REPO\tests\Mdex.Cli.Tests"
dotnet sln "$REPO\Mdex.sln" add "$REPO\src\Mdex.Cli\Mdex.Cli.csproj" "$REPO\tests\Mdex.Cli.Tests\Mdex.Cli.Tests.csproj"
dotnet add "$REPO\tests\Mdex.Cli.Tests\Mdex.Cli.Tests.csproj" reference "$REPO\src\Mdex.Cli\Mdex.Cli.csproj"
dotnet add "$REPO\src\Mdex.Cli\Mdex.Cli.csproj" package YamlDotNet
dotnet new gitignore -o $REPO
```

- [ ] **Step 2: Set the CLI tool identity**

Replace `src/Mdex.Cli/Mdex.Cli.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Cli</RootNamespace>
    <AssemblyName>mdext</AssemblyName>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>mdext</ToolCommandName>
    <PackageId>Mdex.Cli</PackageId>
    <Version>0.1.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="YamlDotNet" Version="16.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build "$REPO\Mdex.sln"`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Commit**

```powershell
git -C $REPO add -A
git -C $REPO commit -m "chore: scaffold mdex-tooling solution with Mdex.Cli + tests"
```

> From here on, `git commit -am "…"` is shorthand for `git -C $REPO commit -am "…"`
> and `dotnet test --filter X` for `dotnet test "$REPO\Mdex.sln" --filter X`.
> **Re-assign `$REPO` at the start of every shell call** — PowerShell variables do
> not persist between separate tool calls.

---

### Task 2: `TestVault` fixture

A disposable temp directory that looks like a real vault, including the `resources/templates/` the CLI reads. Self-contained — does not depend on the real vault's location.

**Files:**
- Create: `tests/Mdex.Cli.Tests/TestVault.cs`

- [ ] **Step 1: Write the fixture (used by later tests; verified via a smoke test)**

```csharp
using System.Text;

namespace Mdex.Cli.Tests;

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

    /// <summary>Write a note with raw text at a vault-relative path. Returns the absolute path.</summary>
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

- [ ] **Step 2: Smoke test**

Create `tests/Mdex.Cli.Tests/TestVaultTests.cs`:

```csharp
namespace Mdex.Cli.Tests;

public class TestVaultTests
{
    [Fact]
    public void Creates_braid_folders_and_templates()
    {
        using var v = new TestVault();
        Assert.True(Directory.Exists(Path.Combine(v.Root, "inbox")));
        Assert.True(File.Exists(Path.Combine(v.Root, "resources", "templates", "frontmatter-zettel.md")));
    }
}
```

- [ ] **Step 3: Run** → `dotnet test --filter TestVaultTests` → expect PASS.

- [ ] **Step 4: Commit**

```powershell
git add -A; git commit -m "test: add disposable TestVault fixture"
```

---

### Task 3: `Slugifier`

Port of `new.sh`'s `slugify()`: lowercase; every char outside `[a-z0-9äöüß-]` → `-`; collapse repeats; trim leading/trailing `-`.

**Files:**
- Create: `src/Mdex.Cli/Vault/Slugifier.cs`
- Test: `tests/Mdex.Cli.Tests/SlugifierTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class SlugifierTests
{
    [Theory]
    [InlineData("Emergenz Denken", "emergenz-denken")]
    [InlineData("Café & Bar!!", "café-bar")]   // & and ! -> -, collapsed, trimmed
    [InlineData("  Über_Alles  ", "über-alles")]
    [InlineData("ÄÖÜ-ß", "äöü-ß")]
    public void Slugify_matches_pkmt_rules(string input, string expected)
        => Assert.Equal(expected, Slugifier.Slugify(input));
}
```

- [ ] **Step 2: Run** → `dotnet test --filter SlugifierTests` → expect FAIL (type missing).

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using System.Text;

namespace Mdex.Cli.Vault;

public static class Slugifier
{
    public static string Slugify(string input)
    {
        var lower = input.ToLower(CultureInfo.InvariantCulture);
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            bool keep = ch is >= 'a' and <= 'z' or >= '0' and <= '9'
                        || ch is 'ä' or 'ö' or 'ü' or 'ß' or '-';
            sb.Append(keep ? ch : '-');
        }
        var collapsed = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return collapsed;
    }
}
```

- [ ] **Step 4: Run** → `dotnet test --filter SlugifierTests` → expect PASS.

- [ ] **Step 5: Commit** → `git add -A; git commit -m "feat: add Slugifier (port of pkmt slugify)"`

---

### Task 4: `VaultResolver`

Resolve the vault root in order: `--root` flag > `MDEX_ROOT` env var > config file (`%APPDATA%\mdex\config.json` on Windows, `~/.config/mdex/config.json` elsewhere; JSON `{ "root": "..." }`). Throw `CommandException` if none resolves to an existing directory.

**Files:**
- Create: `src/Mdex.Cli/Vault/VaultResolver.cs`
- Create: `src/Mdex.Cli/Cli/ICommand.cs` (just `CommandException` for now; the rest is added in Task 7)
- Test: `tests/Mdex.Cli.Tests/VaultResolverTests.cs`

- [ ] **Step 1: Add `CommandException`**

Create `src/Mdex.Cli/Cli/ICommand.cs`:

```csharp
namespace Mdex.Cli.Cli;

/// <summary>Expected, user-facing failure. Becomes a clean error (exit 1 / json error), not a stack trace.</summary>
public sealed class CommandException(string message) : Exception(message);
```

- [ ] **Step 2: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class VaultResolverTests
{
    [Fact]
    public void Flag_wins_over_env()
    {
        using var v = new TestVault();
        Environment.SetEnvironmentVariable("MDEX_ROOT", @"C:\does\not\exist");
        try { Assert.Equal(v.Root, VaultResolver.Resolve(v.Root)); }
        finally { Environment.SetEnvironmentVariable("MDEX_ROOT", null); }
    }

    [Fact]
    public void Env_used_when_no_flag()
    {
        using var v = new TestVault();
        Environment.SetEnvironmentVariable("MDEX_ROOT", v.Root);
        try { Assert.Equal(v.Root, VaultResolver.Resolve(null)); }
        finally { Environment.SetEnvironmentVariable("MDEX_ROOT", null); }
    }

    [Fact]
    public void Throws_when_nothing_resolves()
    {
        Environment.SetEnvironmentVariable("MDEX_ROOT", null);
        Assert.Throws<CommandException>(() => VaultResolver.Resolve(@"C:\nope\missing"));
    }
}
```

- [ ] **Step 3: Run** → `dotnet test --filter VaultResolverTests` → expect FAIL.

- [ ] **Step 4: Implement**

```csharp
using System.Text.Json;
using Mdex.Cli.Cli;

namespace Mdex.Cli.Vault;

public static class VaultResolver
{
    public static string Resolve(string? rootFlag)
    {
        foreach (var candidate in Candidates(rootFlag))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full)) return full;
        }
        throw new CommandException(
            "Vault root not found. Pass --root <path>, set MDEX_ROOT, or create the config file.");
    }

    static IEnumerable<string?> Candidates(string? rootFlag)
    {
        yield return rootFlag;
        yield return Environment.GetEnvironmentVariable("MDEX_ROOT");
        yield return FromConfig();
    }

    static string? FromConfig()
    {
        var path = ConfigPath();
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("root", out var r) ? r.GetString() : null;
        }
        catch { return null; }
    }

    static string ConfigPath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdex", "config.json");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "mdex", "config.json");
    }
}
```

- [ ] **Step 5: Run** → `dotnet test --filter VaultResolverTests` → expect PASS. **Commit** → `git commit -am "feat: add VaultResolver (--root > MDEX_ROOT > config)"`

---

### Task 5: `PathGuard`

Resolve any vault-relative-or-absolute path to an absolute path and **refuse anything outside the vault root** (spec §7: paths strictly jailed). Also convert absolute → vault-relative for output.

**Files:**
- Create: `src/Mdex.Cli/Vault/PathGuard.cs`
- Test: `tests/Mdex.Cli.Tests/PathGuardTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class PathGuardTests
{
    [Fact]
    public void Resolves_relative_inside_root()
    {
        using var v = new TestVault();
        var g = new PathGuard(v.Root);
        Assert.Equal(Path.Combine(v.Root, "zettel", "x.md"), g.ResolveInside("zettel/x.md"));
    }

    [Fact]
    public void Rejects_escape_with_dotdot()
    {
        using var v = new TestVault();
        var g = new PathGuard(v.Root);
        Assert.Throws<CommandException>(() => g.ResolveInside("../outside.md"));
    }

    [Fact]
    public void Rejects_absolute_outside_root()
    {
        using var v = new TestVault();
        var g = new PathGuard(v.Root);
        Assert.Throws<CommandException>(() => g.ResolveInside(@"C:\Windows\system32\x"));
    }

    [Fact]
    public void ToRelative_uses_forward_slashes()
    {
        using var v = new TestVault();
        var g = new PathGuard(v.Root);
        Assert.Equal("zettel/x.md", g.ToRelative(Path.Combine(v.Root, "zettel", "x.md")));
    }
}
```

- [ ] **Step 2: Run** → `dotnet test --filter PathGuardTests` → expect FAIL.

- [ ] **Step 3: Implement**

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Vault;

public sealed class PathGuard(string vaultRoot)
{
    readonly string _root = Path.GetFullPath(vaultRoot);

    /// <summary>Resolve a vault-relative or absolute path; throw if it escapes the vault root.</summary>
    public string ResolveInside(string pathOrRelative)
    {
        var combined = Path.IsPathRooted(pathOrRelative)
            ? pathOrRelative
            : Path.Combine(_root, pathOrRelative);
        var full = Path.GetFullPath(combined);

        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root : _root + Path.DirectorySeparatorChar;
        if (full != _root && !full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new CommandException($"Path escapes the vault root: {pathOrRelative}");
        return full;
    }

    public string ToRelative(string absolute)
        => Path.GetRelativePath(_root, absolute).Replace(Path.DirectorySeparatorChar, '/');
}
```

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add PathGuard (jail paths inside vault root)"`

---

### Task 6: Frontmatter model + parser

`FrontmatterParser` splits the leading `---\n…\n---\n` block from the body and parses the YAML into a typed `Frontmatter` (read-only path uses YamlDotNet). Defines `Note` and `NoteSummary`.

**Files:**
- Create: `src/Mdex.Cli/Vault/Frontmatter.cs`
- Create: `src/Mdex.Cli/Vault/FrontmatterParser.cs`
- Test: `tests/Mdex.Cli.Tests/FrontmatterParserTests.cs`

- [ ] **Step 1: Define the models** (`src/Mdex.Cli/Vault/Frontmatter.cs`)

```csharp
namespace Mdex.Cli.Vault;

public sealed class Frontmatter
{
    public string? Type { get; set; }
    public string? Created { get; set; }
    public string? Modified { get; set; }
    public List<string> Tags { get; set; } = new();
    /// <summary>Raw key/value for any other fields (author, source, backlinks…).</summary>
    public Dictionary<string, object?> Extra { get; set; } = new();
}

public sealed class Note
{
    public required string Path { get; init; }          // absolute
    public required string RelativePath { get; init; }   // forward-slash, vault-relative
    public required string Slug { get; init; }           // filename without .md
    public required string Title { get; init; }          // first H1 or slug
    public required Frontmatter Frontmatter { get; init; }
    public required string Body { get; init; }

    public NoteSummary ToSummary() => new()
    {
        Path = RelativePath, Slug = Slug, Title = Title,
        Type = Frontmatter.Type, Tags = Frontmatter.Tags,
        Created = Frontmatter.Created, Modified = Frontmatter.Modified,
    };
}

public sealed class NoteSummary
{
    public required string Path { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string? Type { get; init; }
    public List<string> Tags { get; init; } = new();
    public string? Created { get; init; }
    public string? Modified { get; init; }
}

public sealed class ParseResult
{
    public required Frontmatter Frontmatter { get; init; }
    public required string Body { get; init; }
}
```

- [ ] **Step 2: Failing test** (`FrontmatterParserTests.cs`)

```csharp
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class FrontmatterParserTests
{
    const string Sample =
        "---\ntype: zettel\ncreated: 2026-06-01T10:00:00\nmodified: 2026-06-02\n" +
        "tags: [emergenz, denken]\nsource:\n---\n\n# Emergenz\n\nBody text.\n";

    [Fact]
    public void Parses_typed_fields_and_tags()
    {
        var r = FrontmatterParser.Parse(Sample);
        Assert.Equal("zettel", r.Frontmatter.Type);
        Assert.Equal("2026-06-01T10:00:00", r.Frontmatter.Created);
        Assert.Equal("2026-06-02", r.Frontmatter.Modified);
        Assert.Equal(new[] { "emergenz", "denken" }, r.Frontmatter.Tags);
    }

    [Fact]
    public void Separates_body()
    {
        var r = FrontmatterParser.Parse(Sample);
        Assert.StartsWith("# Emergenz", r.Body.TrimStart());
    }

    [Fact]
    public void No_frontmatter_yields_empty_model_and_full_body()
    {
        var r = FrontmatterParser.Parse("# Just a heading\n\ntext\n");
        Assert.Null(r.Frontmatter.Type);
        Assert.StartsWith("# Just a heading", r.Body);
    }
}
```

- [ ] **Step 3: Run** → `dotnet test --filter FrontmatterParserTests` → expect FAIL.

- [ ] **Step 4: Implement** (`FrontmatterParser.cs`)

```csharp
using YamlDotNet.Serialization;

namespace Mdex.Cli.Vault;

public static class FrontmatterParser
{
    static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    public static ParseResult Parse(string text)
    {
        text = text.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n"))
            return new ParseResult { Frontmatter = new Frontmatter(), Body = text };

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
            return new ParseResult { Frontmatter = new Frontmatter(), Body = text };

        var yaml = text.Substring(4, end - 4);
        var afterMarker = text.IndexOf('\n', end + 1);
        var body = afterMarker < 0 ? "" : text[(afterMarker + 1)..];

        return new ParseResult { Frontmatter = FromYaml(yaml), Body = body };
    }

    static Frontmatter FromYaml(string yaml)
    {
        var fm = new Frontmatter();
        Dictionary<string, object?>? map;
        try { map = Yaml.Deserialize<Dictionary<string, object?>>(yaml); }
        catch { return fm; }
        if (map is null) return fm;

        foreach (var (key, value) in map)
        {
            switch (key)
            {
                case "type": fm.Type = value?.ToString(); break;
                case "created": fm.Created = value?.ToString(); break;
                case "modified": fm.Modified = value?.ToString(); break;
                case "tags": fm.Tags = AsList(value); break;
                default: fm.Extra[key] = value; break;
            }
        }
        return fm;
    }

    static List<string> AsList(object? value) => value switch
    {
        IEnumerable<object> seq => seq.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList(),
        string s when s.Length > 0 => new List<string> { s },
        _ => new List<string>(),
    };
}
```

- [ ] **Step 5: Run** → expect PASS. **Commit** → `git commit -am "feat: add Frontmatter model + parser"`

---

### Task 7: CLI plumbing — `ICommand`, `CommandResult`, `CliContext`, registry, JSON envelope, `Program`

The dispatch backbone plus a `version` command as the walking skeleton.

**Files:**
- Modify: `src/Mdex.Cli/Cli/ICommand.cs` (add interfaces/records)
- Create: `src/Mdex.Cli/Cli/JsonEnvelope.cs`, `src/Mdex.Cli/Cli/CommandRegistry.cs`, `src/Mdex.Cli/Cli/ArgReader.cs`
- Modify: `src/Mdex.Cli/Program.cs`
- Test: `tests/Mdex.Cli.Tests/JsonEnvelopeTests.cs`

- [ ] **Step 1: Failing test** (the stable JSON contract)

```csharp
using Mdex.Cli.Cli;
namespace Mdex.Cli.Tests;

public class JsonEnvelopeTests
{
    [Fact]
    public void Ok_envelope_has_ok_command_data()
    {
        var json = JsonEnvelope.Ok("search", new { hits = 2 });
        Assert.Contains("\"ok\": true", json);
        Assert.Contains("\"command\": \"search\"", json);
        Assert.Contains("\"hits\": 2", json);
    }

    [Fact]
    public void Error_envelope_has_ok_false_and_message()
    {
        var json = JsonEnvelope.Err("read-note", "not found");
        Assert.Contains("\"ok\": false", json);
        Assert.Contains("\"error\": \"not found\"", json);
    }
}
```

- [ ] **Step 2: Run** → `dotnet test --filter JsonEnvelopeTests` → expect FAIL.

- [ ] **Step 3: Implement the backbone**

Append to `src/Mdex.Cli/Cli/ICommand.cs`:

```csharp
using Mdex.Cli.Vault;

namespace Mdex.Cli.Cli;

public sealed record CommandResult(object? Data, string HumanText)
{
    public static CommandResult Of(object? data, string humanText) => new(data, humanText);
}

public sealed class CliContext
{
    public required string VaultRoot { get; init; }
    public required bool Json { get; init; }
    public required NoteRepository Repo { get; init; }
    public required PathGuard Guard { get; init; }
    public required TemplateService Templates { get; init; }
}

public interface ICommand
{
    string Name { get; }
    bool NeedsVault => true;          // version/help override to false
    CommandResult Run(string[] args, CliContext ctx);
}
```

Create `src/Mdex.Cli/Cli/JsonEnvelope.cs`:

```csharp
using System.Text.Json;

namespace Mdex.Cli.Cli;

public static class JsonEnvelope
{
    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Ok(string command, object? data)
        => JsonSerializer.Serialize(new { ok = true, command, data }, Opts);

    public static string Err(string command, string error)
        => JsonSerializer.Serialize(new { ok = false, command, error }, Opts);
}
```

Create `src/Mdex.Cli/Cli/ArgReader.cs`:

```csharp
namespace Mdex.Cli.Cli;

/// <summary>Tiny positional + `--flag value` + `--flag` (bool) + stdin reader.</summary>
public sealed class ArgReader
{
    readonly List<string> _positionals = new();
    readonly Dictionary<string, string?> _flags = new(StringComparer.Ordinal);

    public ArgReader(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) { _flags[key] = args[++i]; }
                else _flags[key] = null; // boolean flag
            }
            else _positionals.Add(args[i]);
        }
    }

    public string? Positional(int i) => i < _positionals.Count ? _positionals[i] : null;
    public IReadOnlyList<string> Positionals => _positionals;
    public bool Has(string flag) => _flags.ContainsKey(flag);
    public string? Flag(string flag) => _flags.TryGetValue(flag, out var v) ? v : null;

    /// <summary>Content from --content, else from stdin if piped.</summary>
    public string Content()
    {
        var c = Flag("--content");
        if (c is not null) return c;
        if (Console.IsInputRedirected) return Console.In.ReadToEnd();
        return "";
    }
}
```

Create `src/Mdex.Cli/Cli/CommandRegistry.cs`:

```csharp
namespace Mdex.Cli.Cli;

public sealed class CommandRegistry
{
    readonly Dictionary<string, ICommand> _commands = new(StringComparer.Ordinal);
    public void Add(ICommand c) => _commands[c.Name] = c;
    public ICommand? Get(string name) => _commands.GetValueOrDefault(name);
    public IEnumerable<string> Names => _commands.Keys.OrderBy(n => n);
}
```

Replace `src/Mdex.Cli/Program.cs`:

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands;
using Mdex.Cli.Vault;

var registry = CommandFactory.BuildRegistry();

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine("mdext <command> [args] [--root <path>] [--json]");
    Console.WriteLine("Commands: " + string.Join(", ", registry.Names));
    return 0;
}

var verb = args[0];
var rest = args[1..];

// pull global flags out of the tail
var json = rest.Contains("--json");
string? rootFlag = null;
var cleaned = new List<string>();
for (var i = 0; i < rest.Length; i++)
{
    if (rest[i] == "--json") continue;
    if (rest[i] == "--root") { rootFlag = i + 1 < rest.Length ? rest[++i] : null; continue; }
    cleaned.Add(rest[i]);
}

var command = registry.Get(verb);
if (command is null)
{
    Console.Error.WriteLine(json ? JsonEnvelope.Err(verb, $"unknown command '{verb}'")
                                  : $"mdext: unknown command '{verb}'");
    return 1;
}

try
{
    CliContext? ctx = null;
    if (command.NeedsVault)
    {
        var root = VaultResolver.Resolve(rootFlag);
        var guard = new PathGuard(root);
        ctx = new CliContext
        {
            VaultRoot = root, Json = json,
            Repo = new NoteRepository(root, guard),
            Guard = guard,
            Templates = new TemplateService(root),
        };
    }
    else
    {
        // commands that don't touch the vault still get a context shell
        ctx = new CliContext
        {
            VaultRoot = "", Json = json,
            Repo = null!, Guard = null!, Templates = null!,
        };
    }

    var result = command.Run(cleaned.ToArray(), ctx);
    Console.WriteLine(json ? JsonEnvelope.Ok(verb, result.Data) : result.HumanText);
    return 0;
}
catch (CommandException ex)
{
    Console.Error.WriteLine(json ? JsonEnvelope.Err(verb, ex.Message) : $"Error: {ex.Message}");
    return 1;
}
```

Create `src/Mdex.Cli/Commands/CommandFactory.cs` (registers commands; starts with just `version`):

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands;

public static class CommandFactory
{
    public static CommandRegistry BuildRegistry()
    {
        var r = new CommandRegistry();
        r.Add(new VersionCommand());
        // read/write/fs commands are registered here as later tasks add them
        return r;
    }
}

public sealed class VersionCommand : ICommand
{
    public string Name => "version";
    public bool NeedsVault => false;
    public CommandResult Run(string[] args, CliContext ctx)
        => CommandResult.Of(new { version = "0.1.0" }, "mdext 0.1.0");
}
```

> **Note:** `Program.cs` references `NoteRepository` (Task 8) and `TemplateService`
> (Task 14). Per the ordering in "Prerequisites & Goal condition" (… → 14 → 8 → 7 → …),
> both already exist when you implement this task, so everything compiles — no
> commenting-out needed. The `JsonEnvelopeTests` filter in Step 4 will pass once
> this task's backbone is in place.

- [ ] **Step 4: Run** → `dotnet test --filter JsonEnvelopeTests` → expect PASS.

- [ ] **Step 5: Commit** → `git commit -am "feat: CLI backbone — dispatch, JSON envelope, arg reader, version command"`

---

### Task 8: `NoteRepository` — fresh, index-free scan

Scan all `.md` under the vault root (excluding `.git`, `resources/templates`, `resources/scripts`, `resources/assets`), parse each into a `Note`. Provide `LoadAll()` and `FindBySlugOrPath()`. Title = first `# H1` line, else slug.

**Files:**
- Create: `src/Mdex.Cli/Vault/NoteRepository.cs`
- Test: `tests/Mdex.Cli.Tests/NoteRepositoryTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class NoteRepositoryTests
{
    static NoteRepository Repo(TestVault v) => new(v.Root, new PathGuard(v.Root));

    [Fact]
    public void LoadAll_finds_notes_and_skips_templates()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/emergenz.md",
            "---\ntype: zettel\ntags: [denken]\n---\n\n# Emergenz\n\nText\n");
        var notes = Repo(v).LoadAll();
        Assert.Contains(notes, n => n.Slug == "emergenz" && n.Type() == "zettel");
        Assert.DoesNotContain(notes, n => n.RelativePath.StartsWith("resources/templates"));
    }

    [Fact]
    public void Title_falls_back_to_slug_without_h1()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/no-heading.md", "---\ntype: zettel\n---\n\nJust text\n");
        var note = Repo(v).FindBySlugOrPath("no-heading");
        Assert.NotNull(note);
        Assert.Equal("no-heading", note!.Title);
    }

    [Fact]
    public void FindBySlugOrPath_accepts_relative_path()
    {
        using var v = new TestVault();
        v.WriteNote("projects/x/index.md", "---\ntype: project\n---\n\n# X\n");
        var note = Repo(v).FindBySlugOrPath("projects/x/index.md");
        Assert.NotNull(note);
        Assert.Equal("index", note!.Slug);
    }
}
```

> Add a tiny `Type()` helper to keep tests terse — put it in `Frontmatter.cs`:
> ```csharp
> public static class NoteExtensions { public static string? Type(this Note n) => n.Frontmatter.Type; }
> ```

- [ ] **Step 2: Run** → `dotnet test --filter NoteRepositoryTests` → expect FAIL.

- [ ] **Step 3: Implement**

```csharp
using System.Text.RegularExpressions;

namespace Mdex.Cli.Vault;

public sealed class NoteRepository(string vaultRoot, PathGuard guard)
{
    static readonly string[] ExcludedDirs =
        { ".git", "resources/templates", "resources/scripts", "resources/assets" };

    public IReadOnlyList<Note> LoadAll()
    {
        var notes = new List<Note>();
        foreach (var file in Directory.EnumerateFiles(vaultRoot, "*.md", SearchOption.AllDirectories))
        {
            var rel = guard.ToRelative(file);
            if (ExcludedDirs.Any(d => rel.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)))
                continue;
            notes.Add(Build(file, rel));
        }
        return notes;
    }

    public Note? FindBySlugOrPath(string slugOrPath)
    {
        // direct path hit first
        var asPath = Path.Combine(vaultRoot, slugOrPath.Replace('/', Path.DirectorySeparatorChar));
        if (!asPath.EndsWith(".md")) asPath += ".md";
        if (File.Exists(asPath))
            return Build(asPath, guard.ToRelative(asPath));

        return LoadAll().FirstOrDefault(n =>
            string.Equals(n.Slug, slugOrPath, StringComparison.OrdinalIgnoreCase));
    }

    static readonly Regex H1 = new(@"^#\s+(.+)$", RegexOptions.Multiline);

    Note Build(string absolute, string relative)
    {
        var text = File.ReadAllText(absolute);
        var parsed = FrontmatterParser.Parse(text);
        var slug = Path.GetFileNameWithoutExtension(absolute);
        var m = H1.Match(parsed.Body);
        var title = m.Success ? m.Groups[1].Value.Trim() : slug;
        return new Note
        {
            Path = absolute, RelativePath = relative, Slug = slug,
            Title = title, Frontmatter = parsed.Frontmatter, Body = parsed.Body,
        };
    }
}
```

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add NoteRepository (fresh index-free scan)"`

---

## Phase 1 — Read commands

> All read commands are available to **both** token roles (spec §7). Each is registered in `CommandFactory.BuildRegistry()`. After each task, add the `r.Add(new XCommand());` line and confirm `dotnet build` succeeds.

### Task 9: `search`

Full-text, case-insensitive substring search across note bodies + titles. Returns `{ query, hits: [{ path, slug, title, line, snippet }] }`.

**Files:**
- Create: `src/Mdex.Cli/Commands/Read/SearchCommand.cs`
- Modify: `src/Mdex.Cli/Commands/CommandFactory.cs` (register)
- Test: `tests/Mdex.Cli.Tests/SearchCommandTests.cs`

- [ ] **Step 1a: Add the shared JSON test helper** (used by all command tests; tests the real `--json` contract path via `JsonEnvelope`)

Create `tests/Mdex.Cli.Tests/CommandTestExtensions.cs`:

```csharp
using System.Text.Json;
using Mdex.Cli.Cli;

namespace Mdex.Cli.Tests;

public static class CommandTestExtensions
{
    /// <summary>Serialize a CommandResult through the real envelope and return its `data` element.</summary>
    public static JsonElement Data(this CommandResult result)
        => JsonDocument.Parse(JsonEnvelope.Ok("test", result.Data)).RootElement.GetProperty("data");
}
```

> JSON property names are **camelCase** (the envelope's policy): `hits`, `count`,
> `notes`, `path`, `slug`, `title`, `tags`, `tag`, `related`, `entries`, `from`,
> `to`, `frontmatter`, `body`, `query`, `pattern`, etc.

- [ ] **Step 1b: Failing test**

```csharp
using System.Text.Json;
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Read;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class SearchCommandTests
{
    static CliContext Ctx(TestVault v)
    {
        var guard = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, guard), Guard = guard,
            Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Finds_matching_line_with_snippet()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n\n# A\n\nEmergenz ist spannend.\n");
        var data = new SearchCommand().Run(new[] { "Emergenz" }, Ctx(v)).Data();
        Assert.Equal(1, data.GetProperty("hits").GetArrayLength());
    }

    [Fact]
    public void Requires_query()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new SearchCommand().Run(Array.Empty<string>(), Ctx(v)));
    }
}
```

- [ ] **Step 2: Run** → `dotnet test --filter SearchCommandTests` → expect FAIL.

- [ ] **Step 3: Implement**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Read;

public sealed class SearchCommand : ICommand
{
    public string Name => "search";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var query = reader.Positional(0) ?? throw new CommandException("search needs a query: mdext search <query>");

        var hits = new List<object>();
        var human = new System.Text.StringBuilder();
        foreach (var note in ctx.Repo.LoadAll())
        {
            var haystack = note.Title + "\n" + note.Body;
            var lines = haystack.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var snippet = lines[i].Trim();
                    hits.Add(new { path = note.RelativePath, slug = note.Slug, title = note.Title, line = i + 1, snippet });
                    human.AppendLine($"{note.RelativePath}:{i + 1}: {snippet}");
                    break; // one hit per note keeps results readable
                }
            }
        }
        var text = hits.Count == 0 ? $"No matches for '{query}'." : human.ToString().TrimEnd();
        return CommandResult.Of(new { query, hits }, text);
    }
}
```

Register in `CommandFactory.BuildRegistry()`: `r.Add(new Mdex.Cli.Commands.Read.SearchCommand());`

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add search command"`

---

### Task 10: `read-note`

Return the full content of one note. Data: `{ path, slug, title, frontmatter, body }`. Throws if not found.

**Files:**
- Create: `src/Mdex.Cli/Commands/Read/ReadNoteCommand.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/ReadNoteCommandTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Read;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class ReadNoteCommandTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Returns_body_for_known_slug()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n\n# A\n\nHello\n");
        var d = new ReadNoteCommand().Run(new[] { "a" }, Ctx(v)).Data();
        Assert.Equal("a", d.GetProperty("slug").GetString());
        Assert.Contains("Hello", d.GetProperty("body").GetString());
    }

    [Fact]
    public void Throws_when_missing()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new ReadNoteCommand().Run(new[] { "nope" }, Ctx(v)));
    }
}
```

- [ ] **Step 2: Run** → expect FAIL.

- [ ] **Step 3: Implement**

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class ReadNoteCommand : ICommand
{
    public string Name => "read-note";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("read-note needs a slug or path");
        var note = ctx.Repo.FindBySlugOrPath(key) ?? throw new CommandException($"note not found: {key}");

        var data = new
        {
            path = note.RelativePath, slug = note.Slug, title = note.Title,
            frontmatter = new
            {
                type = note.Frontmatter.Type, created = note.Frontmatter.Created,
                modified = note.Frontmatter.Modified, tags = note.Frontmatter.Tags,
            },
            body = note.Body,
        };
        return CommandResult.Of(data, note.Body);
    }
}
```

Register: `r.Add(new Mdex.Cli.Commands.Read.ReadNoteCommand());`

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add read-note command"`

---

### Task 11: `get-by-tag`, `get-by-type`, `get-by-date-range`

Three frontmatter filters that all return `{ count, notes: NoteSummary[] }`. `get-by-tag` uses **AND** semantics (a note must carry every requested tag). `get-by-date-range` filters on `created` by default, `--field modified` to switch; bounds are inclusive `YYYY-MM-DD` strings compared lexicographically (ISO dates sort correctly).

**Files:**
- Create: `src/Mdex.Cli/Commands/Read/GetByTagCommand.cs`, `GetByTypeCommand.cs`, `GetByDateRangeCommand.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/FilterCommandsTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Read;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class FilterCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    static int Count(CommandResult r) => r.Data().GetProperty("count").GetInt32();

    [Fact]
    public void GetByTag_requires_all_tags()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x, y]\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ntags: [x]\n---\n# B\n");
        Assert.Equal(1, Count(new GetByTagCommand().Run(new[] { "x", "y" }, Ctx(v))));
        Assert.Equal(2, Count(new GetByTagCommand().Run(new[] { "x" }, Ctx(v))));
    }

    [Fact]
    public void GetByType_filters_on_frontmatter_type()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        v.WriteNote("projects/p/index.md", "---\ntype: project\n---\n# P\n");
        Assert.Equal(1, Count(new GetByTypeCommand().Run(new[] { "project" }, Ctx(v))));
    }

    [Fact]
    public void GetByDateRange_inclusive_on_created()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ncreated: 2026-05-01T09:00:00\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ncreated: 2026-06-10T09:00:00\n---\n# B\n");
        var r = new GetByDateRangeCommand().Run(new[] { "--from", "2026-05-01", "--to", "2026-05-31" }, Ctx(v));
        Assert.Equal(1, Count(r));
    }
}
```

- [ ] **Step 2: Run** → `dotnet test --filter FilterCommandsTests` → expect FAIL.

- [ ] **Step 3: Implement the three commands**

`GetByTagCommand.cs`:

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class GetByTagCommand : ICommand
{
    public string Name => "get-by-tag";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var tags = reader.Positionals;
        if (tags.Count == 0) throw new CommandException("get-by-tag needs at least one tag");

        var notes = ctx.Repo.LoadAll()
            .Where(n => tags.All(t => n.Frontmatter.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            .Select(n => n.ToSummary()).ToList();
        return Summaries.Result(notes);
    }
}
```

`GetByTypeCommand.cs`:

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class GetByTypeCommand : ICommand
{
    public string Name => "get-by-type";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var type = reader.Positional(0) ?? throw new CommandException("get-by-type needs a type");
        var notes = ctx.Repo.LoadAll()
            .Where(n => string.Equals(n.Frontmatter.Type, type, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.ToSummary()).ToList();
        return Summaries.Result(notes);
    }
}
```

`GetByDateRangeCommand.cs`:

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class GetByDateRangeCommand : ICommand
{
    public string Name => "get-by-date-range";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var from = reader.Flag("--from") ?? throw new CommandException("get-by-date-range needs --from");
        var to = reader.Flag("--to") ?? throw new CommandException("get-by-date-range needs --to");
        var field = reader.Flag("--field") ?? "created";

        var notes = ctx.Repo.LoadAll().Where(n =>
        {
            var value = field == "modified" ? n.Frontmatter.Modified : n.Frontmatter.Created;
            if (string.IsNullOrEmpty(value)) return false;
            var day = value.Length >= 10 ? value[..10] : value; // YYYY-MM-DD prefix
            return string.CompareOrdinal(day, from) >= 0 && string.CompareOrdinal(day, to) <= 0;
        }).Select(n => n.ToSummary()).ToList();
        return Summaries.Result(notes);
    }
}
```

Add a shared `Summaries` helper — create `src/Mdex.Cli/Commands/Read/Summaries.cs`:

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Read;

public static class Summaries
{
    public static CommandResult Result(List<NoteSummary> notes)
    {
        var human = notes.Count == 0
            ? "No matches."
            : string.Join('\n', notes.Select(n => $"{n.Path}  [{n.Type}]  {n.Title}"));
        return CommandResult.Of(new { count = notes.Count, notes }, human);
    }
}
```

Register all three in `CommandFactory.BuildRegistry()`.

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add get-by-tag/type/date-range filters"`

---

### Task 12: `list-tags` and `search-tags`

`list-tags` → `{ tags: [{ tag, count }] }` sorted by count desc, then name. `search-tags <pattern>` → `{ pattern, tags: [string] }` of distinct tags whose name contains `pattern` (case-insensitive).

**Files:**
- Create: `src/Mdex.Cli/Commands/Read/ListTagsCommand.cs`, `SearchTagsCommand.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/TagCommandsTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Read;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class TagCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void ListTags_counts_and_orders()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [deep-work, fokus]\n---\n# A\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\ntags: [deep-work]\n---\n# B\n");
        var first = new ListTagsCommand().Run(Array.Empty<string>(), Ctx(v)).Data()
            .GetProperty("tags")[0];
        Assert.Equal("deep-work", first.GetProperty("tag").GetString());
        Assert.Equal(2, first.GetProperty("count").GetInt32());
    }

    [Fact]
    public void SearchTags_substring_match()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [deep-work, fokus]\n---\n# A\n");
        var tags = new SearchTagsCommand().Run(new[] { "work" }, Ctx(v)).Data()
            .GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(new[] { "deep-work" }, tags);
    }
}
```

- [ ] **Step 2: Run** → expect FAIL.

- [ ] **Step 3: Implement**

`ListTagsCommand.cs`:

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class ListTagsCommand : ICommand
{
    public string Name => "list-tags";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var tags = ctx.Repo.LoadAll()
            .SelectMany(n => n.Frontmatter.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { tag = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).ThenBy(x => x.tag, StringComparer.Ordinal)
            .ToList();
        var human = string.Join('\n', tags.Select(t => $"{t.count,4}  {t.tag}"));
        return CommandResult.Of(new { tags }, human);
    }
}
```

`SearchTagsCommand.cs`:

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class SearchTagsCommand : ICommand
{
    public string Name => "search-tags";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var pattern = reader.Positional(0) ?? throw new CommandException("search-tags needs a pattern");
        var tags = ctx.Repo.LoadAll()
            .SelectMany(n => n.Frontmatter.Tags)
            .Where(t => t.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        return CommandResult.Of(new { pattern, tags }, string.Join('\n', tags));
    }
}
```

Register both.

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add list-tags and search-tags"`

---

### Task 13: `list` and `find-related`

`list [path]` → directory tree under an optional vault-relative subpath: `{ root, entries: [{ path, kind }] }` (`kind` = `dir`|`file`), `.git` excluded. `find-related <slug> [--hops N]` (default 1) → walks outgoing `[[wikilinks]]` in the body **and** incoming references (other notes linking to this slug), up to N hops: `{ slug, related: NoteSummary[] }`.

**Files:**
- Create: `src/Mdex.Cli/Commands/Read/ListCommand.cs`, `FindRelatedCommand.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/ListRelatedTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Read;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class ListRelatedTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void List_returns_entries_under_subpath()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n");
        var entries = new ListCommand().Run(new[] { "zettel" }, Ctx(v)).Data()
            .GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("path").GetString());
        Assert.Contains("zettel/a.md", entries);
    }

    [Fact]
    public void FindRelated_follows_wikilinks_both_directions()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\n---\n# A\n\nSiehe [[b]].\n");
        v.WriteNote("zettel/b.md", "---\ntype: zettel\n---\n# B\n");
        v.WriteNote("zettel/c.md", "---\ntype: zettel\n---\n# C\n\nlinks [[a]]\n");
        var slugs = new FindRelatedCommand().Run(new[] { "a" }, Ctx(v)).Data()
            .GetProperty("related").EnumerateArray()
            .Select(n => n.GetProperty("slug").GetString()).ToList();
        Assert.Contains("b", slugs); // outgoing
        Assert.Contains("c", slugs); // incoming
    }
}
```

- [ ] **Step 2: Run** → expect FAIL.

- [ ] **Step 3: Implement**

`ListCommand.cs`:

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class ListCommand : ICommand
{
    public string Name => "list";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var sub = reader.Positional(0) ?? ".";
        var baseAbs = ctx.Guard.ResolveInside(sub);
        if (!Directory.Exists(baseAbs)) throw new CommandException($"not a directory: {sub}");

        var entries = new List<object>();
        var paths = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(baseAbs, "*", SearchOption.AllDirectories))
        {
            var rel = ctx.Guard.ToRelative(path);
            if (rel.StartsWith(".git/") || rel == ".git") continue;
            entries.Add(new { path = rel, kind = Directory.Exists(path) ? "dir" : "file" });
            paths.Add(rel);
        }
        var human = string.Join('\n', paths);
        return CommandResult.Of(new { root = ctx.Guard.ToRelative(baseAbs), entries }, human);
    }
}
```

`FindRelatedCommand.cs`:

```csharp
using System.Text.RegularExpressions;
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Read;

public sealed class FindRelatedCommand : ICommand
{
    public string Name => "find-related";
    static readonly Regex WikiLink = new(@"\[\[([^\]]+)\]\]");

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var slug = reader.Positional(0) ?? throw new CommandException("find-related needs a slug");
        var hops = int.TryParse(reader.Flag("--hops"), out var h) ? Math.Max(1, h) : 1;

        var all = ctx.Repo.LoadAll();
        if (all.All(n => !string.Equals(n.Slug, slug, StringComparison.OrdinalIgnoreCase)))
            throw new CommandException($"note not found: {slug}");

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug };
        var frontier = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug };

        for (var hop = 0; hop < hops; hop++)
        {
            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var current in frontier)
            {
                var note = all.FirstOrDefault(n => string.Equals(n.Slug, current, StringComparison.OrdinalIgnoreCase));
                if (note is not null)
                    foreach (Match m in WikiLink.Matches(note.Body))   // outgoing
                        next.Add(m.Groups[1].Value.Trim());

                foreach (var other in all)                              // incoming
                    if (WikiLink.Matches(other.Body).Any(m => string.Equals(m.Groups[1].Value.Trim(), current, StringComparison.OrdinalIgnoreCase)))
                        next.Add(other.Slug);
            }
            foreach (var s in next) if (visited.Add(s)) frontier.Add(s);
            frontier = next;
        }
        visited.Remove(slug);

        var related = all.Where(n => visited.Contains(n.Slug)).Select(n => n.ToSummary()).ToList();
        var human = related.Count == 0 ? "No related notes." : string.Join('\n', related.Select(n => $"{n.Path}  {n.Title}"));
        return CommandResult.Of(new { slug, related }, human);
    }
}
```

Register both.

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add list and find-related commands"`

---

## Phase 2 — Write / intent commands

> All write commands require the `read-write` role *at the MCP layer* (spec §7); the CLI itself does not enforce roles — the server does. The CLI just performs the mutation. **No git** here: writing the file is the whole job; `mdex-autocommit.sh` is the only committer (spec §3).

### Task 14: `TemplateService` + `FrontmatterEditor`

`TemplateService` reads `resources/templates/*.md`, substitutes `{{created}}`/`{{modified}}`, and builds new-note text exactly like `new.sh` (frontmatter + optional H1 title + body template). `FrontmatterEditor` does targeted line edits (set `modified`, set `type`) without reformatting the rest.

**Files:**
- Create: `src/Mdex.Cli/Vault/TemplateService.cs`, `src/Mdex.Cli/Vault/FrontmatterEditor.cs`
- Test: `tests/Mdex.Cli.Tests/TemplateServiceTests.cs`, `FrontmatterEditorTests.cs`

- [ ] **Step 1: Failing tests**

`TemplateServiceTests.cs`:

```csharp
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class TemplateServiceTests
{
    [Fact]
    public void Builds_zettel_with_title_h1_and_substituted_dates()
    {
        using var v = new TestVault();
        var svc = new TemplateService(v.Root);
        var text = svc.BuildNote("zettel", "emergenz-denken", includeBody: false,
            created: "2026-06-02T10:00:00", modified: "2026-06-02");
        Assert.Contains("type: zettel", text);
        Assert.Contains("created: 2026-06-02T10:00:00", text);
        Assert.Contains("# Emergenz Denken", text);
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public void Project_includes_default_body_log_heading()
    {
        using var v = new TestVault();
        var svc = new TemplateService(v.Root);
        var text = svc.BuildNote("project", "mein-projekt", includeBody: true,
            created: "2026-06-02T10:00:00", modified: "2026-06-02");
        Assert.Contains("## Log", text);
    }
}
```

`FrontmatterEditorTests.cs`:

```csharp
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class FrontmatterEditorTests
{
    [Fact]
    public void SetField_replaces_existing_line()
    {
        var text = "---\ntype: inbox\ncreated: 2026-06-01T09:00:00\nmodified: 2026-06-01\n---\n\nbody\n";
        var updated = FrontmatterEditor.SetField(text, "modified", "2026-06-02");
        Assert.Contains("modified: 2026-06-02", updated);
        Assert.DoesNotContain("modified: 2026-06-01", updated);
        Assert.Contains("body", updated);
    }
}
```

- [ ] **Step 2: Run** → expect FAIL (both filters).

- [ ] **Step 3: Implement**

`TemplateService.cs`:

```csharp
using System.Globalization;
using System.Text;
using Mdex.Cli.Cli;

namespace Mdex.Cli.Vault;

public sealed class TemplateService(string vaultRoot)
{
    string Dir => Path.Combine(vaultRoot, "resources", "templates");

    /// <summary>Build full note text for a new note (frontmatter + optional H1 + body template).</summary>
    public string BuildNote(string type, string slug, bool includeBody, string created, string modified, string? bodyTemplate = null)
    {
        var fmPath = Path.Combine(Dir, $"frontmatter-{type}.md");
        if (!File.Exists(fmPath)) throw new CommandException($"frontmatter template missing: {fmPath}");
        var fm = File.ReadAllText(fmPath).Replace("\r\n", "\n")
            .Replace("{{created}}", created).Replace("{{modified}}", modified).TrimEnd('\n');

        var sb = new StringBuilder();
        sb.Append(fm).Append('\n');

        var tpl = bodyTemplate ?? (type == "project" ? "project-default" : null);
        if (includeBody && tpl is not null)
        {
            var bodyPath = Path.Combine(Dir, $"body-{tpl}.md");
            if (!File.Exists(bodyPath)) throw new CommandException($"body template missing: {bodyPath}");
            sb.Append('\n').Append("# ").Append(TitleCase(slug)).Append("\n\n");
            sb.Append(File.ReadAllText(bodyPath).Replace("\r\n", "\n"));
        }
        return sb.ToString();
    }

    static string TitleCase(string slug)
    {
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]);
        return string.Join(' ', words);
    }
}
```

`FrontmatterEditor.cs`:

```csharp
namespace Mdex.Cli.Vault;

public static class FrontmatterEditor
{
    /// <summary>Replace `key: ...` inside the leading frontmatter block; insert before closing --- if absent.</summary>
    public static string SetField(string text, string key, string value)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count == 0 || lines[0] != "---") return text;

        var close = lines.FindIndex(1, l => l == "---");
        if (close < 0) return text;

        var idx = lines.FindIndex(1, close - 1, l => l.StartsWith(key + ":"));
        if (idx >= 0) lines[idx] = $"{key}: {value}";
        else lines.Insert(close, $"{key}: {value}");

        return string.Join(nl, lines);
    }
}
```

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add TemplateService + FrontmatterEditor"`

---

### Task 15: `capture-inbox`

Create the next free `inbox/YYYYMMDD-NN.md` with inbox frontmatter + the captured content. Content from `--content` or stdin. Data: `{ path, slug }`.

**Files:**
- Create: `src/Mdex.Cli/Commands/Write/CaptureInboxCommand.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/CaptureInboxTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Write;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class CaptureInboxTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Creates_inbox_file_with_content()
    {
        using var v = new TestVault();
        var rel = new CaptureInboxCommand().Run(new[] { "--content", "Quick idea" }, Ctx(v))
            .Data().GetProperty("path").GetString()!;
        Assert.StartsWith("inbox/", rel);
        var written = v.Read(rel);
        Assert.Contains("type: inbox", written);
        Assert.Contains("Quick idea", written);
    }

    [Fact]
    public void Second_capture_same_day_increments_number()
    {
        using var v = new TestVault();
        var c = new CaptureInboxCommand();
        var a = c.Run(new[] { "--content", "one" }, Ctx(v)).Data().GetProperty("path").GetString();
        var b = c.Run(new[] { "--content", "two" }, Ctx(v)).Data().GetProperty("path").GetString();
        Assert.NotEqual(a, b);
    }
}
```

- [ ] **Step 2: Run** → expect FAIL.

- [ ] **Step 3: Implement**

```csharp
using System.Text;
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Write;

public sealed class CaptureInboxCommand : ICommand
{
    public string Name => "capture-inbox";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var content = reader.Content();
        if (string.IsNullOrWhiteSpace(content)) throw new CommandException("capture-inbox needs --content or piped stdin");

        var now = DateTime.Now;
        var slug = NextInboxSlug(ctx.VaultRoot, now);
        var rel = $"inbox/{slug}.md";
        var abs = ctx.Guard.ResolveInside(rel);

        var fm = ctx.Templates.BuildNote("inbox", slug, includeBody: false,
            created: now.ToString("yyyy-MM-ddTHH:mm:ss"), modified: now.ToString("yyyy-MM-dd"));
        File.WriteAllText(abs, fm + "\n" + content.TrimEnd() + "\n", new UTF8Encoding(false));

        return CommandResult.Of(new { path = rel, slug }, $"✓ {rel}");
    }

    static string NextInboxSlug(string root, DateTime now)
    {
        var prefix = now.ToString("yyyyMMdd");
        var dir = Path.Combine(root, "inbox");
        Directory.CreateDirectory(dir);
        for (var n = 1; ; n++)
        {
            var name = $"{prefix}-{n:D2}";
            if (!File.Exists(Path.Combine(dir, name + ".md"))) return name;
        }
    }
}
```

Register: `r.Add(new Mdex.Cli.Commands.Write.CaptureInboxCommand());`

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add capture-inbox command"`

---

### Task 16: `create` (note/project/area/reference)

One command parameterized by type → covers the four `create_*` intent tools (the MCP server maps each tool to `mdext create <type> …`). `mdext create <type> <name> [--no-body] [--body <tpl>]`. Mirrors `new.sh` subpath handling for project/area/reference (`garten/pflanzplan`). `zettel` lives flat in `zettel/`. Refuses `inbox` (use `capture-inbox`). Data: `{ path, slug, type }`.

**Files:**
- Create: `src/Mdex.Cli/Commands/Write/CreateCommand.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/CreateCommandTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Write;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class CreateCommandTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Creates_zettel_flat()
    {
        using var v = new TestVault();
        var path = new CreateCommand().Run(new[] { "zettel", "Emergenz Denken" }, Ctx(v))
            .Data().GetProperty("path").GetString();
        Assert.Equal("zettel/emergenz-denken.md", path);
    }

    [Fact]
    public void Creates_project_with_subpath()
    {
        using var v = new TestVault();
        var path = new CreateCommand().Run(new[] { "project", "garten/pflanzplan" }, Ctx(v))
            .Data().GetProperty("path").GetString();
        Assert.Equal("projects/garten/pflanzplan.md", path);
    }

    [Fact]
    public void Refuses_inbox()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() => new CreateCommand().Run(new[] { "inbox", "x" }, Ctx(v)));
    }

    [Fact]
    public void Refuses_overwrite()
    {
        using var v = new TestVault();
        var ctx = Ctx(v);
        new CreateCommand().Run(new[] { "zettel", "dup" }, ctx);
        Assert.Throws<CommandException>(() => new CreateCommand().Run(new[] { "zettel", "dup" }, ctx));
    }
}
```

- [ ] **Step 2: Run** → expect FAIL.

- [ ] **Step 3: Implement**

```csharp
using System.Text;
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Write;

public sealed class CreateCommand : ICommand
{
    public string Name => "create";

    static readonly Dictionary<string, string> BaseDirs = new()
    {
        ["zettel"] = "zettel",
        ["project"] = "projects",
        ["area"] = "areas",
        ["reference"] = "resources/references",
    };

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var type = reader.Positional(0) ?? throw new CommandException("create needs a type");
        if (type == "inbox") throw new CommandException("use capture-inbox for inbox entries");
        if (!BaseDirs.TryGetValue(type, out var baseDir))
            throw new CommandException($"unknown type '{type}'. Valid: zettel | project | area | reference");

        var name = reader.Positional(1) ?? throw new CommandException($"create {type} needs a name");
        var noBody = reader.Has("--no-body");
        var bodyTpl = reader.Flag("--body");

        var (relDir, slug) = ResolveTarget(baseDir, name, allowSubpath: type != "zettel");
        var rel = $"{relDir}/{slug}.md";
        var abs = ctx.Guard.ResolveInside(rel);
        if (File.Exists(abs)) throw new CommandException($"already exists: {rel}");
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);

        var now = DateTime.Now;
        var text = ctx.Templates.BuildNote(type, slug, includeBody: !noBody,
            created: now.ToString("yyyy-MM-ddTHH:mm:ss"), modified: now.ToString("yyyy-MM-dd"), bodyTemplate: bodyTpl);
        File.WriteAllText(abs, text, new UTF8Encoding(false));

        return CommandResult.Of(new { path = rel, slug, type }, $"✓ {rel}");
    }

    static (string relDir, string slug) ResolveTarget(string baseDir, string name, bool allowSubpath)
    {
        name = name.EndsWith(".md") ? name[..^3] : name;
        if (allowSubpath && name.Contains('/'))
        {
            var sub = name[..name.LastIndexOf('/')];
            var file = name[(name.LastIndexOf('/') + 1)..];
            return ($"{baseDir}/{sub}", Slugifier.Slugify(file));
        }
        return (baseDir, Slugifier.Slugify(name));
    }
}
```

Register.

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add create command (zettel/project/area/reference)"`

---

### Task 17: `append-to-note`

Append content to a note's body (preceded by a blank line) and bump `modified` to today. Data: `{ path }`.

**Files:**
- Create: `src/Mdex.Cli/Commands/Write/AppendToNoteCommand.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/AppendToNoteTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Write;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class AppendToNoteTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Appends_and_bumps_modified()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\nmodified: 2026-01-01\n---\n\n# A\n\nold\n");
        new AppendToNoteCommand().Run(new[] { "a", "--content", "new line" }, Ctx(v));
        var text = v.Read("zettel/a.md");
        Assert.Contains("new line", text);
        Assert.DoesNotContain("modified: 2026-01-01", text);
    }
}
```

- [ ] **Step 2: Run** → expect FAIL.

- [ ] **Step 3: Implement**

```csharp
using System.Text;
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Write;

public sealed class AppendToNoteCommand : ICommand
{
    public string Name => "append-to-note";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("append-to-note needs a slug or path");
        var content = reader.Content();
        if (string.IsNullOrWhiteSpace(content)) throw new CommandException("append-to-note needs --content or stdin");

        var note = ctx.Repo.FindBySlugOrPath(key) ?? throw new CommandException($"note not found: {key}");
        var text = File.ReadAllText(note.Path).Replace("\r\n", "\n").TrimEnd('\n');
        text += "\n\n" + content.TrimEnd() + "\n";
        text = FrontmatterEditor.SetField(text, "modified", DateTime.Now.ToString("yyyy-MM-dd"));
        File.WriteAllText(note.Path, text, new UTF8Encoding(false));

        return CommandResult.Of(new { path = note.RelativePath }, $"✓ appended to {note.RelativePath}");
    }
}
```

Register.

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add append-to-note command"`

---

### Task 18: `append-to-log`

Append `- <content>` under a project's `## Log` → `### <today>` subsection, creating the dated subsection if missing, and the `## Log` section if missing. This fixes the heading convention from `body-project-default.md` (`## Log` / `### YYYY-MM-DD`) as part of the contract (spec §7). Bumps `modified`. Data: `{ path }`.

**Files:**
- Create: `src/Mdex.Cli/Commands/Write/AppendToLogCommand.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/AppendToLogTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Write;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class AppendToLogTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Creates_dated_subsection_under_log()
    {
        using var v = new TestVault();
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        v.WriteNote("projects/p/index.md",
            "---\ntype: project\nmodified: 2026-01-01\n---\n\n# P\n\n## Log\n### 2026-01-01\n- old entry\n");
        new AppendToLogCommand().Run(new[] { "index", "--content", "did a thing" }, Ctx(v));
        var text = v.Read("projects/p/index.md");
        Assert.Contains($"### {today}", text);
        Assert.Contains("- did a thing", text);
    }

    [Fact]
    public void Adds_log_section_when_missing()
    {
        using var v = new TestVault();
        v.WriteNote("projects/q/index.md", "---\ntype: project\n---\n\n# Q\n\n## Ziel\nx\n");
        new AppendToLogCommand().Run(new[] { "projects/q/index.md", "--content", "kickoff" }, Ctx(v));
        var text = v.Read("projects/q/index.md");
        Assert.Contains("## Log", text);
        Assert.Contains("- kickoff", text);
    }
}
```

- [ ] **Step 2: Run** → expect FAIL.

- [ ] **Step 3: Implement**

```csharp
using System.Text;
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Write;

public sealed class AppendToLogCommand : ICommand
{
    public string Name => "append-to-log";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("append-to-log needs a project slug or path");
        var content = reader.Content();
        if (string.IsNullOrWhiteSpace(content)) throw new CommandException("append-to-log needs --content or stdin");

        var note = ctx.Repo.FindBySlugOrPath(key) ?? throw new CommandException($"note not found: {key}");
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var entry = "- " + content.Trim();

        var lines = File.ReadAllText(note.Path).Replace("\r\n", "\n").Split('\n').ToList();
        var logIdx = lines.FindIndex(l => l.TrimEnd() == "## Log");
        if (logIdx < 0)
        {
            // append a fresh Log section at the end
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
            lines.Add("## Log");
            lines.Add($"### {today}");
            lines.Add(entry);
        }
        else
        {
            var dateIdx = lines.FindIndex(logIdx + 1, l => l.TrimEnd() == $"### {today}");
            if (dateIdx < 0)
            {
                lines.Insert(logIdx + 1, entry);
                lines.Insert(logIdx + 1, $"### {today}");
            }
            else
            {
                // insert right after the dated heading
                lines.Insert(dateIdx + 1, entry);
            }
        }

        var text = FrontmatterEditor.SetField(string.Join('\n', lines), "modified", today);
        if (!text.EndsWith("\n")) text += "\n";
        File.WriteAllText(note.Path, text, new UTF8Encoding(false));

        return CommandResult.Of(new { path = note.RelativePath }, $"✓ logged to {note.RelativePath}");
    }
}
```

Register.

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add append-to-log command (## Log / ### date convention)"`

---

### Task 19: `promote-inbox-item`

Turn an inbox entry into a `zettel`/`project`/`area`/`reference`: read the inbox file's body, create the target note (rebuilt frontmatter of the target type, preserving original `created`), then delete the original inbox file. Data: `{ from, to }`.

**Files:**
- Create: `src/Mdex.Cli/Commands/Write/PromoteInboxItemCommand.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/PromoteInboxItemTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Write;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class PromoteInboxItemTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void Promotes_inbox_to_zettel_and_removes_original()
    {
        using var v = new TestVault();
        v.WriteNote("inbox/20260601-01.md",
            "---\ntype: inbox\ncreated: 2026-06-01T09:00:00\nmodified: 2026-06-01\nbacklinks: []\n---\n\nEine rohe Idee.\n");
        var to = new PromoteInboxItemCommand().Run(
            new[] { "20260601-01", "zettel", "--name", "rohe-idee" }, Ctx(v))
            .Data().GetProperty("to").GetString();

        Assert.Equal("zettel/rohe-idee.md", to);
        Assert.False(File.Exists(Path.Combine(v.Root, "inbox", "20260601-01.md")));
        var text = v.Read(to);
        Assert.Contains("type: zettel", text);
        Assert.Contains("created: 2026-06-01T09:00:00", text);   // preserved
        Assert.Contains("Eine rohe Idee.", text);
    }
}
```

- [ ] **Step 2: Run** → expect FAIL.

- [ ] **Step 3: Implement**

```csharp
using System.Text;
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Write;

public sealed class PromoteInboxItemCommand : ICommand
{
    public string Name => "promote-inbox-item";

    static readonly Dictionary<string, string> BaseDirs = new()
    {
        ["zettel"] = "zettel", ["project"] = "projects",
        ["area"] = "areas", ["reference"] = "resources/references",
    };

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var sourceKey = reader.Positional(0) ?? throw new CommandException("promote-inbox-item needs an inbox slug");
        var targetType = reader.Positional(1) ?? throw new CommandException("promote-inbox-item needs a target type");
        if (!BaseDirs.TryGetValue(targetType, out var baseDir))
            throw new CommandException($"unknown target type '{targetType}'");

        var source = ctx.Repo.FindBySlugOrPath(sourceKey) ?? throw new CommandException($"inbox item not found: {sourceKey}");
        if (source.Frontmatter.Type != "inbox")
            throw new CommandException($"not an inbox item: {sourceKey}");

        var name = reader.Flag("--name") ?? source.Slug;
        var slug = Slugifier.Slugify(name);
        var rel = $"{baseDir}/{slug}.md";
        var abs = ctx.Guard.ResolveInside(rel);
        if (File.Exists(abs)) throw new CommandException($"target already exists: {rel}");
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);

        var created = source.Frontmatter.Created ?? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var modified = DateTime.Now.ToString("yyyy-MM-dd");
        var header = ctx.Templates.BuildNote(targetType, slug, includeBody: false, created: created, modified: modified);
        var text = header + "\n" + source.Body.TrimEnd('\n') + "\n";
        File.WriteAllText(abs, text, new UTF8Encoding(false));

        File.Delete(source.Path);   // original inbox entry removed (revertible via git history)

        return CommandResult.Of(new { from = source.RelativePath, to = rel },
            $"✓ {source.RelativePath} → {rel}");
    }
}
```

Register.

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add promote-inbox-item command"`

---

## Phase 3 — Raw filesystem commands (guarded notausgang)

### Task 20: `write-file`, `append-file`, `move`, `mkdir`, `delete`

Thin, guarded wrappers (spec §7). Every path goes through `PathGuard.ResolveInside`; nothing outside the vault is touchable. No frontmatter logic — these are the deliberate escape hatch.

**Files:**
- Create: `src/Mdex.Cli/Commands/Fs/RawFsCommands.cs`
- Modify: `CommandFactory.cs`
- Test: `tests/Mdex.Cli.Tests/RawFsCommandsTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands.Fs;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class RawFsCommandsTests
{
    static CliContext Ctx(TestVault v)
    {
        var g = new PathGuard(v.Root);
        return new CliContext { VaultRoot = v.Root, Json = false,
            Repo = new NoteRepository(v.Root, g), Guard = g, Templates = new TemplateService(v.Root) };
    }

    [Fact]
    public void WriteFile_then_AppendFile()
    {
        using var v = new TestVault();
        new WriteFileCommand().Run(new[] { "scratch/x.md", "--content", "one" }, Ctx(v));
        new AppendFileCommand().Run(new[] { "scratch/x.md", "--content", "two" }, Ctx(v));
        var text = v.Read("scratch/x.md");
        Assert.Contains("one", text);
        Assert.Contains("two", text);
    }

    [Fact]
    public void Move_and_Delete()
    {
        using var v = new TestVault();
        var ctx = Ctx(v);
        new WriteFileCommand().Run(new[] { "scratch/a.md", "--content", "x" }, ctx);
        new MoveCommand().Run(new[] { "scratch/a.md", "scratch/b.md" }, ctx);
        Assert.True(File.Exists(Path.Combine(v.Root, "scratch", "b.md")));
        new DeleteCommand().Run(new[] { "scratch/b.md" }, ctx);
        Assert.False(File.Exists(Path.Combine(v.Root, "scratch", "b.md")));
    }

    [Fact]
    public void WriteFile_rejects_escape()
    {
        using var v = new TestVault();
        Assert.Throws<CommandException>(() =>
            new WriteFileCommand().Run(new[] { "../evil.md", "--content", "x" }, Ctx(v)));
    }
}
```

- [ ] **Step 2: Run** → expect FAIL.

- [ ] **Step 3: Implement** (all five in `RawFsCommands.cs`)

```csharp
using System.Text;
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Fs;

public sealed class WriteFileCommand : ICommand
{
    public string Name => "write-file";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var path = reader.Positional(0) ?? throw new CommandException("write-file needs a path");
        var abs = ctx.Guard.ResolveInside(path);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, reader.Content(), new UTF8Encoding(false));
        return CommandResult.Of(new { path = ctx.Guard.ToRelative(abs) }, $"✓ wrote {path}");
    }
}

public sealed class AppendFileCommand : ICommand
{
    public string Name => "append-file";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var path = reader.Positional(0) ?? throw new CommandException("append-file needs a path");
        var abs = ctx.Guard.ResolveInside(path);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.AppendAllText(abs, reader.Content(), new UTF8Encoding(false));
        return CommandResult.Of(new { path = ctx.Guard.ToRelative(abs) }, $"✓ appended {path}");
    }
}

public sealed class MoveCommand : ICommand
{
    public string Name => "move";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var from = reader.Positional(0) ?? throw new CommandException("move needs <from> <to>");
        var to = reader.Positional(1) ?? throw new CommandException("move needs <from> <to>");
        var fromAbs = ctx.Guard.ResolveInside(from);
        var toAbs = ctx.Guard.ResolveInside(to);
        if (!File.Exists(fromAbs) && !Directory.Exists(fromAbs)) throw new CommandException($"not found: {from}");
        Directory.CreateDirectory(Path.GetDirectoryName(toAbs)!);
        if (Directory.Exists(fromAbs)) Directory.Move(fromAbs, toAbs);
        else File.Move(fromAbs, toAbs, overwrite: false);
        return CommandResult.Of(new { from = ctx.Guard.ToRelative(fromAbs), to = ctx.Guard.ToRelative(toAbs) },
            $"✓ {from} → {to}");
    }
}

public sealed class MkdirCommand : ICommand
{
    public string Name => "mkdir";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var path = reader.Positional(0) ?? throw new CommandException("mkdir needs a path");
        var abs = ctx.Guard.ResolveInside(path);
        Directory.CreateDirectory(abs);
        return CommandResult.Of(new { path = ctx.Guard.ToRelative(abs) }, $"✓ mkdir {path}");
    }
}

public sealed class DeleteCommand : ICommand
{
    public string Name => "delete";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var path = reader.Positional(0) ?? throw new CommandException("delete needs a path");
        var abs = ctx.Guard.ResolveInside(path);
        if (Directory.Exists(abs)) Directory.Delete(abs, recursive: true);
        else if (File.Exists(abs)) File.Delete(abs);
        else throw new CommandException($"not found: {path}");
        return CommandResult.Of(new { path = ctx.Guard.ToRelative(abs) }, $"✓ deleted {path}");
    }
}
```

Register all five in `CommandFactory.BuildRegistry()`.

- [ ] **Step 4: Run** → expect PASS. **Commit** → `git commit -am "feat: add guarded raw fs commands (write/append/move/mkdir/delete)"`

---

## Phase 4 — Packaging & docs

### Task 21: End-to-end JSON smoke test through `Program.Main`

Verify the real entry point produces a valid `--json` envelope (the MCP contract), not just the command classes in isolation.

**Files:**
- Test: `tests/Mdex.Cli.Tests/EndToEndJsonTests.cs`

- [ ] **Step 1: Failing test** (runs the built `mdext` against a TestVault)

```csharp
using System.Diagnostics;
using System.Text.Json;
namespace Mdex.Cli.Tests;

public class EndToEndJsonTests
{
    static string CliDir() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Mdex.Cli"));

    [Fact]
    public void List_tags_json_envelope_is_valid()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x]\n---\n# A\n");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = CliDir(),
        };
        foreach (var a in new[] { "run", "--", "list-tags", "--root", v.Root, "--json" }) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);

        using var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("list-tags", doc.RootElement.GetProperty("command").GetString());
    }
}
```

- [ ] **Step 2: Run** → `dotnet test --filter EndToEndJsonTests` → expect PASS (after the prior tasks; if FAIL, the dispatcher wiring in Task 7 is the place to fix).

- [ ] **Step 3: Commit** → `git commit -am "test: end-to-end --json envelope smoke test"`

---

### Task 22: README + install-as-global-tool instructions

Document the install path from spec §11 (Desktop = .NET Global Tool) so the tool is usable without `cd`-ing into the vault.

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Write the README**

````markdown
# mdex-tooling

Vault-only tooling for the **mdex** LifeOS. This repo holds three binaries
(spec: `projects/mdex-tooling-konzept/2026-06-01-mdex-tooling-design.md` in the vault):

| Binary       | .NET project   | Status            |
|--------------|----------------|-------------------|
| `mdext`      | `Mdex.Cli`     | this plan         |
| `mdex-mcp`   | `Mdex.Mcp`     | later plan        |
| `mdex-mcpctl`| `Mdex.Mcpctl`  | later plan        |

## `mdext` — the content CLI

Two output modes: human-readable, or `--json` (the stable contract the MCP
server consumes).

### Build & test
```powershell
dotnet build
dotnet test
```

### Install as a global tool (Desktop)
```powershell
dotnet pack src/Mdex.Cli -o ./nupkg
dotnet tool install --global --add-source ./nupkg Mdex.Cli
```
Then point it at the vault (resolution order: `--root` > `MDEX_ROOT` > config):
```powershell
$env:MDEX_ROOT = "C:\path\to\mdex"   # your vault root
mdext list-tags
mdext search "emergenz" --json
```
Update: `dotnet tool update --global --add-source ./nupkg Mdex.Cli`

### Commands
Read: `search` · `read-note` · `list` · `get-by-tag` · `get-by-type` ·
`get-by-date-range` · `list-tags` · `search-tags` · `find-related`
Write: `capture-inbox` · `create <type> <name>` · `append-to-note` ·
`append-to-log` · `promote-inbox-item`
Raw FS (guarded to the vault root): `write-file` · `append-file` · `move` ·
`mkdir` · `delete`

All paths are jailed to the vault root. The CLI never touches git — the vault's
`mdex-autocommit.sh` is the only committer.
````

- [ ] **Step 2: Final full test run** → `dotnet test` → expect all green.

- [ ] **Step 3: Commit** → `git commit -am "docs: README with global-tool install + command list"`

---

## Self-Review

**1. Spec coverage** (against `2026-06-01-mdex-tooling-design.md`):

| Spec item | Covered by |
|---|---|
| §5 CLI is the foundation, human + `--json` | Task 7 (dispatcher renders both modes) |
| §5 "vault-only", never touches files outside root | Task 5 PathGuard, used by every write/raw command |
| §5 `pkmt new` → intent tools | Tasks 15, 16, 19 (capture/create/promote) |
| §6 No index, fresh resolution each run | Task 8 NoteRepository (scans every call) |
| §7 Retrieval tools (9) | Tasks 9–13 |
| §7 Intent tools (5) | Tasks 15–19 |
| §7 Raw FS tools (5), jailed | Task 20 |
| §7 `append_to_log` heading convention fixed | Task 18 (`## Log` / `### YYYY-MM-DD`) |
| §3 No git logic in tool | No command invokes git anywhere (asserted by absence; README states it) |
| §11 Vault resolution `--root > MDEX_ROOT > config` | Task 4 VaultResolver |
| §11 Desktop = .NET Global Tool | Task 1 csproj (`PackAsTool`), Task 22 README |

Out of scope for this plan (correctly): token roles/auth (§2, §8 → `mdex-mcpctl`/`mdex-mcp` plans), the MCP server itself (§1, §10), `pkmt sync` (§5), the bootstrapper (Offene Punkte). Role enforcement is the **server's** job; the CLI performs mutations unconditionally — noted in Phase 2 intro.

**2. Placeholder scan:** No "TBD"/"implement later"/"add error handling" — every code step is complete. The cross-task compile dependency (TemplateService/NoteRepository before the Task 7 backbone) is resolved by the fixed implementation order in "Prerequisites & Goal condition", not a placeholder.

**3. Type consistency:** `CommandResult`, `CliContext`, `ICommand`, `NoteRepository(root, guard)`, `PathGuard.ResolveInside`/`ToRelative`, `TemplateService.BuildNote(...)`, `FrontmatterEditor.SetField(...)`, `Note.ToSummary()`, `Summaries.Result(...)`, and the JSON envelope `{ ok, command, data|error }` are used identically across all tasks. The data field naming (`path`, `slug`, `notes`, `count`, `hits`) is consistent and forms the `--json` contract.

**4. Test approach:** Command tests assert against the **serialized `--json` envelope** (via the `CommandTestExtensions.Data()` helper, Task 9) using camelCase property names — no `dynamic` access to internal anonymous types (which would throw `RuntimeBinderException` across the assembly boundary). Service/unit tests (Tasks 2–8, 14) assert on typed objects directly.

---

## Execution Handoff

Plan complete and saved to `projects/mdex-tooling-konzept/2026-06-02-mdext-cli-plan.md`. Execution options:

1. **Autonomous via `/goal`** (the intended path) — arm `/goal` with the goal condition from "Prerequisites & Goal condition" above; the agent works straight through Tasks 1–22 in the fixed order and won't stop until `dotnet test` is green and the repo is committed. Install the .NET 10 SDK first.
2. **Subagent-Driven** — a fresh subagent per task, two-stage review between tasks.
3. **Inline Execution** — execute tasks in this session via executing-plans, batched with checkpoints.

> **Note on execution environment:** This is C# work whose natural home is the separate `mdex-tooling` repo (`<REPO>`), *not* this vault. You create that repo yourself (empty folder + `git init`) and set `$REPO` to its path; Task 1 scaffolds the solution into it. No path is hard-coded, so the build runs on any machine/OS.
