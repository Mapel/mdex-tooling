# mdex-tooling

Vault-only tooling for the **mdex** LifeOS — a local+remote MCP family over one
shared core (design: [`docs/2026-06-05-mdex-mcp-local-remote-design.md`](docs/2026-06-05-mdex-mcp-local-remote-design.md)):

| Binary          | .NET project    | Where  | Status   |
|-----------------|-----------------|--------|----------|
| `mdex-cli`      | `Mdex.Cli`      | local  | built    |
| `mdex-mcp`      | `Mdex.Mcp`      | local  | built    |
| `mdex-gateway`  | `Mdex.Gateway`  | server | built    |
| `mdex-token`    | `Mdex.Token`    | server | built    |

All four are thin hosts over `Mdex.Core` (the single operations source); the two
MCP hosts share the same tool surface (`Mdex.Mcp.Tools`).

## `mdex-cli` — the content CLI

Two output modes: human-readable, or `--json` (the stable contract the MCP
server consumes).

### Build & test
```bash
dotnet build
dotnet test
```

### Install as a global tool (Desktop)
```bash
dotnet pack src/Mdex.Cli -o ./nupkg
dotnet tool install --global --add-source ./nupkg Mdex.Cli
```
Then point it at the vault (resolution order: `--root` > `MDEX_ROOT` > config):
```bash
export MDEX_ROOT="/path/to/mdex"   # your vault root
mdex-cli list-tags
mdex-cli search "emergenz" --json
```
Update: `dotnet tool update --global --add-source ./nupkg Mdex.Cli`

### Commands

`mdex-cli --help` prints this same reference. Every command also accepts the two
**global flags**:

| Flag | Meaning |
|------|---------|
| `--root <path>` | Vault root. Resolution order: `--root` > `$MDEX_ROOT` > config. |
| `--json` | Emit the stable JSON envelope instead of human text (see below). |

The `--json` envelope is the stable contract the MCP server consumes. Success is
`{ ok: true, command, data }`; failure is `{ ok: false, command, error, details? }`,
where `error` is the human message and the optional `details` carries structured
context — e.g. an ambiguous slug returns `details.candidates: [paths]` so the caller
can offer the choice without parsing the message. Output is real UTF-8 (no `\uXXXX`).

A note is addressed by **slug** (the filename without `.md`, e.g. `make-buy-spektrum`)
or by a **vault-relative path** (e.g. `zettel/make-buy-spektrum.md`). If a bare slug
is shared by several notes, read commands list the candidates and write commands
refuse — pass an explicit path to disambiguate. Content for write commands comes
from `--content <text>` or piped stdin.

The role split is **convention-preserving vs. convention-breaking**: write-intent may do
anything that keeps frontmatter/structure intact; fullwrite is the deliberate escape hatch.
The system owns the frontmatter — the AI only ever touches `tags`.

#### Read

| Command | Description |
|---------|-------------|
| `search <query> [--match all\|any]` | Full-text substring search over title + body (one hit per note). `--match` combines whitespace terms (default `all`). |
| `read-note <target>...` | Print one or more notes' frontmatter and body. Partial success: missing targets are listed under `errors`. |
| `find-notes [--tag X...] [--type T] [--from D] [--to D] [--field created\|modified] [--match all\|any]` | Find notes by tag, type, and/or date range. Different filters AND; `--match` governs how `--tag` values combine (default `all`). Replaces the former `get-by-tag` / `get-by-type` / `get-by-date-range`. |
| `list-tags [pattern] [--top N]` | All tags with their counts, sorted by frequency. `pattern` filters tag names; `--top N` caps the list. |
| `list [path] [--depth N\|all]` | File/dir tree under an optional vault-relative subpath. Default depth 1 (direct children); `--depth N` descends N levels, `--depth all` is fully recursive. |
| `find-related <slug\|path> [--depth N\|all] [--direction in\|out\|both]` | Notes linked via `[[wikilinks]]`. Default depth 1, both directions; results are direction-labelled. Backlinks = `--direction in --depth 1`. |
| `list-todos [--status open\|done\|all] [--in <path>]` | Collect checkboxes (`- [ ]` / `- [x]`) across all notes. Default open only; `--in` scopes to a subtree. |

