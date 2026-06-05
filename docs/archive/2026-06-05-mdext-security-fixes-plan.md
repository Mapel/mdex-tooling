# mdext Security-Fixes & Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the four PoC-confirmed containment findings in `mdext` (Phase-1 security review) and harden `ArgReader`, so the vault jail actually holds before `mdex-mcp` builds its role ladder on top of it.

**Architecture:** Two surfaces. (1) `PathGuard.ResolveInside` becomes the single containment chokepoint: rejects empty/root targets, resolves symlinks before the boundary check, and compares case-correctly per OS. (2) `NoteRepository.FindBySlugOrPath` — today's bypass — is routed through that same chokepoint so slug/path resolution can no longer escape the vault. Plus `ArgReader` gains `--key=value` + `--` end-of-options (moved here from the mdex-mcp plan, as it is a pure CLI change the MCP arg-building depends on).

**Tech Stack:** .NET 10 · C# · xUnit. No new dependencies.

> **Why this plan runs first:** The mdex-mcp design assumes "every path is jailed to the vault root" and "`read` can only read inside the vault." Phase-1 PoCs showed that is currently false on the slug/path code path (`read-note ../secret` reads any `.md`; `append-to-note ../outside` writes outside; `delete ""` wipes the root). The mdex-mcp spec/plan are written assuming **this plan has already landed**.

> **Out of scope (handled in the mdex-mcp plan):** Phase-1 MEDIUM-5 / Phase-2 D-4 (unexpected exceptions leaking stack traces) is fixed at the in-process boundary `CliDispatcher.RunToJson` (mdex-mcp plan Task 1), not here — a stack trace on the local CLI's stderr to its own operator is explicitly out of the Phase-1 threat model; the leak only matters to the untrusted MCP caller.

---

## Prerequisites

- `dotnet --version` → `10.*`; `dotnet test` green before starting.
- Run commands from the repo root.
- Symlink tests use `Directory.CreateSymbolicLink` (works unprivileged on Linux/macOS; on Windows requires Developer Mode — CI here is Linux).

---

## File Structure

```
src/Mdex.Cli/
  Cli/ArgReader.cs            MOD  --key=value + -- end-of-options
  Vault/PathGuard.cs          MOD  empty/root reject · symlink canonicalization · case-correct compare
  Vault/NoteRepository.cs     MOD  route the direct-path branch through PathGuard (close the bypass)
tests/Mdex.Cli.Tests/
  ArgReaderTests.cs           NEW
  PathGuardTests.cs           NEW
  NoteContainmentTests.cs     NEW
```

---

## Task 1: Harden `ArgReader` (`--key=value` + `--` end-of-options)

Make argument parsing robust so any user value — content or a positional — survives even when it begins with `--`. Pure CLI improvement; the mdex-mcp arg-building relies on it.

**Files:**
- Modify: `src/Mdex.Cli/Cli/ArgReader.cs:9-21`
- Test: `tests/Mdex.Cli.Tests/ArgReaderTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Cli.Tests/ArgReaderTests.cs
using Mdex.Cli.Cli;
namespace Mdex.Cli.Tests;

public class ArgReaderTests
{
    [Fact]
    public void Equals_form_keeps_leading_dashes_in_value()
    {
        var r = new ArgReader(new[] { "--content=--- a dashy line" });
        Assert.Equal("--- a dashy line", r.Flag("--content"));
    }

    [Fact]
    public void Equals_form_allows_empty_value()
    {
        var r = new ArgReader(new[] { "--content=" });
        Assert.Equal("", r.Flag("--content"));
    }

    [Fact]
    public void Equals_form_splits_on_first_equals_only()
    {
        var r = new ArgReader(new[] { "--content=a=b=c" });
        Assert.Equal("a=b=c", r.Flag("--content"));
    }

    [Fact]
    public void End_of_options_makes_dashy_token_positional()
    {
        var r = new ArgReader(new[] { "--", "--weird" });
        Assert.Equal("--weird", r.Positional(0));
        Assert.False(r.Has("--weird"));
    }

    [Fact]
    public void Flags_before_separator_then_positionals_after()
    {
        var r = new ArgReader(new[] { "--maxhops=3", "--recursive", "--", "--weird-slug" });
        Assert.Equal("3", r.Flag("--maxhops"));
        Assert.True(r.Has("--recursive"));
        Assert.Equal("--weird-slug", r.Positional(0));
    }

    [Fact]
    public void Space_form_and_boolean_flag_still_work()
    {
        var r = new ArgReader(new[] { "--content", "text", "--recursive" });
        Assert.Equal("text", r.Flag("--content"));
        Assert.True(r.Has("--recursive"));
        Assert.Null(r.Flag("--recursive"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Cli.Tests --filter ArgReaderTests`
