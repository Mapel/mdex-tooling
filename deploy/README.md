# Deployment

The gateway runs as a Docker container behind an nginx-proxy + letsencrypt companion.
See [`docker-compose.yml`](docker-compose.yml) for the (redacted) deployment template.

## First-time setup

1. Copy `docker-compose.yml` to your deploy host (outside this repo) and fill in:
   - `build:` → path to your `mdex-tooling` checkout
   - the `/vault` volume → path to your data vault
   - `VIRTUAL_HOST` / `LETSENCRYPT_*` → your public host
   - the OAuth client secrets and `MDEX_SIGNING_CERT_PASS`
2. Generate the persistent signing certificate (below).
3. `docker compose up -d --build`

## Persistent OAuth signing certificate

The gateway signs OAuth tokens with the key in an X.509 certificate. Without one it falls
back to an **ephemeral** key and invalidates every issued token on each restart. Generate a
self-signed cert once and mount it read-only at `/certs`:

```bash
mkdir -p certs && chmod 700 certs
PFXPASS=$(openssl rand -base64 24)
TMP=$(mktemp -d)
openssl req -x509 -newkey rsa:2048 -keyout "$TMP/key.pem" -out "$TMP/cert.pem" \
  -days 3650 -nodes -subj "/CN=mdex-gateway-signing"
openssl pkcs12 -export -out certs/signing.pfx -inkey "$TMP/key.pem" -in "$TMP/cert.pem" \
  -name "mdex-gateway-signing" -passout pass:"$PFXPASS"
chmod 600 certs/signing.pfx
rm -rf "$TMP"
echo "Put this in MDEX_SIGNING_CERT_PASS: $PFXPASS"
```

Then set `MDEX_SIGNING_CERT_PATH=/certs/signing.pfx` and `MDEX_SIGNING_CERT_PASS=<password>`
in the compose environment (already wired in the template).

**Back up `certs/signing.pfx` and its password.** Losing either invalidates all tokens.

The same certificate is used for both token **signing** and **encryption** — refresh tokens and
authorization codes are always encrypted, so the encryption key must persist too (an ephemeral one
would make stored refresh tokens undecryptable after a restart).

## OAuth store (refresh tokens persist across restarts)

OAuth authorizations and refresh tokens live in a SQLite file (`/tokens/openiddict.db` by default,
on the same persisted volume as the token store; override with `MDEX_OPENIDDICT_DB`). The schema is
created automatically at startup. This — together with the persistent signing/encryption cert —
lets clients silently renew expired access tokens across redeploys, so claude.ai/ChatGPT don't
re-prompt for consent. Nothing to set up beyond keeping the `tokens/` volume.

## OAuth clients (runtime-editable)

OAuth clients — id, secret, scopes, and **redirect URIs** — are defined in a watched `clients.json`
on the `/tokens` volume (default `/tokens/clients.json`, override with `MDEX_OAUTH_CLIENTS`). The
gateway syncs the file into its store at startup **and whenever the file changes**, so you can add a
connector's callback URL or rotate a secret by editing the file — no rebuild, no restart.

Start from [`clients.example.json`](clients.example.json):

```bash
cp clients.example.json tokens/clients.json   # the /tokens volume
chmod 600 tokens/clients.json                  # holds client secrets
# fill in real secrets / redirect URIs, then save — changes apply within ~1s
```

Notes:
- Clients are matched and removed by `ClientId`: drop a client from the file and it's removed from
  the store. An **empty** file is ignored (guard) so a truncated file can't wipe every client.
- `clients.json` holds secrets — it is gitignored and must never be committed. `appsettings.json`
  ships an empty client list; local dev defaults live in `appsettings.Development.json`.

## Verify

```bash
# AS metadata reachable through the proxy
curl -sS https://<your-host>/.well-known/oauth-authorization-server   # -> 200

# Signing key is persistent: the JWKS `kid` must stay stable across a restart
curl -sS https://<your-host>/.well-known/jwks | grep kid
docker restart mdex-gateway && sleep 5
curl -sS https://<your-host>/.well-known/jwks | grep kid   # same kid
```

The `kid` equals the cert's SHA-1 fingerprint (`openssl x509 -noout -fingerprint`).
