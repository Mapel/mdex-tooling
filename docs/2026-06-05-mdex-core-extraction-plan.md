# Phase 0 — `Mdex.Core` Extraction + `mdext`→`mdex-cli` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the vault operations, dispatch and shared services out of the `Mdex.Cli` executable into a new reusable `Mdex.Core` class library, expose a small in-process `CommandEngine`, and rename the binary `mdext`→`mdex-cli` — without changing any observable behaviour (all existing tests stay green).

**Architecture:** Today `Mdex.Cli` is a single exe holding everything (`Cli/`, `Commands/`, `Vault/`) plus an inline dispatcher in `Program.cs`. This phase moves the shared code into `Mdex.Core` (a class library), leaves `Mdex.Cli` as a thin exe that only parses CLI flags and delegates to `CommandEngine`, and renames the produced tool. `Mdex.Core` becomes the single source of every operation that Phase 1's `Mdex.Mcp.Tools` (and both MCP hosts) will later call in-process — the foundation of the "one code path, no drift" property of the [design](2026-06-05-mdex-mcp-local-remote-design.md) (§1, §3).

**Tech Stack:** .NET 10 · C# · xUnit · YamlDotNet 18. No new dependencies. Pure refactor + one new class (`CommandEngine`).

> **Plan A of 3** for the local+remote MCP build (design: [2026-06-05-mdex-mcp-local-remote-design.md](2026-06-05-mdex-mcp-local-remote-design.md)). Plan B = `Mdex.Mcp.Tools` + `mdex-mcp` (local stdio host); Plan C = `Mdex.Tokens` + `mdex-token` + `mdex-gateway`. This phase is the prerequisite for both.

> **Security gate (Phase-2-Delta N-2, see [security plan](2026-06-05-mdex-security-review-plan.md)):** This is a *pure move*. The Phase-1 containment fixes live in `PathGuard`, `NoteRepository` and `Resolver`; their tests (`PathGuardTests`, `NoteContainmentTests`, `NoteRepositoryTests`, `ArgReaderTests`, …) must travel into the test project unchanged and stay **green**. The single PathGuard chokepoint (`Resolver.Single` → `NoteRepository.FindBySlugOrPath` → `PathGuard`) must remain the only slug/path resolution path. Do not "improve" any of this code here.

---

## Conventions for every task

- Set `$REPO` at the **start of every shell call** — PowerShell variables do not persist between separate tool calls: `$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"`.
- Build: `dotnet build "$REPO\Mdex.sln"`. Test: `dotnet test "$REPO\Mdex.sln"` (add `--filter <Name>` to scope).
- Commit after each task with the message shown (`git -C $REPO commit -am "…"`).
- Do **not** use `cd` in compound commands (can trigger a permission prompt).
- The whole repo is path-free in code; only this plan's shell lines name `$REPO`.

---

## File Structure (end state of this phase)

```
src/
  Mdex.Core/                       NEW class library — the single operations source
    Mdex.Core.csproj
    Cli/        ArgReader · CommandRegistry · ICommand (+ CommandResult, CommandException, CliContext)
                · JsonEnvelope · CommandEngine (NEW)
    Commands/   CommandFactory · Resolver · Read/* · Write/* · Fs/*
    Vault/      VaultResolver · PathGuard · NoteRepository · Frontmatter(+Parser/Editor)
                · Slugifier · TemplateService
  Mdex.Cli/                        thin exe → produces `mdex-cli`
    Mdex.Cli.csproj                references Mdex.Core; AssemblyName/ToolCommandName = mdex-cli
    Program.cs                     parse global flags + render; delegates to CommandEngine
tests/
  Mdex.Cli.Tests/                  retargeted to reference Mdex.Core (name unchanged)
```

Namespaces move `Mdex.Cli.*` → `Mdex.Core.*` for the extracted code. `Program.cs` keeps living in the `Mdex.Cli` exe (top-level statements, no namespace) but now `using Mdex.Core.*`. The test project keeps its name `Mdex.Cli.Tests` (it is the safety net — we churn it as little as possible) and only swaps its project reference + `using`s.

---

## Task 1: Create the `Mdex.Core` class library and wire it into the solution

**Files:**
- Create: `src/Mdex.Core/Mdex.Core.csproj`
- Modify: `Mdex.sln`

