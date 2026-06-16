# Tool-Surface Rework (2026-06-16)

Status: **implemented**. Supersedes the tool-surface and role parts of
`2026-06-05-mdex-mcp-local-remote-design.md`. Binary family, transport, and auth
are unchanged.

## Why

The old role split was *semantic vs. raw* (write-intent = semantic, fullwrite =
raw FS). That left `move`/`rename`/`archive` in fullwrite even though they are
**convention-preserving** (frontmatter stays intact, path-less wikilinks survive).
Write-intent was therefore not self-sufficient for normal AI work — restructuring
needed fullwrite or a human.

## New principle

The role split is **convention-preserving vs. convention-breaking**:

- **write-intent** — everything that *respects and enforces* the vault
  conventions (derive `type`, apply templates, bump `modified`). Self-sufficient
  for the whole normal AI workflow.
- **fullwrite** — everything that *deliberately bypasses* conventions: arbitrary
  content (`write-file`) and hard `delete`. A human-in-the-loop escape hatch.

The system owns the frontmatter; the AI only ever touches `tags` (via `set-tags`
or `--tag`). Cross-cutting idioms are consistent everywhere: `--depth N|all`
(default 1), `--match all|any` (default all), `(--content | stdin)`, `--tags`,
`<slug|path>` addressing with `details.candidates` on ambiguity.

## Surface (20 tools)

**read (7):** `search`, `find-notes`, `read-note`, `list`, `list-tags`,
`find-related`, `list-todos`.

**write-intent (11):** `capture-inbox`, `create`, `append-to-note`,
`append-to-log`, `promote-inbox-item`, `move`, `replace-body`, `set-tags`,
`mkdir`, `set-todo`, `commit-and-push`.

**fullwrite (2):** `write-file`, `delete`.

See the README command tables for full signatures.

## Consolidated / removed

- `get-by-tag` + `get-by-type` + `get-by-date-range` → `find-notes` (3 → 1).
- `search-tags` → a `pattern` argument of `list-tags`.
- `append-file` → dropped (no real AI use case).
- `move` and `mkdir` → pulled up from fullwrite into write-intent.
- `find-related` `--maxhops`/`--recursive` → `--depth N|all`; added
  `--direction in|out|both` (backlinks = `--direction in --depth 1`).
- `backlinks` frontmatter field → live-computed via `find-related --direction in`
  (no stored field).

## Behavioural changes / fixes

- `capture-inbox` — `--title` is now **required**; sets the H1 and derives
  `inbox/YYYYMMDD-<title-slug>.md` (a tooling convention for sorting/dedup, not a
  vault law — mobile captures without it stay valid).
- `read-note` — variadic with **partial success** (missing targets returned under
  `errors`; throws only when *all* targets fail).
- `list` / `find-related` — default **depth 1** (was fully recursive / 1 hop).
- `promote-inbox-item` — repairs missing/incomplete frontmatter for legacy inbox
  items instead of failing (anything under `inbox/` is accepted).
- `replace-body` — new rewrite path (body replaced, frontmatter preserved).
- ToDos — represented as embedded checklists, aggregated by `list-todos` and
  mutated by `set-todo` (text-match addressing, ambiguity reported not guessed).
- `commit-and-push` — atomic `add -A → commit → pull --rebase → push` for
  AI-initiated persistence. Local changes are committed **before** the rebase so a
  dirty-tree rebase can't abort; a real merge conflict fails hard and is returned.
  Semantic message, no `auto:` prefix (that belongs to the server sync script).

## Vault-lint implication

Inbox items may legitimately lack full frontmatter (mobile captures), so a future
Vault-Lint must only flag frontmatter defects on **typed** notes — never in
`inbox/`. Inbox-lint is limited to age / staleness.