Expected: FAIL — `Equals_form_*` and `End_of_options_*` fail (value lost / token treated as flag).

- [ ] **Step 3: Rewrite the `ArgReader` constructor**

Replace the constructor body (`src/Mdex.Cli/Cli/ArgReader.cs:9-21`) with:

```csharp
    public ArgReader(string[] args)
    {
        var positionalsOnly = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (!positionalsOnly && arg == "--") { positionalsOnly = true; continue; }  // end-of-options

            if (!positionalsOnly && arg.StartsWith("--"))
            {
                var eq = arg.IndexOf('=');
                if (eq >= 0) _flags[arg[..eq]] = arg[(eq + 1)..];                         // --key=value
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    _flags[arg] = args[++i];                                              // --key value
                else _flags[arg] = null;                                                 // boolean --key
            }
            else _positionals.Add(arg);
        }
    }
```

- [ ] **Step 4: Run the new test and the full CLI suite**

Run: `dotnet test tests/Mdex.Cli.Tests`
Expected: PASS — `ArgReaderTests` green and every pre-existing test still green (the space and boolean forms are unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Cli/Cli/ArgReader.cs tests/Mdex.Cli.Tests/ArgReaderTests.cs
git commit -m "Harden ArgReader: --key=value and -- end-of-options"
```

---

## Task 2: `PathGuard` — reject empty/root targets + case-correct compare (MED-2, LOW-MED-4)

`ResolveInside("")` and `ResolveInside(".")` currently resolve to the vault root, which is *allowed* by the guard (`full != _root`), so `delete ""` wipes the whole vault. And the boundary check uses `OrdinalIgnoreCase`, which on a case-sensitive filesystem admits `/srv/VAULT/x` when the root is `/srv/vault` — a different directory outside the jail. This task rewrites `ResolveInside` to reject empty/whitespace input, reject the root itself as a target, and compare case-correctly per OS. (Symlink canonicalization is added in Task 3, layered onto the same method.)

**Files:**
- Modify: `src/Mdex.Cli/Vault/PathGuard.cs`
- Test: `tests/Mdex.Cli.Tests/PathGuardTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Cli.Tests/PathGuardTests.cs
using Mdex.Cli.Cli;
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class PathGuardTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "mdex-guard-" + Guid.NewGuid().ToString("N"));
    public PathGuardTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Inside_relative_path_resolves_under_root()
    {
        var guard = new PathGuard(_root);
        var abs = guard.ResolveInside("inbox/a.md");
        Assert.StartsWith(_root, abs);
        Assert.EndsWith("a.md", abs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_path_is_rejected(string bad)
    {
        var guard = new PathGuard(_root);
        Assert.Throws<CommandException>(() => guard.ResolveInside(bad));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("inbox/..")]
    public void Root_itself_is_rejected_as_a_target(string pointsAtRoot)
    {
        var guard = new PathGuard(_root);
        Assert.Throws<CommandException>(() => guard.ResolveInside(pointsAtRoot));
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("inbox/../../escape.md")]
    public void Relative_traversal_is_rejected(string escaping)
    {
        var guard = new PathGuard(_root);
        Assert.Throws<CommandException>(() => guard.ResolveInside(escaping));
    }

    [Fact]
    public void Different_case_absolute_sibling_is_rejected_on_case_sensitive_fs()
    {
        // Probe the filesystem: only meaningful where casing distinguishes paths.
        var probe = Path.Combine(_root, "Probe");
        Directory.CreateDirectory(probe);
        var caseInsensitive = Directory.Exists(Path.Combine(_root, "probe"));
        if (caseInsensitive) return; // not applicable on this FS (Windows/default macOS)

        // A sibling path with the root's last segment upper-cased is a DIFFERENT directory.
        var parent = Path.GetDirectoryName(_root)!;
        var flipped = Path.Combine(parent, Path.GetFileName(_root).ToUpperInvariant(), "x.md");
        var guard = new PathGuard(_root);
        Assert.Throws<CommandException>(() => guard.ResolveInside(flipped));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Cli.Tests --filter PathGuardTests`
Expected: FAIL — `Empty_or_whitespace_*` and `Root_itself_*` fail (currently resolve to root); the case test fails on Linux.

- [ ] **Step 3: Rewrite `PathGuard`**

Replace the whole body of `src/Mdex.Cli/Vault/PathGuard.cs` with (symlink canonicalization is filled in by Task 3 — for now `Canonicalize` is the identity so this task's tests pass on their own):

```csharp
using Mdex.Cli.Cli;

namespace Mdex.Cli.Vault;

public sealed class PathGuard
{
    readonly string _root;

    public PathGuard(string vaultRoot) => _root = Path.GetFullPath(vaultRoot);

    /// <summary>Resolve a vault-relative or absolute path; throw if it is empty, the root itself,
    /// or escapes the vault root. Returns the absolute (lexical) path to operate on.</summary>
    public string ResolveInside(string pathOrRelative)
    {
        if (string.IsNullOrWhiteSpace(pathOrRelative))
            throw new CommandException("Empty path is not allowed.");

        var combined = Path.IsPathRooted(pathOrRelative)
            ? pathOrRelative
            : Path.Combine(_root, pathOrRelative);
        var full = Path.GetFullPath(combined);

        // Containment is checked on the symlink-resolved form (Task 3); the lexical `full`
        // is what we return and operate on (a symlink that stays inside resolves inside anyway).
        var canonical = Canonicalize(full);
        var canonicalRoot = Canonicalize(_root);

        var cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSep = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;

        if (canonical.Equals(canonicalRoot, cmp) || !canonical.StartsWith(rootWithSep, cmp))
            throw new CommandException($"Path escapes the vault root: {pathOrRelative}");

        return full;
    }

    public string ToRelative(string absolute)
        => Path.GetRelativePath(_root, absolute).Replace(Path.DirectorySeparatorChar, '/');

    // Replaced with real symlink resolution in Task 3.
    static string Canonicalize(string path) => path;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mdex.Cli.Tests --filter PathGuardTests`
Expected: PASS — all `PathGuardTests` green (case test green on Linux, early-returns elsewhere).

- [ ] **Step 5: Run the full CLI suite (guard against regressions)**

Run: `dotnet test tests/Mdex.Cli.Tests`
Expected: PASS. If a write/fs command legitimately resolved to the root before, it now throws — none should; the fs commands target files/subdirs. Investigate any failure before continuing.

- [ ] **Step 6: Commit**

```bash
git add src/Mdex.Cli/Vault/PathGuard.cs tests/Mdex.Cli.Tests/PathGuardTests.cs
git commit -m "Harden PathGuard: reject empty/root targets, case-correct containment"
```

---

## Task 3: `PathGuard` — symlink canonicalization (MED-3)

`Path.GetFullPath` is purely lexical and does not resolve symlinks, so a pre-existing symlink inside the vault (`vault/link -> /etc`) lets even guarded reads/writes escape. Replace the identity `Canonicalize` with per-segment symlink resolution over the existing prefix: each existing component's final link target is resolved, so an intermediate or leaf symlink that points outside the root is caught by the Task-2 boundary check. Non-existing tail segments cannot be symlinks yet, so they stay lexical.

**Files:**
- Modify: `src/Mdex.Cli/Vault/PathGuard.cs` (the `Canonicalize` method)
- Test: `tests/Mdex.Cli.Tests/PathGuardTests.cs` (add cases)

- [ ] **Step 1: Add the failing tests**

Append to `PathGuardTests`:

```csharp
    [Fact]
    public void Symlink_escaping_the_vault_is_rejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "mdex-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "x.md"), "secret");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "link"), outside); // vault/link -> outside
            var guard = new PathGuard(_root);
            Assert.Throws<CommandException>(() => guard.ResolveInside("link/x.md"));
        }
        finally { try { Directory.Delete(outside, true); } catch { } }
    }

    [Fact]
    public void Symlink_staying_inside_the_vault_is_allowed()
    {
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        File.WriteAllText(Path.Combine(_root, "real", "x.md"), "ok");
        Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), Path.Combine(_root, "real")); // inside -> inside
        var guard = new PathGuard(_root);
        var abs = guard.ResolveInside("alias/x.md");
        Assert.EndsWith(Path.Combine("alias", "x.md"), abs); // lexical path returned; resolves inside
    }
