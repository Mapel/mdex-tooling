# Phase 3 Security Review — Tokens / Token CLI / Gateway

**Date:** 2026-06-07
**Scope:** the implemented phase-3 security surface — `Mdex.Tokens`, `Mdex.Token`, `Mdex.Gateway`
(and the shared `Mdex.Mcp.Tools` gate they rely on), plus the working-tree changes from the
[code review](2026-06-06-phase-3-code-review.md).
**Basis:** this executes the open **Phase 3** checklist of
[`2026-06-05-mdex-security-review-plan.md`](2026-06-05-mdex-security-review-plan.md) (Phases 1, 2,
2-Delta already done) and re-uses its threat IDs (D-1…D-8, N-1…N-4).

---

## Result

**No new HIGH or MEDIUM exploitable vulnerability** was found in the phase-3 code or in the
code-review working-tree changes. The working-tree diff (CR-1…CR-5) is security-neutral to slightly
**positive**: CR-1 restores the startup fail-fast (no accidental boot with an unset root), CR-3
tightens the dev bind to `127.0.0.1`, CR-4 keeps the CSPRNG token generation (256-bit, unchanged
output). One **LOW / defense-in-depth** item is actionable and is implemented (SEC-1); one item is
recorded as an accepted tradeoff (SEC-2).

## Phase-3 checklist — verification

| Checklist item (plan §Phase 3) | Status | Evidence |
|---|---|---|
| Constant-time token compare in the auth path | ✅ met | `TokenStore.FindRole` hashes first, compares hashes with `CryptographicOperations.FixedTimeEquals` (`TokenStore.cs:34-50`). Per the plan's own refinement, the channel would leak only a preimage-resistant hash — constant-time is here belt-and-suspenders and present. |
| Path-traversal end-to-end through the MCP tools | ✅ met (not re-opened) | Containment lives in `Mdex.Core` `PathGuard` (phase-1 fix); the gateway pins `vaultRoot` server-side and the AI cannot inject `--root` (positionals go after `--`). Phase 3 adds no path-handling code. |
| No token leaks in logs / exceptions | ✅ met | `BearerAuthHandler` returns only reasons ("invalid token"), never the token value (`BearerAuthHandler.cs:36-37`). `MdexBackend` maps unknown exceptions to a generic message (D-4). `TokenStore` persists only SHA-256 hashes. |
| Auth header edge cases → clean 401, no crash/leak | ✅ met | `AuthIntegrationTests`: no header, empty token, wrong scheme, no-scheme prefix, unknown token → all 401. |
| Hot-reload robustness on corrupt/partial `tokens.json` | ✅ met | `TokenStore.Load` catches all and returns empty → fail-closed (all 401); `AuthIntegrationTests.Corrupt_token_file_fails_closed_401`. Writes are atomic (temp + rename, D-6) so the watcher never reads a half-file. |
| File perms (D-3) | ✅ met | `Save` sets dir `0700` / file `0600` on POSIX; `TokenStoreTests.Saved_file_is_owner_only_on_posix`. |
| 127.0.0.1 bind (D-7) | ✅ met | `appsettings.json` Kestrel `http://127.0.0.1:3001`; README states "never a public interface without the proxy". |

## Findings

### SEC-1 — Default-deny guard verifies *declaration*, not *enforcement* (LOW, authorization / defense-in-depth)

`tests/Mdex.Mcp.Tools.Tests/ToolGateGuardTests.cs`

The role gate is **opt-in**: each tool enforces its role with an imperative first line
`auth.Require(<role>)`. Threat-model item **D-2** accepted this *only* on the condition of a guard
test that "**proves a `Require` call for each tool**". The current guard proves three things — there
are 19 tools, each carries `[MinRole]`, each takes a `ToolAuthorizer` parameter — but it does **not**
prove the body actually calls `auth.Require`, nor that it passes the role matching its `[MinRole]`.

A future tool with `[MinRole(Role.FullWrite)]` and a `ToolAuthorizer` parameter that simply **omits**
the `auth.Require(...)` line (or calls it with the wrong role) would pass every existing guard
assertion and ship as **default-ALLOW**: any authenticated token — including `read` — could invoke it.

- **Current exploitability:** none. All 19 tools today call `auth.Require` with the role matching
  their `[MinRole]` (verified by reading `ReadTools`, `IntentTools`, `RawFsTools`, and confirmed
  end-to-end by `ToolCallIntegrationTests`). This is a latent regression-class, not a live hole — hence LOW.
- **Why fix now:** it closes the exact gap D-2 was signed off against; the acceptance criterion is
  currently unmet.

**Fix (implemented):** add a behavioral guard that, for every tool whose `[MinRole]` is above the
`read` floor, invokes the tool method via reflection with a role one step **below** its declared
minimum and asserts it is refused with the authorizer's own message
(`"requires the '<min>' role"`). That message originates *only* in `ToolAuthorizer.Require(min)`, so
the assertion fails for a forgotten `Require` (backend would instead throw a different message) **and**
for a mismatched role (message would carry the wrong role string). `read`-floor tools are exempt:
`read` is the lowest rung, so a missing check there cannot escalate any authenticated caller.

### SEC-2 — Legacy SSE transport enabled on an untrusted-facing host (accepted tradeoff / hardening, not a vuln)

`src/Mdex.Gateway/Program.cs:41` — `options.EnableLegacySse = true` raises SDK diagnostic MCP9004:
"Legacy SSE transport has no built-in request backpressure and should only be used with completely
trusted clients in isolated processes."

This is a **DoS/backpressure** characteristic (explicitly out of scope for this review), **not** an
auth bypass: the legacy SSE `GET`/`POST` endpoints are still behind `MapMcp().RequireAuthorization()`,
so the bearer-token gate and per-request role seam apply identically. Disabling it would drop
compatibility for SSE-only remote clients (a functional decision), so it is **left as-is** and
recorded here as a known, bounded tradeoff. Recommendation if/when all clients support Streamable
HTTP: set `EnableLegacySse = false` to shed the unsupported, unbackpressured surface.

---

## Plan

- **SEC-1:** implement the behavioral enforcement guard in `ToolGateGuardTests`; run the suite.
- **SEC-2:** no code change — documented tradeoff (owner decision).
