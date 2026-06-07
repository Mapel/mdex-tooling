# Security-Review-Plan: mdext & mdex-mcp

**Stand:** 2026-06-05
**Ziel:** Sicherheitsbetrachtung des mdex-Toolings in drei Phasen, abgestimmt auf den Entwicklungsstand. Tragende Annahmen früh validieren, neue Angriffsfläche reviewen, sobald sie existiert.

**Leitgedanke:** Der MCP-Host ruft die mdex-Operationen **in-process** auf. Damit erbt der Server mdexts Sicherheitseigenschaften direkt: PathGuard, ArgReader und der JsonEnvelope-Vertrag *sind* die Sicherheitsgrenze, sobald die Tools in-process laufen. Deshalb wird mdexts Fundament vor der Auth-Schicht geprüft, nicht erst danach.

**Verfügbare Hilfsmittel:**
- `security-guidance` (Hook) — warnt passiv beim Editieren vor Mustern wie Command-Injection/unsicheren Patterns. Aktiv während der Implementierung (Phase 3).
- `code-review` (Plugin) — Multi-Agent-PR-Review, confidence-scored.
- `security-review` (Skill) — Security-Review der pending Changes auf dem Branch.
- `pr-review-toolkit` ist *nicht* installiert; bei Bedarf für Phase 3 nachrüsten (u.a. `silent-failure-hunter`).

---

## Phase 1 — Schlanker Code-Check von mdext  *(abgeschlossen 2026-06-05)*

Eng geschnitten: nur die Eigenschaften, auf die sich `mdex-mcp` tatsächlich stützt. **Kein** Voll-Audit.

- [x] **PathGuard / Path-Jailing** — die tragende Wand.
- [x] **ArgReader-Robustheit** (an Task 1B gekoppelt).
- [x] **Error-/Info-Leakage** über Envelope/Exceptions.

**Explizit außerhalb des Scopes:** mdext als „CLI gegen seinen eigenen Operator". Als lokales Single-User-Tool hat es ein schwaches Threat-Model — wer mdext auf seinem eigenen Vault ausführt, hat eh Shell-Zugriff; „aus dem Vault ausbrechen" ist da kein Angriff. Dieser Aspekt wird erst durch die MCP-Schicht (untrusted AI-Caller) real.

### Ergebnisse (PoC-bestätigt)

**Kernbefund:** Das Containment, auf dem die ganze Rollen-Ladder steht, wird auf zwei von drei Code-Pfaden nicht erzwungen. PathGuard schützt nur die Raw-FS-Befehle; die Slug-/Pfad-Auflösung umgeht ihn vollständig.

