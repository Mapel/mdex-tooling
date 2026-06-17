# Security review — gateway auth, OAuth & runtime client config

**Date:** 2026-06-17 · **Reviewer:** Claude (manual code review) · **Commit:** `c9ca7f9` (main)

## Scope & method

Manual review of the remote MCP gateway's security surface, with emphasis on the recently added
runtime-editable OAuth client config (`clients.json`) and the OAuth 2.1 / token paths. Files read:
`src/Mdex.Gateway/Program.cs`, `src/Mdex.Gateway/Auth/*`, `src/Mdex.Tokens/*`,
`src/Mdex.Core/Security/*`, `src/Mdex.Mcp.Tools/RoleToolVisibility.cs`.

Threat model: a single-owner, internet-exposed gateway (`https://mdex-mcp.rum2011.de`) behind an
nginx-proxy, fronting a personal knowledge vault. The owner's clients are claude.ai, ChatGPT and
Langdock. "Compromise" = an unauthorized party reading or writing the vault, or minting tokens.

Overall: **the core authN/authZ design is sound** — default-deny tool authorization, hash-only token
storage with fixed-time comparison, fail-closed handling, scope clamping, and persistent signing keys.
The findings below are hardening opportunities, most of them low-to-medium given the single-tenant
model. No critical/exploitable vulnerability was found.

---

## Findings

### M1 — Forwarded headers trusted from any source
`Program.cs:167-169` clears `KnownNetworks` and `KnownProxies` before `UseForwardedHeaders`, so the
gateway accepts `X-Forwarded-Proto` / `X-Forwarded-For` from **any** caller, not just the nginx-proxy.
Kestrel binds `0.0.0.0:3001` on the shared `nextcloud-docker_nextcloud_network`; the port is not
published to the host, but every other container on that network (nextcloud, pihole, mariadb, …) can
reach it directly and spoof these headers.

- **Impact (today):** low — no IP-based access control exists, and a spoofed `X-Forwarded-Proto: https`
  only satisfies the transport check / sets the discovery issuer scheme; it does not grant auth.
- **Risk (latent):** any future IP allowlisting, rate-limiting, or audit logging would be trivially
  bypassable, and `X-Forwarded-Proto: http` could be used to influence issuer/redirect scheme.
- **Recommendation:** restrict trust to the proxy — set `fwdOptions.KnownNetworks` to the docker
  subnet (e.g. `172.20.0.0/16`) or `KnownProxies` to the nginx-proxy container IP, instead of clearing.

### M2 — The authorization server performs no resource-owner authentication
The consent endpoint (`AuthorizationController.Authorize`) shows an Allow/Deny page to anyone who can
reach it and signs in a fixed subject `"mdex-owner"` (`AuthorizationController.cs:49`) with no login.
Security therefore rests entirely on **client-secret confidentiality**: only a holder of a valid
`client_id`+`client_secret` can exchange the resulting code (confidential clients), and codes are only
delivered to pre-registered redirect URIs, with PKCE on top.

- **Impact:** an attacker who reaches the consent page cannot obtain a usable token (no secret; the
  code goes to the legitimate redirect URI). But anyone who obtains a `client_secret` can mint tokens
  for that client's full scope tier with no second factor and no user interaction.
- **Assessment:** acceptable for a single-owner gateway and consistent with the README's credential-
  hygiene guidance, but it should be explicitly documented as the central trust assumption.
- **Recommendation (optional):** gate the consent POST behind a shared owner secret, or bind consent to
  a simple authenticated session, so a leaked `client_id` alone has zero value.

### M3 — Plaintext secrets at rest; `clients.json` is now a full-compromise target
Client secrets live in plaintext in `clients.json`, and the signing-cert password in the compose file
(both mode 600, single-tenant host). With the new runtime-sync, **write access to `clients.json` is
equivalent to full gateway takeover**: an attacker who can write it can add a client with a known
secret and an attacker-controlled redirect URI, then mint `fullwrite` tokens and/or capture auth codes.

- **Impact:** high *if* the `/tokens` volume is writable by an untrusted party; otherwise contained to
  the host/owner trust boundary.
- **Recommendation:** keep `clients.json` mode 600 and owned by the container user only (done);
  document that its integrity = gateway security; back it up off-host; consider validating redirect
  URIs against an allowlist of trusted hosts on load (reject unexpected domains).

### M4 — Opaque bearer tokens never expire
`TokenEntry` carries only a `Created` date (`TokenEntry.cs:4`); `TokenStore.FindRole` matches on hash
+ role and never checks expiry (`TokenStore.cs:34-44`). Custom bearer tokens are therefore valid until
manually revoked — no rotation, no TTL.

