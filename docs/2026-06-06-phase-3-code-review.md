# Phase 3 Code Review — Tokens / Token CLI / Gateway

**Scope:** `9ee5bc7..HEAD` (commits `063424c`…`83d7e21`) — `Mdex.Tokens`, `Mdex.Token`,
`Mdex.Gateway` and their tests.
**Date:** 2026-06-06
**Baseline before changes:** build 0 errors / 2 warnings; `dotnet test` 136 passed, 0 failed.

This is the **correctness + cleanup** pass. Security-posture items (e.g. legacy-SSE on an
untrusted transport, token-file permission window, fixed-time-compare early-return) are
deliberately deferred to the separate security review.

---

## Findings

### CR-1 — Gateway startup root-guard is dead code (Medium, correctness)

`src/Mdex.Gateway/Program.cs:12-14`

```csharp
var vaultRoot = builder.Configuration["Mdex:Root"]
    ?? Environment.GetEnvironmentVariable("MDEX_ROOT")
    ?? throw new InvalidOperationException("Set Mdex:Root in config or MDEX_ROOT env var.");
```

`appsettings.json` ships `"Mdex": { "Root": "" }`, so `Configuration["Mdex:Root"]` returns the
**empty string**, not `null`. `??` only short-circuits on `null`, so the env-var fallback and the
fail-fast `throw` are **both unreachable**; `vaultRoot` is always `""` unless overridden via
`Mdex__Root`.

It is not a vault-resolution outage in practice — `VaultResolver.Resolve` independently re-reads
`MDEX_ROOT` (it skips whitespace candidates), so the systemd unit's `MDEX_ROOT` still works. But the
**intended fail-fast is gone**: a fully unconfigured gateway boots successfully and then fails
*every* tool call at runtime ("Vault root not found") instead of refusing to start with a clear
message.

It is also inconsistent with the **very next line** (`tokenPath`, `Program.cs:15-16`), which
correctly uses `string.IsNullOrWhiteSpace`.

**Failure scenario:** Deploy with neither `Mdex:Root` set nor `MDEX_ROOT` exported → process starts
"healthy", passes liveness, then 100% of tool invocations error. Operator sees per-request failures
instead of a boot-time config error.

**Fix:** mirror the `tokenPath` pattern — treat empty/whitespace as unset.

```csharp
var vaultRoot = builder.Configuration["Mdex:Root"];
if (string.IsNullOrWhiteSpace(vaultRoot)) vaultRoot = Environment.GetEnvironmentVariable("MDEX_ROOT");
if (string.IsNullOrWhiteSpace(vaultRoot))
    throw new InvalidOperationException("Set Mdex:Root in config or MDEX_ROOT env var.");
```

---

### CR-2 — No end-to-end test of an authorized tool *call* / role enforcement over HTTP (Medium, coverage)

`tests/Mdex.Gateway.Tests/AuthIntegrationTests.cs`

Phase 2 shipped E2E "role-refusal through the tools" tests (commit `ffc4de2`). The gateway, which is
the security-critical HTTP re-host of that same surface, has **no** test that actually *invokes a
tool*. Every integration test hits `tools/list` (which does not run `auth.Require`) or a 401 path.

That leaves the per-request scoped DI seam — `HttpRoleProvider` (scoped) → `ToolAuthorizer` (scoped),
resolved from the request's `RequestServices` by the MCP SDK — **unverified end-to-end**. A
regression that resolved these from the root provider (captive dependency, no `HttpContext`) would
make *all* tool calls throw and no current test would catch it. Likewise, nothing asserts that a
`read` token is refused a `fullwrite` tool over the wire.

**Fix:** add an integration test that performs a real `tools/call`:
- a `read` tool with a `read` token → succeeds (not an auth error);
- a `fullwrite`/raw-fs tool with a `read` token → refused (role error surfaced), proving the role
  claim flows through the scoped authorizer per request.

---

### CR-3 — launchSettings noise + port divergence (Low, cleanup)

`src/Mdex.Gateway/Properties/launchSettings.json`

- `launchBrowser: true` on an API/MCP server opens a browser to an endpoint that just returns 401 —
  pure noise for `dotnet run`.
- `applicationUrl` (`http://localhost:5110`, `https://localhost:7106`) diverges from the configured
  Kestrel endpoint (`127.0.0.1:3001` in `appsettings.json`), so the dev port silently differs from
  the documented/prod one.

**Fix:** set `launchBrowser: false`; align the http profile's `applicationUrl` to
`http://127.0.0.1:3001` to match `appsettings.json`.

---

### CR-4 — Hand-rolled base64url where the BCL now has one (Low, reuse)

`src/Mdex.Tokens/TokenGenerator.cs:14-15`

```csharp
Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
```

.NET 9+ ships `System.Buffers.Text.Base64Url.EncodeToString(bytes)`, which produces the identical
url-safe, unpadded output. Token format is unchanged (and only hashes are persisted, so no
compatibility concern).

**Fix:** replace the manual transform with `Base64Url.EncodeToString(bytes)`.

---

### CR-5 — `TokenStore._entries` reassigned across threads without a barrier (Low, robustness)

`src/Mdex.Tokens/TokenStore.cs:22`

`Reload()` reassigns the `_entries` reference on a `Timer` thread (the watcher); `FindRole` reads it
on request threads. Reference assignment is atomic and `foreach` captures the reference, so there is
no torn read or `InvalidOperationException` — but the field is non-`volatile`, so timely visibility
of a reload to other threads is not formally guaranteed (benign on x86, but free to make correct).

**Fix:** mark the field `volatile List<TokenEntry> _entries;` (or read it into a local in
`FindRole`). Low priority; correctness-by-construction.

---

## Plan

Implement **CR-1, CR-2, CR-3, CR-4, CR-5** (all cheap, no behavioural risk). Re-run the full suite;
CR-2 also serves as live verification that the scoped-DI seam resolves correctly. Then proceed to the
security review.
