# TODO / Backlog

Running list of deferred work. Newest first. Move done items to the bottom or delete.

---

## Drop YamlDotNet from frontmatter parsing (unblocks Native AOT)

**Status:** open — noted 2026-06-28 while planning self-contained Nix deployment of `mdex-cli`/`mdex-mcp`.

**Problem.** `src/Mdex.Core/Vault/FrontmatterParser.cs` is the *only* place YamlDotNet is used in the
whole codebase (no serializer, no `--yaml` output, MCP output is all `System.Text.Json`). It pulls in
the full reflection-based YamlDotNet 18 just to deserialize a flat `Dictionary<string, object?>` and
then hand-pick `type`/`created`/`modified`/`tags`. The frontmatter schema is **fixed**, so the generic
reflection machinery is pure overkill — the reflection comes from the library being general-purpose,
not from the problem.

**Why it matters.** YamlDotNet's default `DeserializerBuilder` is reflection-based and not
trim/AOT-annotated. That blocks **Native AOT** and trimmed publishes, which would otherwise give a
small, fast, fully self-contained single binary (ideal for the Nix/NixOS distribution). Right now we
ship untrimmed self-contained (~70–90 MB) specifically to keep YamlDotNet's reflection working.
Secondary: a fixed schema deserves an explicit parser, not a general YAML engine.

**Proposed approach.**
- Remove the `YamlDotNet` PackageReference from `src/Mdex.Core/Mdex.Core.csproj`.
- Replace `FromYaml` with a small reflection-free parser for the known keys: scalar `type`/`created`/
  `modified`, list-or-scalar `tags` (both `[a, b]` flow and `- a` block form), pass-through `extra`.
  Cover the real edge cases: quoted strings, `#` comments, empty values, CRLF (already normalized).
- Keep `ParseResult`/`Frontmatter` shape unchanged so nothing downstream moves.
- Add focused unit tests for the parser (round-trip against the existing vault's frontmatter variants).

**Acceptance.** `YamlDotNet` no longer appears anywhere in the build; existing frontmatter behaviour
is unchanged (tests green against real notes); `dotnet publish -r linux-x64 -p:PublishAot=true`
produces no trim/AOT warnings originating from frontmatter parsing.

**Touch points.** `src/Mdex.Core/Vault/FrontmatterParser.cs`, `src/Mdex.Core/Mdex.Core.csproj`,
new tests under `tests/Mdex.Core.Tests/`.

---

## Runtime-editable OAuth client config (no rebuild/redeploy)

**Status:** ✅ done 2026-06-17 — implemented on `feat/runtime-oauth-client-config`. Clients live in a
watched `clients.json` synced into the OpenIddict store at boot + on change (add/update/remove, with
an empty-config guard). See [2026-06-17-runtime-oauth-client-config.md](2026-06-17-runtime-oauth-client-config.md).

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
