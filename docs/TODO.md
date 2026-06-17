# TODO / Backlog

Running list of deferred work. Newest first. Move done items to the bottom or delete.

---

## Runtime-editable OAuth client config (no rebuild/redeploy)

**Status:** open — raised 2026-06-17

**Problem.** OAuth clients (`Mdex:OAuthClients`: client id/secret, scopes, **redirect URIs**)
live in `src/Mdex.Gateway/appsettings.json`, which is baked into the Docker image. Any change —
e.g. adding a connector's callback URL — currently requires a full `docker compose build` +
redeploy. That is heavyweight for what is effectively a small piece of operational config, and it
caused exactly that friction when adding the Langdock callback (ID2043).

**Why it matters.** Redirect URIs and client registrations change for operational reasons
(onboarding a new connector, rotating a secret) that have nothing to do with code. They should be
adjustable at runtime by an operator, not gated behind a build.

**Existing pattern to follow.** The bearer-token store already does this well: `tokens.json` is a
separate file outside the image, mounted as a volume, and `TokenStoreWatcher` picks up edits within
~1s with no restart. The OAuth client config should work the same way.

**Proposed approach.**
- Move OAuth client definitions out of `appsettings.json` into a separate JSON file (e.g.
  `clients.json`) referenced by an env var (e.g. `MDEX_OAUTH_CLIENTS`), mounted on the same
  persisted volume as `tokens.json` / `openiddict.db`.
- Watch the file (mirror `TokenStoreWatcher`) and re-run the idempotent seed
  (`OAuthClientSeed.StartAsync` logic) on change, so edits to redirect URIs / scopes apply live.
  The seed already does `UpdateAsync` against the persistent OpenIddict store, so most of the
  machinery exists — it just needs to run on file change instead of only at boot.
- Keep `appsettings.json` as a fallback/default for dev/CI.

**Acceptance.** Adding a redirect URI to a connector requires only editing the mounted clients file;
the change takes effect within ~1s, no rebuild, no container recreate. Verify the new URI lands in
`OpenIddictApplications.RedirectUris` without a redeploy.

**Touch points.** `src/Mdex.Gateway/Program.cs` (config + watcher wiring),
`src/Mdex.Gateway/Auth/OAuthClientSeed.cs` (extract seed into a re-runnable service),
`src/Mdex.Tokens/TokenStoreWatcher.cs` (reference implementation), `deploy/` (volume + env).
