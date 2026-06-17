# Runtime-editable OAuth client config

**Date:** 2026-06-17 · **Status:** planned → in progress · **Branch:** `feat/runtime-oauth-client-config`

## Goal

Let an operator add/change/remove OAuth clients — client id/secret, scopes, **redirect URIs** —
without a rebuild or container recreate. Today these live in `appsettings.json` (baked into the
image), so adding e.g. a connector callback needs `docker compose build` + redeploy. Changes should
apply at runtime, mirroring how `tokens.json` already works.

## Current state

- `Mdex:OAuthClients` is bound to `IOptions<List<OAuthClientConfig>>` from `appsettings.json`.
- Secrets are injected per-client via env (`Mdex__OAuthClients__N__ClientSecret`) in the compose file.
- `OAuthClientSeed` (IHostedService) runs **once** at startup: upserts each client into the
  persistent OpenIddict SQLite store via `Create`/`UpdateAsync`. It never removes clients.

## Design

**Separate file, layered as a config source.** A JSON file (default `clients.json`, next to the
token store on the persisted volume; override with `MDEX_OAUTH_CLIENTS`) is added with
`AddJsonFile(path, optional: true, reloadOnChange: true)` **after** `appsettings.json`, so it wins.
Shape mirrors the config section so binding is unchanged:

```json
{ "Mdex": { "OAuthClients": [ { "ClientId": "...", "ClientSecret": "...", "Scopes": [...], "RedirectUris": [...] } ] } }
```

This reuses ASP.NET's built-in file watcher (no hand-rolled `FileSystemWatcher`), and secrets move
out of compose env into this one mounted file (consistent with `tokens.json`).

**Re-runnable seed.** Extract the upsert loop from `OAuthClientSeed.StartAsync` into
`SeedAsync(IReadOnlyList<OAuthClientConfig>, CancellationToken)`. `StartAsync` calls it once;
a reload also calls it.

**React to reload.** Use `IOptionsMonitor<List<OAuthClientConfig>>` and subscribe `OnChange`.
- **Debounce** ~300ms (reload can fire several events), like `TokenStoreWatcher`.
- **Each run** creates a DI scope (the application manager is scoped).
- Keep the subscription `IDisposable` and dispose in `StopAsync`.

**Deletions.** After upserting the file's clients, enumerate the store
(`IOpenIddictApplicationManager.ListAsync`) and delete any client whose id is **not** in the file.
**Guard:** if the incoming list is empty, skip deletion entirely — a truncated/corrupt file must not
wipe every client and lock everyone out.

**Error guard.** Wrap reload re-seeds in try/catch and validate before applying: a malformed JSON
edit logs a warning and leaves the running store untouched. A bad edit must never take the gateway
down or drop valid clients.

## Security notes

- `clients.json` holds client secrets → mode `600`, on the same restricted volume as `tokens.json`.
- Redirect-URI changes are security-relevant (open-redirect / token exfiltration surface); the file
  is operator-controlled and not world-readable. The deletion guard prevents an empty-file lockout.
- `appsettings.json` keeps only `CHANGE-ME` placeholder secrets as dev/CI defaults.

## Implementation steps

1. Resolve `clients.json` path (env `MDEX_OAUTH_CLIENTS`, default next to token store) and add it as
   a `reloadOnChange` JSON config source in `Program.cs`.
2. Refactor `OAuthClientSeed`: extract `SeedAsync(...)`; add upsert + deletion-with-guard.
3. Switch to `IOptionsMonitor`, subscribe `OnChange` with debounce + try/catch; dispose on stop.
4. Move prod secrets + redirect URIs into the deployed `clients.json`; drop the secret env vars from
   the compose file. Update `deploy/docker-compose.yml` (template) + `deploy/README.md`.
5. Remove the `// TODO` in `Program.cs` and the TODO.md item.

## Testing

- Unit/integration over `SeedAsync` against an in-memory OpenIddict store (or SQLite temp file):
  - add a client → appears in store;
  - change a redirect URI / scope → reflected;
  - remove a client from the list → deleted from store;
  - **empty list → existing clients retained** (guard);
  - malformed input → no change, no throw to caller.
- Manual: edit the mounted `clients.json` on the running container, confirm a new redirect URI lands
  in `OpenIddictApplications.RedirectUris` within ~1s without rebuild/restart.

## Acceptance

Adding a connector callback (or rotating a secret) requires only editing the mounted `clients.json`;
the change applies within ~1s, no `docker compose build`, no container recreate.