| # | Sev | Befund | Fix |
|---|-----|--------|-----|
| **1** | 🔴 HIGH | **PathGuard-Bypass in `NoteRepository.FindBySlugOrPath`.** Direct-Path-Zweig nutzt `Path.Combine(vaultRoot, slugOrPath)` ohne Containment-Check; `Build`/Commands lesen & schreiben den aufgelösten absoluten Pfad direkt (nicht via `ResolveInside`). Betrifft jeden Befehl über `Resolver.Single` (read-note, append-to-note, append-to-log, find-related, promote …). **PoC:** `read-note ../secret` und absoluter Pfad → liest beliebige `.md` am System; `append-to-note ../outside` → schreibt außerhalb des Vaults. | Direct-Path-Zweig durch `PathGuard.ResolveInside` leiten (ein Chokepoint); absolute/escapende Pfade ablehnen. |
| **2** | 🟠 MED | **Leerer Pfad operiert auf Vault-Root.** `ResolveInside("")` → root (`full == _root` erlaubt); Commands prüfen nur auf `null`. **PoC:** `delete ""` löscht den kompletten Vault rekursiv. | null-or-whitespace in `ResolveInside` ablehnen; destruktive Verben nicht auf `== root` zulassen. |
| **3** | 🟠 MED | **Symlink-Traversal.** `Path.GetFullPath` ist rein lexikalisch, löst Symlinks nicht auf. Vorbestehender Symlink im Vault lässt gejailte Reads/Writes ausbrechen. Konditional (AI kann selbst keine Symlinks anlegen). | finalen Zielpfad auflösen (`File.ResolveLinkTarget(..., returnFinalTarget:true)`) vor dem Check — oder bewusst als Limitation ins Threat-Model. |
| **4** | 🟡 LOW-MED | **Case-insensitive Containment** (`OrdinalIgnoreCase`) auf case-sensitivem FS akzeptiert `/srv/VAULT/x` bei Root `/srv/vault` → anderes Verzeichnis außerhalb des Jails. (Sibling-Prefix `/srv/vault-evil` ist durch `rootWithSep` korrekt geblockt.) | `Ordinal` außer auf Windows, oder Containment via `Path.GetRelativePath` + `..`-Check. |
| **5** | 🟠 MED | **Nicht-`CommandException` leckt Stacktraces.** `Program.cs` fängt nur `CommandException`; IO-Fehler propagieren als unhandled Stacktrace mit absoluten Pfaden. CLI-lokal harmlos, aber der in-process Dispatcher (`CliDispatcher.RunToJson`, MCP-Task 1) **muss** breit fangen + sanitisieren, sonst sieht der AI-Agent interne Pfade/Stacks. → wandert nach Phase 2 / mdex-mcp. | Im Dispatcher unbekannte Exceptions auf generische Envelope-Error mappen; Details nur serverseitig loggen. |

**Positiv-Befunde (fürs Spec festhalten):**
- In-Process = keine Shell = **keine Command-Injection** — der größte theoretische Risikopfad ist by-design eliminiert.
- Envelope-`UnsafeRelaxedJsonEscaping` unbedenklich (stdio/MCP-JSON, kein HTML-Kontext → kein XSS).

**Zu ArgReader/Task 1B:** Die Free-Text-Ambiguität (`--content` beginnt mit `--`) ist ein Korrektheitsthema, von Task 1B abgedeckt, **kein** Injection-Vektor. Wichtig: Task 1B fixt HIGH-1 **nicht** — das Traversal sitzt im Resolver/`NoteRepository`, nicht im Arg-Parser. Zwei getrennte Baustellen.

> **Status (2026-06-05): Phase-1-Fixes gelandet** auf Branch `mdext-security-fixes`. Alle drei PoCs (Read-/Write-Traversal, `delete ""`-Root-Wipe) erneut geprüft → dicht; 87 Tests grün. HIGH-1, MED-2, MED-3, LOW-MED-4 + ArgReader-Härtung umgesetzt.

---

## Phase 2 — Security-Design-Check von mdex-mcp  *(abgeschlossen 2026-06-05)*

Review des MCP-Designs auf Design-Ebene — noch bevor Code existiert.

- [x] Threat Model · TLS/Transport · Rollen-Ladder/Default-Deny · Token-Hashing · tokens.json-Permissions · Positiv-Befunde.

### Ergebnisse

**Kernkonsequenz:** Die Rollen-Ladder ist nur so gut wie das Containment darunter — und Phase-1-HIGH-1 hebelt sie aktuell komplett aus. Über MCP heißt das: `read`-Token = „lies jede `.md` am Server", `write-intent`-Token = „beschreibe jede existierende `.md`". Der MCP-Plan enthält **keinen** Task, der das fixt. Das ist der wichtigste Befund.