- **Impact:** a leaked bearer token is usable indefinitely until the owner notices and revokes it.
- **Recommendation:** add an optional `Expires` field to `TokenEntry` and enforce it in `FindRole`
  (fail-closed when past). Even a long default TTL bounds the blast radius of a leak.

### L1 — No rate limiting / brute-force protection on auth endpoints
Neither `/connect/token` (client-secret) nor the bearer path has rate limiting or lockout. Mitigated by
high entropy (256-bit bearer tokens; long client secrets), so brute force is infeasible, but there is
no defense-in-depth and no signal on credential-stuffing attempts.
- **Recommendation:** add ASP.NET rate limiting on `/connect/token` and the MCP endpoint; log repeated
  auth failures (the source IP is only trustworthy once **M1** is fixed).

### L2 — Discovery issuer derived from the request Host/scheme
`/.well-known/oauth-protected-resource` builds the issuer from `ctx.Request.Scheme`/`Host`
(`Program.cs:179`); OpenIddict's AS metadata likewise. A spoofed `Host`/`X-Forwarded-Proto` is
reflected into advertised URLs. Low impact (clients fetch discovery from the known URL), but combined
with **M1** it is worth pinning.
- **Recommendation:** configure a fixed issuer (`SetIssuer`) and/or host filtering.

### L3 — One X.509 certificate used for both signing and encryption
`Program.cs:108` loads a single cert and registers it as both the signing and the encryption
certificate. Functionally fine for a single-tenant AS; using one RSA key for two purposes is mild
crypto-hygiene debt.
- **Recommendation:** if convenient, generate a separate encryption cert; otherwise accept and note.

### L4 — `clients.json` reloads are not atomic on the writer side
The gateway side is safe (a malformed/partial read is caught and the running clients are kept —
`OAuthClientSeed.RunSyncAsync`, with the empty-config guard). But an in-place editor save could be read
mid-write. The token store already uses temp-file+rename for exactly this; `clients.json` edits do not.
- **Recommendation:** document "edit atomically (write temp, then move)" or have an admin helper do so.

### Info
- **Access tokens are unencrypted** (`DisableAccessTokenEncryption`): JWTs are signed, not encrypted,
  and readable by clients. Intentional (MCP client compatibility); tokens carry only scopes + the
  `mdex-owner` subject, no sensitive PII. Acceptable.
- **DataProtection keys not persisted/encrypted** (startup warning, `/home/ubuntu/.aspnet/...`): low
  impact — the gateway is stateless (no auth cookies, no antiforgery in use) and OpenIddict uses the
  X.509 keys, not DataProtection. Persisting the key ring would only silence the warning.
- **`OAuthScopeClaimsTransformer`** trusts a pre-existing `mdex-role` claim and skips scope mapping if
  present (`:14`). Not exploitable: issued JWTs never contain `mdex-role` (the AS sets only subject +
  scopes) and are signed; the bearer path sets it server-side. Noted as defense-in-depth context.

---

## What is done well (no action needed)

- **Token storage:** only SHA-256 hashes are stored; raw tokens shown once; 256-bit entropy
  (`TokenGenerator`, `TokenHasher`). Lookup is constant-time (`TokenStore.FixedTimeEquals`).
- **Fail-closed everywhere:** corrupt token file → empty → all 401 (`TokenStore.cs:96`); unknown tool
  or missing role claim → denied (`RoleToolVisibility.Apply`, `HttpRoleProvider.Current`).
- **Default-deny authZ:** `ToolAuthorizer.Require` gates every tool by minimum role; tool visibility is
  a UX layer, not the gate.
- **Scope handling:** over-requested scopes are clamped to the client's tier, not granted
  (`AuthorizationController.EffectiveScopesAsync`); `offline_access` handled explicitly.
- **Runtime client sync:** empty-config guard prevents a truncated file from wiping all clients;
  reloads are serialized, debounced, and error-guarded.
- **Secret handling:** tokens/secrets are never logged (`BearerAuthHandler.cs:37`); OpenIddict stores
  client secrets hashed in the DB; on-disk token file is written atomically with 0600/0700 perms.
- **Key persistence:** signing + encryption use a persistent X.509 cert so tokens survive restarts.

## Suggested priority

1. **M1** (restrict forwarded-header trust) — cheap, removes a latent spoofing primitive.
2. **M4** (token expiry) — bounds leak blast radius.
3. **M3 hardening** (redirect-URI allowlist on load) + document the `clients.json` trust assumption.
4. **M2** (consent owner gate) — only if the gateway's exposure grows beyond personal use.
5. **L1–L4 / Info** — opportunistic.
