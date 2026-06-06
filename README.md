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

#### Read

| Command | Description |
|---------|-------------|
| `search <query>` | Full-text substring search over title + body (one hit per note). |
| `read-note <slug\|path>` | Print a note's frontmatter and body. |
| `get-by-tag <tag>...` | Notes carrying **all** of the given tags. |
| `get-by-type <type>` | Notes whose frontmatter `type` matches. |
| `get-by-date-range (--from <date> \| --to <date>) [--field created\|modified]` | Notes in a date range. At least one bound required; the other is open-ended. `--field` defaults to `created`. Notes without that date are excluded. |
| `list-tags` | All tags with their counts. |
| `search-tags <pattern>` | Tags containing the given substring. |
| `list [path]` | Recursive file/dir tree under an optional vault-relative subpath. |
| `find-related <slug\|path> [--maxhops N] [--recursive]` | Notes linked via `[[wikilinks]]` in both directions. Depth 1 by default; `--maxhops N` caps the depth; `--recursive` walks the whole connected component (cycles terminate). Given both, `--maxhops` wins as the ceiling. |

#### Write

| Command | Description |
|---------|-------------|
| `capture-inbox (--content <text> \| stdin)` | Append a new timestamped `inbox/YYYYMMDD-NN.md` entry. |
| `create <type> <name> [--no-body] [--body <tpl>]` | Create a note. `type` ∈ `zettel\|project\|area\|reference`; non-zettel `name` may contain a subpath. `--no-body` writes frontmatter only; `--body <tpl>` selects a body template. |
| `append-to-note <slug\|path> (--content <text> \| stdin)` | Append a section and bump `modified`. |
| `append-to-log <slug\|path> (--content <text> \| stdin)` | Add a dated bullet under the note's `## Log` section (created if absent). |
| `promote-inbox-item <inbox> <type> [--name <name>]` | Move an inbox item into a typed folder. `--name` overrides the target slug. |

#### Raw FS (guarded to the vault root)

| Command | Description |
|---------|-------------|
| `write-file <path> (--content <text> \| stdin)` | Create or overwrite a file. |
| `append-file <path> (--content <text> \| stdin)` | Append to a file. |
| `move <from> <to>` | Move a file or directory (no overwrite). |
| `mkdir <path>` | Create a directory. |
| `delete <path>` | Delete a file or directory (recursive). |

All paths are jailed to the vault root. The CLI never touches git — the vault's
`mdex-autocommit.sh` is the only committer.

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

## Development

A dev container (`.devcontainer/`) provides .NET 10 ready to go. See `docs/` for
the full design and implementation plan.