- [ ] **Step 1: Scaffold the library**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
dotnet new classlib -n Mdex.Core -o "$REPO\src\Mdex.Core"
Remove-Item "$REPO\src\Mdex.Core\Class1.cs"
dotnet sln "$REPO\Mdex.sln" add "$REPO\src\Mdex.Core\Mdex.Core.csproj"
```

- [ ] **Step 2: Replace `src/Mdex.Core/Mdex.Core.csproj`** with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Mdex.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="YamlDotNet" Version="18.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build "$REPO\Mdex.sln"`
Expected: `Build succeeded`, 0 errors (the empty library compiles; `Mdex.Cli` + tests untouched).

- [ ] **Step 4: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "chore: add empty Mdex.Core class library"
```

---

## Task 2: Move `Cli/`, `Commands/`, `Vault/` into `Mdex.Core` and retarget references

This is a **mechanical move + namespace rename**. No logic changes. The task ends with the whole solution building and **all existing tests green** — that green is the Phase-1 security regression gate (N-2).

**Files:**
- Move (git mv): `src/Mdex.Cli/Cli/`, `src/Mdex.Cli/Commands/`, `src/Mdex.Cli/Vault/` → `src/Mdex.Core/`
- Modify: every moved `*.cs` (namespace `Mdex.Cli.*`→`Mdex.Core.*`)
- Modify: `src/Mdex.Cli/Mdex.Cli.csproj`, `src/Mdex.Cli/Program.cs`
- Modify: `tests/Mdex.Cli.Tests/Mdex.Cli.Tests.csproj` + every test `*.cs` (`using Mdex.Cli.*`→`using Mdex.Core.*`)

- [ ] **Step 1: Move the three folders with git (preserves history)**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO mv src/Mdex.Cli/Cli      src/Mdex.Core/Cli
git -C $REPO mv src/Mdex.Cli/Commands src/Mdex.Core/Commands
git -C $REPO mv src/Mdex.Cli/Vault    src/Mdex.Core/Vault
```

`Program.cs` is the **only** file left under `src/Mdex.Cli/` (besides the csproj).

- [ ] **Step 2: Rename namespaces in the moved code** (`Mdex.Cli`→`Mdex.Core`, covers both `namespace` and `using` lines). Encoding-preserving bulk replace:

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
Get-ChildItem "$REPO\src\Mdex.Core" -Recurse -Filter *.cs | ForEach-Object {
  $t = [IO.File]::ReadAllText($_.FullName)
  [IO.File]::WriteAllText($_.FullName, ($t -replace 'Mdex\.Cli', 'Mdex.Core'))
}
```

- [ ] **Step 3: Make `Mdex.Cli` reference `Mdex.Core`.** Replace `src/Mdex.Cli/Mdex.Cli.csproj` with (keeps the `mdext` name for now — the rename is Task 4; drops YamlDotNet, now transitive via Core):

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
    <ProjectReference Include="..\Mdex.Core\Mdex.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Update `Program.cs` usings + the `GroupOf` namespace literals.** In `src/Mdex.Cli/Program.cs` change the three `using` lines and the two namespace strings inside `GroupOf` (leave the rest of the body exactly as-is — its refactor is Task 3):

Replace the top three lines
```csharp
using Mdex.Cli.Cli;
using Mdex.Cli.Commands;
using Mdex.Cli.Vault;
```
with
```csharp
using Mdex.Core.Cli;
using Mdex.Core.Commands;
using Mdex.Core.Vault;
```
and inside `GroupOf` replace
```csharp
        "Mdex.Cli.Commands.Read" => "Read",
        "Mdex.Cli.Commands.Write" => "Write",
        "Mdex.Cli.Commands.Fs" => "Raw FS (guarded to vault root)",
```
with
```csharp
        "Mdex.Core.Commands.Read" => "Read",
        "Mdex.Core.Commands.Write" => "Write",
        "Mdex.Core.Commands.Fs" => "Raw FS (guarded to vault root)",
```

- [ ] **Step 5: Retarget the test project reference.** In `tests/Mdex.Cli.Tests/Mdex.Cli.Tests.csproj` change
```xml
    <ProjectReference Include="..\..\src\Mdex.Cli\Mdex.Cli.csproj" />
