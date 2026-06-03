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

## Development

A dev container (`.devcontainer/`) provides .NET 10 ready to go. See `docs/` for
the full design and implementation plan.