| # | Sev | Befund | Fix |
|---|-----|--------|-----|
| **D-1** | 🔴 HIGH | **Rollen-Ladder ruht auf nicht-erzwungenem Containment** (= Phase-1 HIGH-1, über MCP erreichbar). `read_note` → `mdex.Call("read-note", "--", slugOrPath)` → Traversal → beliebige `.md`; `append_to_note` analog schreibend. Kein Plan-Task adressiert es. | **Prerequisite-Task** „mdext-Containment-Fix" *vor* den Tool-Tasks (13–15), oder Containment am `MdexBackend`-Boundary erzwingen. |
| **D-2** | 🟠 MED-HIGH | **Autorisierung ist opt-in statt default-deny.** Plan erzwingt Rollen per imperativer erster Zeile `auth.Require(...)` in jeder Tool-Methode. Vergisst jemand die Zeile bei einem neuen Tool → still **default-ALLOW** (jeder gültige Token). Weicht vom Spec §D.3 ab, das einen zentralen „MCP-Tool-Invocation-Filter" beschreibt. | Spec-Intent umsetzen: zentraler Filter (Tool→Min-Rolle aus einer Tabelle/Attribut), **oder** Guard-Test, der für jede `[McpServerTool]`-Methode einen `Require`-Aufruf beweist. |
| **D-3** | 🟠 MED | **tokens.json ohne Permission-Härtung.** `TokenStore.Save` nutzt `File.WriteAllText` ohne 0600/Dir-0700 → default-umask (oft world-readable). Watcher (Task 6) reloadet jede Änderung **ohne Validierung/Owner-Check**. Wer die Datei schreiben kann, injiziert einen eigenen `fullwrite`-Hash. | Datei 0600 / Verzeichnis 0700 auf POSIX setzen; im Spec: „Schreibrecht auf tokens.json = Vollkompromiss" dokumentieren. |
| **D-4** | 🟠 MED | **Stacktrace-Leak am Dispatcher-Boundary** (= Phase-1 MEDIUM-5, realisiert). `CliDispatcher.RunToJson` (Task 1) fängt nur `CommandException`; alles andere propagiert in die Tool-Methode → MCP-SDK serialisiert .NET-Message/Stack zum AI-Client. | Im Dispatcher breit fangen + auf generische Envelope-Error mappen; Details nur serverseitig loggen. |
| **D-5** | 🟡 LOW-MED | **Root-Wipe via `delete ""`** (= Phase-1 MEDIUM-2, über `fullwrite` erreichbar). `RawFsTools.Delete` reicht den AI-String durch; `path=""` → löscht Vault-Root. | Phase-1-#2-Fix in PathGuard deckt es ab. |
| **D-6** | 🟡 LOW | **Nicht-atomares `TokenStore.Save`** (`File.WriteAllText`) rennt gegen den Watcher; Teil-Lesung → Store als leer behandelt → kurzzeitig alle 401 (fail-closed, nur Verfügbarkeits-Blip). | Atomar schreiben (Temp-Datei + `File.Move`/rename). |
| **D-7** | 🟡 LOW | **Keine TLS/Bind-Erzwingung.** Design verlässt sich auf Operator-Disziplin (Kestrel auf `localhost`, TLS am Proxy). `appsettings` bindet `http://localhost:3001` — ein versehentliches `0.0.0.0` sendet Bearer-Token im Klartext übers Netz. | Explizit `127.0.0.1` binden; Invariante „nie public ohne TLS-Proxy" ins Spec/Deployment dokumentieren. |
| **D-8** | ℹ️ INFO | **Keine Token-Expiry/Rotation** — Tokens gelten ewig bis manuellem `revoke`. Bewusster Tradeoff für langlebige AI-Client-Creds. | Als akzeptierten Tradeoff im Spec festhalten; Rotation = reversibler Nachrüst-Pfad. |