```
to
```xml
    <ProjectReference Include="..\..\src\Mdex.Core\Mdex.Core.csproj" />
```

- [ ] **Step 6: Update the test `using`s.** The tests reference the moved types via `using Mdex.Cli.…`. Rewrite only the `using` lines (leave `namespace Mdex.Cli.Tests` as-is; the `EndToEndJsonTests` `dotnet run` path also stays — it still points at `src/Mdex.Cli`):

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
Get-ChildItem "$REPO\tests\Mdex.Cli.Tests" -Recurse -Filter *.cs | ForEach-Object {
  $t = [IO.File]::ReadAllText($_.FullName)
  [IO.File]::WriteAllText($_.FullName, ($t -replace 'using Mdex\.Cli\.', 'using Mdex.Core.'))
}
```

- [ ] **Step 7: Build, then run the full suite**

Run: `dotnet build "$REPO\Mdex.sln"` → Expected: `Build succeeded`, 0 errors.
Run: `dotnet test "$REPO\Mdex.sln"` → Expected: **all tests pass** (same count as before the move). If any containment test (`PathGuardTests`, `NoteContainmentTests`, `NoteRepositoryTests`) fails, the move altered behaviour — revert and redo the move cleanly; do not edit the security logic.

- [ ] **Step 8: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO add -A
git -C $REPO commit -m "refactor: move Cli/Commands/Vault into Mdex.Core (no behaviour change)"
```

---

## Task 3: Extract `CommandEngine` and delegate `Program.cs` to it

The dispatch logic (resolve vault → build `CliContext` → look up verb → run) is currently inline in `Program.cs`, so nothing else can reuse it. Extract it into a testable `CommandEngine` in `Mdex.Core`. This is the in-process entry Plan B's MCP tools call. The engine returns a `CommandResult` (the `Data` **object**, not a JSON string) — that is the design's §3 refinement over Plan-2's `RunToJson`.

**Files:**
- Create: `src/Mdex.Core/Cli/CommandEngine.cs`
- Test: `tests/Mdex.Cli.Tests/CommandEngineTests.cs`
- Modify: `src/Mdex.Cli/Program.cs`

- [ ] **Step 1: Write the failing test** (`tests/Mdex.Cli.Tests/CommandEngineTests.cs`)

```csharp
using Mdex.Core.Cli;
namespace Mdex.Cli.Tests;

public class CommandEngineTests
{
    [Fact]
    public void Runs_a_no_vault_command()
    {
        var engine = CommandEngine.Default();
        var result = engine.Run("version", Array.Empty<string>(), rootFlag: null, json: false);
        Assert.Contains("version", System.Text.Json.JsonSerializer.Serialize(result.Data));
    }

    [Fact]
    public void Runs_a_vault_command_against_a_root()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x]\n---\n# A\n");
        var engine = CommandEngine.Default();
        var result = engine.Run("list-tags", Array.Empty<string>(), rootFlag: v.Root, json: false);
        Assert.Contains("\"tag\":\"x\"",
            System.Text.Json.JsonSerializer.Serialize(result.Data).Replace(" ", ""));
    }

    [Fact]
    public void Unknown_command_throws_CommandException()
    {
        var engine = CommandEngine.Default();
        Assert.Throws<CommandException>(
            () => engine.Run("nope", Array.Empty<string>(), rootFlag: null, json: false));
    }
}
```

- [ ] **Step 2: Run → expect FAIL** (`CommandEngine` missing)

Run: `dotnet test "$REPO\Mdex.sln" --filter CommandEngineTests`
Expected: FAIL (does not compile / type not found).

- [ ] **Step 3: Implement `CommandEngine`** (`src/Mdex.Core/Cli/CommandEngine.cs`)

```csharp
using Mdex.Core.Commands;
using Mdex.Core.Vault;

namespace Mdex.Core.Cli;

/// <summary>
/// The single in-process entry point for running a verb. The CLI (Program.cs) and —
/// in later phases — the MCP tool layer both go through here, so there is exactly one
/// implementation of "resolve vault, build context, dispatch". Returns the operation's
/// <see cref="CommandResult"/> (the Data object); callers choose how to render it.
/// </summary>
public sealed class CommandEngine
{
    readonly CommandRegistry _registry;

    public CommandEngine(CommandRegistry registry) => _registry = registry;