```

- [ ] **Step 2: Run to verify the escape test fails**

Run: `dotnet test tests/Mdex.Cli.Tests --filter PathGuardTests`
Expected: FAIL — `Symlink_escaping_the_vault_is_rejected` fails (identity `Canonicalize` lets it through).

- [ ] **Step 3: Implement `Canonicalize`**

Replace the `Canonicalize` stub in `src/Mdex.Cli/Vault/PathGuard.cs` with:

```csharp
    /// <summary>
    /// Resolve symlinks over the existing prefix of <paramref name="fullPath"/>, segment by segment,
    /// so a symlink component pointing outside the root is reflected in the returned path. Segments
    /// that do not exist yet are appended literally (they cannot be symlinks).
    /// </summary>
    static string Canonicalize(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? "";
        var rest = fullPath[root.Length..];
        var segments = rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        foreach (var seg in segments)
        {
            current = Path.Combine(current, seg);
            FileSystemInfo? info = Directory.Exists(current) ? new DirectoryInfo(current)
                                 : File.Exists(current) ? new FileInfo(current)
                                 : null;
            if (info?.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                current = target.FullName;   // jump to the link's final target, then keep descending
        }
        return current;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mdex.Cli.Tests --filter PathGuardTests`
Expected: PASS — both symlink tests green, all earlier `PathGuardTests` still green.

- [ ] **Step 5: Commit**

```bash
git add src/Mdex.Cli/Vault/PathGuard.cs tests/Mdex.Cli.Tests/PathGuardTests.cs
git commit -m "Harden PathGuard: resolve symlinks before the containment check"
```

---

## Task 4: Close the bypass in `NoteRepository.FindBySlugOrPath` (HIGH-1)

The direct-path branch resolves with `Path.Combine(vaultRoot, slugOrPath)` and reads/writes the result with **no** containment check, so `read-note ../secret` (and absolute `.md` paths) escape the vault entirely, and slug-resolving writes (`append-to-note`, `promote`, …) write outside it. Route that branch through `PathGuard.ResolveInside` (now the hardened chokepoint from Tasks 2–3). An escaping or non-`.md` candidate simply yields no direct hit and falls through to the in-vault slug search — so traversal resolves to "not found" rather than leaking existence.

**Files:**
- Modify: `src/Mdex.Cli/Vault/NoteRepository.cs:28-38`
- Test: `tests/Mdex.Cli.Tests/NoteContainmentTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Mdex.Cli.Tests/NoteContainmentTests.cs
using Mdex.Cli.Vault;
namespace Mdex.Cli.Tests;

public class NoteContainmentTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "mdex-contain-" + Guid.NewGuid().ToString("N"));
    readonly string _root;

    public NoteContainmentTests()
    {
        _root = Path.Combine(_tmp, "vault");
        Directory.CreateDirectory(Path.Combine(_root, "inbox"));
        File.WriteAllText(Path.Combine(_root, "inbox", "note.md"), "---\ntype: inbox\n---\n# hi\n");
        File.WriteAllText(Path.Combine(_tmp, "secret.md"), "---\ntype: secret\n---\n# TOP SECRET\n"); // OUTSIDE the vault
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    NoteRepository Repo() => new(_root, new PathGuard(_root));

    [Fact]
    public void Relative_traversal_finds_nothing()
    {
        Assert.Empty(Repo().FindBySlugOrPath("../secret"));
    }

    [Fact]
    public void Absolute_path_outside_vault_finds_nothing()
    {
        Assert.Empty(Repo().FindBySlugOrPath(Path.Combine(_tmp, "secret.md")));
    }

    [Fact]
    public void Legit_inside_path_still_resolves()
    {
        var hits = Repo().FindBySlugOrPath("inbox/note");
        Assert.Single(hits);
        Assert.Equal("inbox/note.md", hits[0].RelativePath);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mdex.Cli.Tests --filter NoteContainmentTests`
Expected: FAIL — `Relative_traversal_finds_nothing` and `Absolute_path_outside_vault_finds_nothing` fail (they currently return the outside note).

- [ ] **Step 3: Route the direct-path branch through the guard**

In `src/Mdex.Cli/Vault/NoteRepository.cs`, ensure the file has `using Mdex.Cli.Cli;` at the top (for `CommandException`), then replace `FindBySlugOrPath` (`:28-38`) with:

```csharp
    public IReadOnlyList<Note> FindBySlugOrPath(string slugOrPath)
    {
        // direct path hit first — unambiguous, but must stay inside the vault
        if (TryResolveInside(slugOrPath, out var asPath) && File.Exists(asPath))
            return new[] { Build(asPath, guard.ToRelative(asPath)) };

        return LoadAll().Where(n =>
            string.Equals(n.Slug, slugOrPath, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>True only if <paramref name="slugOrPath"/> names a `.md` file inside the vault.
    /// An escaping or empty candidate yields no direct hit (caller falls back to slug search).</summary>
    bool TryResolveInside(string slugOrPath, out string abs)
    {
        abs = "";
        var rel = slugOrPath.Replace('/', Path.DirectorySeparatorChar);
        if (!rel.EndsWith(".md")) rel += ".md";
        try { abs = guard.ResolveInside(rel); return true; }
        catch (CommandException) { return false; }   // outside the vault → not a direct hit
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mdex.Cli.Tests --filter NoteContainmentTests`
Expected: PASS.

- [ ] **Step 5: Run the full CLI suite**

Run: `dotnet test tests/Mdex.Cli.Tests`
Expected: PASS — every existing read/write test still green (legit slug and in-vault path resolution is unchanged).

- [ ] **Step 6: Commit**

```bash
git add src/Mdex.Cli/Vault/NoteRepository.cs tests/Mdex.Cli.Tests/NoteContainmentTests.cs
git commit -m "Close vault-escape bypass in NoteRepository slug/path resolution"
```

---

## Task 5: End-to-end verification of the closed jail

Re-run the Phase-1 PoCs against the built CLI to confirm the holes are shut (evidence, not just unit tests).

**Files:** none (verification only).

- [ ] **Step 1: Read traversal is shut**

Run:
```bash
tmp=$(mktemp -d); vault="$tmp/vault"; mkdir -p "$vault/inbox"
printf -- '---\ntype: secret\n---\n# TOP SECRET\n' > "$tmp/secret.md"
printf -- '---\ntype: inbox\n---\n# hi\n' > "$vault/inbox/note.md"
MDEX_ROOT="$vault" dotnet run --project src/Mdex.Cli -- read-note "../secret" --json; echo "exit=$?"
rm -rf "$tmp"
```
Expected: an error envelope (`"ok": false`, "note not found: ../secret") and exit `1` — **not** the secret's contents.

- [ ] **Step 2: Write traversal is shut**

Run:
```bash
tmp=$(mktemp -d); vault="$tmp/vault"; mkdir -p "$vault/inbox"
printf -- '---\ntype: note\n---\n# victim\n' > "$tmp/outside.md"
printf -- '---\ntype: inbox\n---\n# hi\n' > "$vault/inbox/note.md"
MDEX_ROOT="$vault" dotnet run --project src/Mdex.Cli -- append-to-note "../outside" --content "x" --json; echo "exit=$?"
grep -q "x" "$tmp/outside.md" && echo "LEAK: written outside" || echo "OK: outside file untouched"
rm -rf "$tmp"
```
Expected: error envelope + exit `1`; prints `OK: outside file untouched`.

- [ ] **Step 3: Root wipe is shut**

Run:
```bash
tmp=$(mktemp -d); vault="$tmp/vault"; mkdir -p "$vault/notes"
printf -- '---\ntype: note\n---\n# keep\n' > "$vault/notes/keep.md"
MDEX_ROOT="$vault" dotnet run --project src/Mdex.Cli -- delete "" --json; echo "exit=$?"
[ -d "$vault" ] && echo "OK: vault intact" || echo "LEAK: vault deleted"
rm -rf "$tmp"
```
Expected: error envelope ("Empty path is not allowed.") + exit `1`; prints `OK: vault intact`.

- [ ] **Step 4: Full solution test run**

Run: `dotnet test`
Expected: PASS — all projects green.

- [ ] **Step 5: Commit (docs: mark Phase-1 fixes landed)**

Append to `docs/2026-06-05-mdex-security-review-plan.md` under Phase 1 a one-line status: "Fixes landed via `2026-06-05-mdext-security-fixes-plan.md`." Then:

```bash
git add docs/2026-06-05-mdex-security-review-plan.md
git commit -m "Mark Phase-1 mdext security fixes landed"
```

---

## Self-Review notes (for the implementer)

- **Finding coverage:** HIGH-1 = Task 4; MED-2 = Task 2; MED-3 = Task 3; LOW-MED-4 = Task 2; Task 1B (ArgReader) = Task 1. MED-5 / D-4 is deliberately **not** here — it is fixed at `CliDispatcher` in the mdex-mcp plan Task 1 (see the out-of-scope note in the header).
- **One chokepoint:** after Task 4, every file read/write that takes a slug-or-path goes through `PathGuard.ResolveInside`. The raw-fs commands already did; the slug resolver now does too.
- **Type consistency:** `PathGuard.ResolveInside(string) → string` and `PathGuard.ToRelative(string) → string` signatures are unchanged; only behaviour tightened. `NoteRepository(string, PathGuard)` constructor unchanged.
- **Order matters:** Task 2 introduces the `Canonicalize` identity stub; Task 3 fills it in. Do them in order so each task's tests pass when written.
```