**Positiv-Befunde (fürs Spec festhalten):**
- **In-Process = keine Shell = keine Command-Injection** (bewusste Sicherheitsentscheidung).
- **Vault-Root serverseitig gepinnt:** `MdexBackend` übergibt `vaultRoot` als `rootFlag`; der AI kann **kein** `--root` injizieren (Tool-Args gehen als Positionals hinter `--`, Root-Auflösung passiert nicht im Dispatcher-Pfad).
- **Nur Hashes gespeichert**, Roh-Token einmalig angezeigt.
- **Hot-Reload fällt sicher** (korrupte Datei → leerer Store → alle 401).
- **Auth-Fehler loggen den Token nicht** (`BearerAuthHandler` gibt nur „invalid token" zurück).

**Verfeinerung für Phase 3 (Constant-Time):** Das Design **hasht den präsentierten Token zuerst und vergleicht dann Hashes** (`FindRole` → SHA-256 → `e.Hash == hash`). Der Timing-Kanal eines nicht-konstanten Vergleichs leckt damit nur einen *preimage-resistenten Hash*, nicht den Token → **constant-time ist hier nicht zwingend nötig**. Die tragende Eigenschaft („Hashes vergleichen, nicht Roh-Token") ist bereits erfüllt. Phase-3-Punkt entsprechend abschwächen.

### Empfehlung fürs Spec (vor Implementierung)
1. **Threat-Model-Abschnitt** ergänzen: internet-exponiert via TLS-Reverse-Proxy, Kestrel loopback-only; Angreifer = (a) Netzwerk-Client ohne/mit geratenem Token, (b) kompromittierter/bösartiger AI-Client *mit* gültigem Token, der seine Rolle übersteigen oder den Vault verlassen will.
2. **D-1 als Prerequisite-Task** in den MCP-Plan (mdext-Containment-Fix vor den Tool-Tasks).
3. **D-2** (zentraler Gate / Guard-Test), **D-3** (Permissions) und **D-4** (Dispatcher-Catch) als konkrete Plan-Anpassungen.
4. Positiv-Befunde + Constant-Time-Verfeinerung als bewusste Entscheidungen dokumentieren.

---

## Phase 2-Delta — Re-Check nach Design-Änderung  *(abgeschlossen 2026-06-05)*

Phase 2 (oben) prüfte das frühere remote-only-Design. Das neue Design
([2026-06-05-mdex-mcp-local-remote-design.md](2026-06-05-mdex-mcp-local-remote-design.md))
führt **lokal + remote**, eine **4-Binary-Familie** (umbenannt: altes `mdex-mcp` =
remote = jetzt `mdex-gateway`; neues `mdex-mcp` = lokaler stdio-Host) und die Extraktion
von `Mdex.Core`/`Mdex.Mcp.Tools` ein. Dieses Delta-Review prüft nur die geänderte Fläche.

- [x] Threat-Model je Host (lokal stdio vs. remote HTTP) · Befunde-Remap D-1…D-8 ·
  neue Fläche (lokaler Host, Core-Extraktion, geteilte Tool-Lib).

**Voller Befund + konsolidiertes Threat-Model:** §8 des neuen Spec. Kurzfassung:

- **Alle Phase-2-Befunde tragen über** (nur umbenannt): D-1 ist durch das Containment in
  `Mdex.Core` (von beiden Hosts geteilt) erfüllt; D-2 gilt jetzt für `Mdex.Mcp.Tools`;
  D-3/D-6/D-7/D-8 sind Gateway-only & unverändert; D-4 wandert in den Tool-Layer
  (kein Envelope-Reparse-Boundary mehr); D-5 ist durch den Phase-1-Fix gedeckt.
- **Neu, alle LOW/INFO — kein neuer HIGH:** N-1 (lokaler Gegner = prompt-injizierter
  Caller, nicht Netz; akzeptiert), N-2 (Core-Extraktion = Regressionsrisiko),
  N-3 (Guard-Test auf geteilter Lib + lokaler Flag-Pfad), N-4 (Remote-Creds-Hygiene).
- **Kernaussage:** Die neue Form erbt Containment + Rollen-Leiter; der lokale Host bringt
  *weniger* Fläche mit (keine Netz-Fläche, keine Secrets-at-rest, kein Bind-Risiko).

### Security-Gates für den neuen Implementierungsplan (pro Phase)

Der phasige Implementierungsplan trägt diese als Akzeptanzkriterien:

| Plan-Phase | Security-Gate |
|---|---|
| 0 — `Mdex.Core` extrahieren (Refactor) | Containment-Tests wandern mit, bleiben grün (N-2); einziger PathGuard-Chokepoint erhalten |
| 1 — `Mdex.Mcp.Tools` | `[MinRole]` + CI-Reflection-Guard gegen die geteilte Lib (D-2, N-3) |
| 2 — `mdex-mcp` (lokal) | Flag fail-closed (kein Default); E2E-Traversal-Test durch die Tools; „`--role read` verweigert Write-Tools" |
| 3 — `Mdex.Tokens` + `mdex-token` | Perms 0600/0700, atomares Schreiben (D-3/D-6) |
| 4 — `mdex-gateway` | Bearer-Auth + Hot-Reload; D-4 (Tool-Layer-Sanitisierung), D-7 (127.0.0.1-Bind) |

Phase 3 (Code-Check, unten) läuft danach pro Phase / vor Merge gegen **beide** Hosts.

---

## Phase 3 — Security-Code-Check von mdex-mcp  *(abgeschlossen 2026-06-07)*

Erst sinnvoll, sobald Code existiert. Setzt Phase 2 voraus (das Threat-Model definiert, wogegen geprüft wird).

- [x] **Constant-Time-Vergleich** im Auth-Pfad — `TokenStore.FindRole` hasht zuerst, vergleicht via `CryptographicOperations.FixedTimeEquals`.
- [x] **Path-Traversal End-to-End** — Containment in `Mdex.Core` (phase-1-Fix), serverseitig gepinnter Root; AI kann kein `--root` injizieren. Phase 3 fügt keinen Pfad-Code hinzu.
- [x] **Token-Leaks** — `BearerAuthHandler` gibt nur Gründe zurück, nie den Token; nur Hashes persistiert; `MdexBackend` sanitisiert Exceptions (D-4).
- [x] **Header-Parsing-Edge-Cases** — `AuthIntegrationTests` (kein Header, leerer Token, falsches Schema, kein Prefix, unbekannt → 401).
- [x] **Hot-Reload-Robustheit** — korrupte Datei → leerer Store → fail-closed; atomares Schreiben; `Corrupt_token_file_fails_closed_401`.

**Voller Befund:** [`2026-06-07-phase-3-security-review.md`](2026-06-07-phase-3-security-review.md).
Kein neuer HIGH/MED-Vuln. Ein LOW/Defense-in-Depth-Punkt umgesetzt: **SEC-1** — der Default-Deny-Guard
prüfte nur *Deklaration* (`[MinRole]` + `ToolAuthorizer`-Param), nicht *Durchsetzung*; D-2 war damit
formal nicht erfüllt. Neuer behavioraler Guard (`ToolGateGuardTests.Every_above_floor_tool_refuses_the_next_lower_role`)
ruft jedes Above-Floor-Tool mit der nächst-niedrigeren Rolle auf und verlangt die Refusal des Authorizers
(mutationsgetestet: schlägt fehl, wenn ein Tool `Require` vergisst). **SEC-2** (Legacy-SSE aktiv) als
bewusster Verfügbarkeits-Tradeoff dokumentiert — Auth gilt weiterhin, DoS-Klasse außerhalb des Scopes.

> **Status (2026-06-07):** Phase-3-Code-Review + Security-Review abgeschlossen. 138 Tests grün, Build 0 Fehler.

**Hilfsmittel:** `security-review`-Skill auf den Branch; `code-review`-Plugin auf den PR; `security-guidance`-Hook ist während der Implementierung ohnehin aktiv. Ggf. `pr-review-toolkit` (silent-failure-hunter) nachinstallieren.

---

## Reihenfolge

1. **Phase 1 + 2** — Fundament validiert + altes Design gehärtet. *(abgeschlossen)*
2. **Phase 2-Delta** — Re-Check nach der Design-Änderung (lokal + remote, 4 Binaries);
   Gates pro Plan-Phase definiert. *(abgeschlossen)*
3. **Phase 3 später** — nach Implementierung, pro Phase / vor Merge, gegen beide Hosts.