    /// <summary>Engine over the standard command set.</summary>
    public static CommandEngine Default() => new(CommandFactory.BuildRegistry());

    /// <summary>Registered commands in registration order (for help/tool generation).</summary>
    public IReadOnlyList<ICommand> Commands => _registry.All;

    /// <summary>Look up a verb without running it (null if unknown).</summary>
    public ICommand? Find(string verb) => _registry.Get(verb);

    /// <summary>
    /// Run <paramref name="verb"/> with <paramref name="args"/>. Resolves the vault from
    /// <paramref name="rootFlag"/> (else MDEX_ROOT/config) for commands that need it.
    /// Throws <see cref="CommandException"/> for an unknown verb or a user-facing failure;
    /// other exceptions propagate (callers sanitise — CLI prints, MCP layer maps).
    /// </summary>
    public CommandResult Run(string verb, string[] args, string? rootFlag, bool json)
    {
        var command = _registry.Get(verb)
            ?? throw new CommandException($"unknown command '{verb}'");
        var ctx = BuildContext(rootFlag, json, command.NeedsVault);
        return command.Run(args, ctx);
    }

    /// <summary>Build the execution context; vault services are only wired when needed.</summary>
    public CliContext BuildContext(string? rootFlag, bool json, bool needsVault)
    {
        if (!needsVault)
            return new CliContext
            {
                VaultRoot = "", Json = json,
                Repo = null!, Guard = null!, Templates = null!,
            };

        var root = VaultResolver.Resolve(rootFlag);
        var guard = new PathGuard(root);
        return new CliContext
        {
            VaultRoot = root, Json = json,
            Repo = new NoteRepository(root, guard),
            Guard = guard,
            Templates = new TemplateService(root),
        };
    }
}
```

- [ ] **Step 4: Run → expect PASS**

Run: `dotnet test "$REPO\Mdex.sln" --filter CommandEngineTests` → Expected: PASS (3 tests).

- [ ] **Step 5: Rewrite `Program.cs` to delegate to the engine.** Replace the whole file `src/Mdex.Cli/Program.cs` with:

```csharp
using Mdex.Core.Cli;

var engine = CommandEngine.Default();

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine("mdex-cli <command> [args] [--root <path>] [--json]");
    Console.WriteLine();
    Console.WriteLine("Global flags:");
    Console.WriteLine("  --root <path>   vault root (else $MDEX_ROOT, else config)");
    Console.WriteLine("  --json          emit the JSON envelope instead of human text");
    Console.WriteLine();

    static string GroupOf(ICommand c) => c.GetType().Namespace switch
    {
        "Mdex.Core.Commands.Read" => "Read",
        "Mdex.Core.Commands.Write" => "Write",
        "Mdex.Core.Commands.Fs" => "Raw FS (guarded to vault root)",
        _ => "Meta",
    };

    var width = Math.Min(engine.Commands.Max(c => c.Usage.Length), 44);
    foreach (var group in engine.Commands.GroupBy(GroupOf))
    {
        Console.WriteLine(group.Key + ":");
        foreach (var c in group)
        {
            if (c.Usage.Length <= width)
                Console.WriteLine($"  {c.Usage.PadRight(width)}  {c.Summary}");
            else
            {
                Console.WriteLine($"  {c.Usage}");
                Console.WriteLine($"  {new string(' ', width)}  {c.Summary}");
            }
        }
        Console.WriteLine();
    }
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

try
{
    var result = engine.Run(verb, cleaned.ToArray(), rootFlag, json);
    Console.WriteLine(json ? JsonEnvelope.Ok(verb, result.Data) : result.HumanText);
    return 0;
}
catch (CommandException ex)
{
    Console.Error.WriteLine(json ? JsonEnvelope.Err(verb, ex.Message, ex.Details) : $"Error: {ex.Message}");
    return 1;
}
```

> Behaviour is identical to before: unknown verb now throws `CommandException("unknown command '<verb>'")` and is caught by the same handler → same stderr message / same `--json` error envelope.

- [ ] **Step 6: Build + full suite**

Run: `dotnet build "$REPO\Mdex.sln"` → `Build succeeded`.
Run: `dotnet test "$REPO\Mdex.sln"` → **all tests pass** (incl. `EndToEndJsonTests`, which still spawns `dotnet run` in `src/Mdex.Cli`).

- [ ] **Step 7: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO commit -am "refactor: extract CommandEngine; Program.cs delegates to it"
```