#### Write (write-intent — convention-preserving)

| Command | Description |
|---------|-------------|
| `capture-inbox --title <t> (--content <text> \| stdin)` | Create an inbox entry. Title is **required** (sets the H1 and derives `inbox/YYYYMMDD-<title-slug>.md`). |
| `create <type> <name> [--tag X...] [--no-body] [--body <tpl>]` | Create a note. `type` ∈ `zettel\|project\|area\|reference`; non-zettel `name` may contain a subpath. `--tag` sets initial tags; `--no-body` writes frontmatter only; `--body <tpl>` selects a body template. |
| `append-to-note <slug\|path> (--content <text> \| stdin) [--tag X...]` | Append a section and bump `modified`. `--tag` merges tags. |
| `append-to-log <slug\|path> (--content <text> \| stdin)` | Add a dated bullet under the note's `## Log` section (created if absent). |
| `promote-inbox-item <inbox> <type> [--name <name>]` | Move an inbox item into a typed folder, repairing missing/incomplete frontmatter. `--name` overrides the target slug. |
| `move <from> <to>` | Move/rename a file or directory. Re-derives `type` from the destination folder (archive keeps its type), bumps `modified`, creates parent dirs, never overwrites. Covers rename + move-to-archive + general relocation. |
| `replace-body <slug\|path> (--content <text> \| stdin)` | Replace a note's body, preserving its frontmatter (and bumping `modified`). |
| `set-tags <slug\|path> (--add \| --remove \| --set) <tag>...` | Mutate a note's tags — the only AI path to frontmatter. Exactly one mode required. |
| `mkdir <path>` | Create a directory (deliberate empty-folder scaffolding). |
| `set-todo <slug\|path> --match <text> --status open\|done` | Set a checkbox's status by text-match. Fails on 0 or 2+ matches (the human disambiguates). |
| `commit-and-push --message <msg>` | Atomically commit all changes, rebase on the remote, and push. Use after a logical unit of work (semantic message, no `auto:` prefix). |

#### Raw FS (fullwrite — convention-breaking, guarded to the vault root)

| Command | Description |
|---------|-------------|
| `write-file <path> (--content <text> \| stdin)` | Create or overwrite a file — the convention escape hatch (frontmatter/templates ignored). |
| `delete <path>` | Delete a file or directory (recursive); the only pre-commit irreversible operation. |

All paths are jailed to the vault root. `commit-and-push` is the one write-intent tool whose
subject is the repo state rather than a note — every other CLI command leaves git to the
vault's `mdex-autocommit.sh`.

## mdex-mcp (local MCP server)

`mdex-mcp` exposes the mdex operations as MCP tools over **stdio** for AI agents that run
on this machine against the **local checkout** (Claude Code, Cowork, Codex CLI). It calls
`Mdex.Core` in-process — no network, no token, no ASP.NET — and stages every tool by role.

**Roles (ladder, fail-closed — you must pass `--role`):** `read` ⊂ `write-intent` ⊂ `fullwrite`
- `read` — the 7 retrieval tools.
- `write-intent` — adds the 11 intent tools (capture/create/append/promote/move/replace-body/
  set-tags/mkdir/set-todo/commit-and-push; convention-preserving).
- `fullwrite` — adds the 2 raw-fs tools (write-file/delete; convention-breaking).