---

## Task 4: Rename the binary `mdext` → `mdex-cli`

Pure rename of the produced tool + the user-visible strings. The project file *name* (`Mdex.Cli.csproj`) and `PackageId` stay — only the assembly/tool command and the literal `mdext` strings change.

**Files:**
- Modify: `src/Mdex.Cli/Mdex.Cli.csproj`, `src/Mdex.Core/Commands/CommandFactory.cs` (`VersionCommand` text), `README.md`

- [ ] **Step 1: Rename assembly + tool command.** In `src/Mdex.Cli/Mdex.Cli.csproj` change the two lines:
```xml
    <AssemblyName>mdext</AssemblyName>
```
→
```xml
    <AssemblyName>mdex-cli</AssemblyName>
```
and
```xml
    <ToolCommandName>mdext</ToolCommandName>
```
→
```xml
    <ToolCommandName>mdex-cli</ToolCommandName>
```

- [ ] **Step 2: Update the version string.** In `src/Mdex.Core/Commands/CommandFactory.cs`, `VersionCommand.Run` returns `"mdext 0.1.0"`. Change it:
```csharp
        => CommandResult.Of(new { version = "0.1.0" }, "mdext 0.1.0");
```
→
```csharp
        => CommandResult.Of(new { version = "0.1.0" }, "mdex-cli 0.1.0");
```

- [ ] **Step 3: Update `README.md`.** Replace the user-facing `mdext` mentions with `mdex-cli`: the binary/table row, the global-tool `dotnet tool install`/`update` command name, and the `mdext list-tags` / `mdext search …` examples. (The `Mdex.Cli` *project* name stays.) Verify none remain:

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
Select-String -Path "$REPO\README.md" -Pattern '\bmdext\b'
```
Expected after editing: **no matches**.

- [ ] **Step 4: Build, test, and manually confirm the new command name**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
dotnet test "$REPO\Mdex.sln"
dotnet run --project "$REPO\src\Mdex.Cli" -- version
```
Expected: all tests pass; the `version` run prints `mdex-cli 0.1.0`.

- [ ] **Step 5: Commit**

```powershell
$REPO = "C:\Users\MichaelDiesen\repos\mdex-tooling"
git -C $REPO commit -am "refactor: rename binary mdext -> mdex-cli"
```

---

## Done-condition for Phase 0

- `dotnet build "$REPO\Mdex.sln"` succeeds; `dotnet test "$REPO\Mdex.sln"` reports **all tests green** (every pre-existing test plus the 3 `CommandEngineTests`).
- `Mdex.Core` holds all operations/services; `Mdex.Cli` is a thin exe over it producing `mdex-cli`.
- `CommandEngine.Run(verb, args, rootFlag, json)` returns a `CommandResult` — the in-process seam Plan B builds on.
- Containment tests unchanged and green (security gate N-2 satisfied).
- `git -C $REPO status` clean; four commits (one per task).

---

## Self-Review (against the design)

- **§1 (4-Binary-Familie, `Mdex.Core`):** Task 1–2 create the lib and move operations in; `Mdex.Cli`→`mdex-cli` (Task 4). ✔
- **§3 (In-Process: Data-Objekt statt Envelope-Parse):** `CommandEngine.Run` returns `CommandResult` (Data object), not a JSON string. ✔
- **Security gate N-2 (Core-Extraktion = Regressionsrisiko):** pure move; containment tests travel and stay green (Task 2 Step 7, Task 3 Step 6, Task 4 Step 4). ✔
- **Out of scope here (correct):** `Mdex.Mcp.Tools`, hosts, tokens — Plan B/C. The `[MinRole]` attribute and role authorizer are **not** introduced in this phase; `CommandEngine` carries no role concept yet (roles enter at the tool layer in Plan B).
- **Placeholder scan:** none — every step has exact files, commands and code.

---

## Execution Handoff

Plan complete and saved to `docs/2026-06-05-mdex-core-extraction-plan.md`. Two execution options:

1. **Subagent-Driven (recommended)** — a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute the tasks in this session with checkpoints for review.

Which approach?