The role also drives **visibility**: `tools/list` only advertises the tools the role may use, so
the agent never sees (and won't reach for) a tool above its grant. This is layered on top of —
not a replacement for — the in-tool gate, which still refuses any out-of-role call directly.

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

## mdex-gateway (remote MCP server)

`mdex-gateway` exposes the **same** mdex tools as `mdex-mcp`, but over HTTP for off-machine
clients (phone, web, Langdock). It hosts `Mdex.Mcp.Tools` in-process and gates every tool by
the role carried by the caller's credential. It is the **sibling** of the local stdio host —
same tools, different transport (design §2).

**Roles (ladder):** `read` ⊂ `write-intent` ⊂ `fullwrite` (7 read / +11 intent / +2 raw-fs tools).
Each credential's role also scopes its `tools/list`: a caller only sees the tools its role may
use, while the in-tool gate still refuses any out-of-role call (visibility layered over
enforcement).
**Transport:** Streamable HTTP (default) + legacy SSE.
**Config:** `MDEX_ROOT` (vault), `Mdex:TokenStore` or `MDEX_GATEWAY_TOKENS`
(default `~/.config/mdex-gateway/tokens.json`), Kestrel URL in `appsettings.json`.
Run behind a TLS reverse proxy; the server binds `127.0.0.1` only — never a public interface
without the proxy (credentials over plain HTTP would be in clear text).

**Credential hygiene (N-4):** configure gateway credentials **only** in off-machine clients. On a
machine with a local checkout, use `mdex-mcp` (local) — never put remote credentials in a local
agent config.

### Auth: two paths (hybrid)

The gateway accepts **either** credential type on the same endpoint:

| Path | For | How |
|------|-----|-----|
| **Custom bearer token** | Langdock, scripts, any client that accepts manual tokens | `mdex-token new` → paste the raw token as `Authorization: Bearer <token>` |
| **OAuth 2.1** (Authorization Code + PKCE) | claude.ai, ChatGPT, any standard MCP client | Client auto-discovers the AS and drives the flow; you only configure `client_id` + `client_secret` once |

Both paths produce the same role-ladder enforcement — the gateway maps OAuth scopes to roles
internally; `HttpRoleProvider` and `ToolAuthorizer` see no difference.

### OAuth 2.1 setup

The gateway is its own Authorization Server (collocated, no extra service). Clients are
configured statically in `appsettings.json` under `Mdex:OAuthClients`:

```json
"Mdex": {
  "OAuthClients": [
    {
      "ClientId": "mdex-write-intent",
      "ClientSecret": "your-secret-here",
      "DisplayName": "mdex write-intent",
      "Scopes": ["mdex:read", "mdex:write-intent"],
      "RedirectUris": [
        "https://claude.ai/api/mcp/auth_callback",
        "https://chatgpt.com/connector_platform_oauth_redirect"
      ]
    }
  ]
}
```

One client per role. Multiple `RedirectUris` let the same `client_id`/`secret` pair be reused
across services — enter the same credentials in both claude.ai's and ChatGPT's connector
settings.

**Scopes map to roles:**

| Scope | Role |
|-------|------|
| `mdex:read` | `read` |
| `mdex:write-intent` | `write-intent` (includes read) |
| `mdex:fullwrite` | `fullwrite` (includes all) |

**Discovery endpoints** (served automatically, no auth required):

| Endpoint | Standard | Purpose |
|----------|----------|---------|
| `/.well-known/oauth-protected-resource` | RFC 9728 | Points MCP clients to the AS |
| `/.well-known/oauth-authorization-server` | RFC 8414 | AS metadata (endpoints, PKCE methods) |

**Connecting claude.ai:** add a custom connector → enter the gateway URL → Advanced settings →
supply `client_id` and `client_secret`. Claude auto-discovers the AS and handles the login flow.

**Before go-live:** the gateway uses ephemeral signing keys by default — tokens are invalidated
on restart. Configure a persistent X.509 signing certificate (`MDEX_SIGNING_CERT_PATH` /
`MDEX_SIGNING_CERT_PASS`) before production use. See [`deploy/`](deploy/) for the deployment
template and a step-by-step certificate setup.

## mdex-token (token admin)

```
mdex-token new --role <read|write-intent|fullwrite> --label <name>
mdex-token list
mdex-token revoke <label>
```

Only the SHA-256 hash is stored; the raw token is shown once. The gateway watches the token
file and picks up changes within ~1s — no restart needed.

## Development

A dev container (`.devcontainer/`) provides .NET 10 ready to go. See `docs/` for
the full design and implementation plan.
