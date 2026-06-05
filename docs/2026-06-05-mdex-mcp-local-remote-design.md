# Design-Update: mdex – Lokaler + Remote MCP, 4-Binary-Familie (revidiert Plan 2)

> **Datum:** 2026-06-05
> **Status:** Entwurf (zum Review)
> **Kontext:** Brainstorming-Session mit Michael.
> **Ersetzt:** [2026-06-04-mdex-mcp-design.md](2026-06-04-mdex-mcp-design.md) (Plan 2, remote-only)
> und verfeinert §§2,5 von [2026-06-01-mdex-tooling-design.md](2026-06-01-mdex-tooling-design.md).
> Die dazugehörige Plan-2-Implementierungsdoku
> ([2026-06-04-mdex-mcp-plan.md](2026-06-04-mdex-mcp-plan.md)) ist damit ebenfalls
> abgelöst und wird neu geschrieben (eigener Schritt).

---

## Warum dieses Update (Auslöser)

Plan 2 entwarf den MCP **remote-only**. Der reale Arbeitsalltag bricht damit: Der
Vault ist auf dem Arbeitsrechner **lokal ausgecheckt**, und Claude Code / Cowork /
Codex laufen *auf dieser Maschine* gegen genau diesen Checkout. Ein remote-only-MCP
würde in die **Server-Working-Copy** schreiben → push-vom-Server + pull-nach-lokal →
ein rückwärtiger, fehleranfälliger Workflow, der dem ganzen Sinn lokaler Arbeit
zuwiderläuft.

Konsequenz: Es braucht **zwei** Zugänge zur selben Tool-Oberfläche:

- **lokal** – ein MCP-Server, der auf den **lokalen Checkout** arbeitet (für
  Code/Cowork/Codex auf dem Arbeitsrechner). Du committest/pushst danach selbst,
  wie heute schon.
- **remote** – der bestehende HTTP+Auth-Zugang für **unterwegs** (Handy, Webchat,
  Langdock), der auf die **Server-Working-Copy** arbeitet.

## Was sich gegenüber Plan 2 ändert – und was bleibt

| Bereich | Plan 2 (alt) | Dieses Update |
|---|---|---|
| Reichweite | nur remote | **lokal + remote**, zwei Geschwister-Hosts |
| Binaries | `mdext`, `mdex-mcp`, `mdex-mcpctl` | **4er-Familie, umbenannt** (siehe §1) |
| `mdex-mcp` | = der **Remote**-Server | = der **lokale** stdio-Host (Bedeutung gewechselt!) |
| In-Process | Tool parst `CliDispatcher.RunToJson`-Envelope | Tool nutzt das **`Data`-Objekt direkt** (§3) |
| `--json` | „stabiler Vertrag, den der MCP konsumiert" | reiner CLI-Ausgabemodus, **kein** Vertrag mehr (§7) |
| Rollen-Quelle | nur Token (remote) | remote = Token, **lokal = Launch-Flag, kein Default** (§4) |
| Client-Setup | nicht behandelt | **Topologie lokal vs. remote** (§5) |
| **Unverändert übernommen** | Drei-Rollen-Leiter, Threat-Model, Token-Store, Auth-Mechanik, Hot-Reload, Deployment-Koexistenz, Tests | siehe §6 |

Die zwei Plan-2-Kernentscheidungen **In-Process** (supersedes §5) und **Drei
Rollen** (supersedes §2) bleiben gültig; dieses Update *verfeinert* die erste und
*erweitert* die zweite um den lokalen Fall.

---

## 1. Binary-Familie & Namensschema

Einheitlicher `mdex-`-Prefix (löst den `mdext`-Bruch auf). `mdex-cli` bewusst **nicht**
bare `mdex`, um Verwechslung mit dem Repo-/Vault-Ordner `mdex` zu vermeiden.

| Binary | Wo | Enthält | ASP.NET? |
|---|---|---|---|
| **`mdex-cli`** | lokal (.NET Global Tool) | Content-CLI (für dich), human + `--json` | nein |
| **`mdex-mcp`** | lokal (.NET Global Tool) | **stdio**-MCP-Server gegen den lokalen Checkout | **nein** |
| **`mdex-gateway`** | Server (systemd) | **HTTP**-MCP-Host + Bearer-Auth + Rollen | ja |
| **`mdex-token`** | Server | Token-Verwaltung (new/list/revoke) | nein |

**Mapping aus Plan-2-Namen** (zur Migration der Köpfe):

| Plan 2 | jetzt | Anmerkung |
|---|---|---|
| `mdext` | `mdex-cli` | reine Umbenennung |
| `mdex-mcp` (remote) | `mdex-gateway` | Remote-HTTP-Host heißt jetzt *gateway* |
| — | `mdex-mcp` (lokal) | **neu**: derselbe MCP, aber stdio/lokal |
| `mdex-mcpctl` | `mdex-token` | reine Umbenennung |

**Kern-Begründung der Aufteilung:** Der ASP.NET-/Auth-Stack darf **nicht** auf den
Dev-Rechner (Ballast + `dotnet tool`-Paketierung von ASP.NET ist fummelig). Deshalb
bleibt `mdex-mcp` schlank (kein Web-Stack), und `gateway`/`token` sind server-only
und landen nie auf dem Arbeitsrechner. Lokale Deployment-Ziele und Server sind
**disjunkt** – keine Maschine führt beide MCP-Hosts.

### Projekt-/Library-Struktur

```
src/
  Mdex.Core/        (Lib)   Operationen (ICommand) + Dispatcher + Vault-Services
                            + Rollen-Authorizer + die Data-Objekte. Die EINE Quelle.
  Mdex.Cli/         → mdex-cli      dünne Console über Mdex.Core
  Mdex.Mcp.Tools/   (Lib)   [McpServerTool]-Wrapper → rufen Mdex.Core; tragen [MinRole]
  Mdex.Mcp/         → mdex-mcp      stdio-Host über Mdex.Mcp.Tools (kein ASP.NET)
  Mdex.Gateway/     → mdex-gateway  ASP.NET-Host über Mdex.Mcp.Tools + Mdex.Tokens
  Mdex.Tokens/      (Lib)   Token-Store: Modell, SHA-256, Lesen/Schreiben/Watch
  Mdex.Token/       → mdex-token    Console über Mdex.Tokens
tests/
  Mdex.Core.Tests/ · Mdex.Mcp.Tools.Tests/ · Mdex.Gateway.Tests/ · Mdex.Tokens.Tests/
```

Referenz-Regeln:
- `Mdex.Core` wird von `Mdex.Cli`, `Mdex.Mcp.Tools` referenziert (alle Operationen).
- `Mdex.Mcp.Tools` wird von **beiden** MCP-Hosts (`Mdex.Mcp`, `Mdex.Gateway`)
  referenziert → genau **eine** Tool-Definition, kein Drift.
- `Mdex.Tokens` wird von `Mdex.Gateway` (verifiziert/watcht) und `Mdex.Token`
  (verwaltet) geteilt – einzige Stelle, die das `tokens.json`-Format kennt.

> Gegenüber Plan-2 §A wird der „Core" als eigene Lib herausgezogen statt
> `Mdex.Mcp → Mdex.Cli` zu referenzieren. Das ist ein **internes**
> Implementierungsdetail, kein als Produkt herausgehobener Core – der Geist von
> §5 (eine wörtliche Implementierung jeder Operation) wird damit sogar erfüllt.

---

## 2. Architektur: zwei MCP-Hosts über geteiltem Kern (B2)

`mdex-mcp` (stdio, lokal) und `mdex-gateway` (HTTP, remote) sind **Geschwister-Hosts**
über derselben `Mdex.Mcp.Tools`-Lib. Beide rufen die Operationen **in-process** auf.

```
LOKAL (Arbeitsrechner)                 REMOTE (Homeserver)

Claude Code ──stdio──▶ mdex-mcp        Handy/Web ──HTTPS──▶ Reverse-Proxy (TLS)
                          │                                      │
                          │                                 mdex-gateway
                          │                                  ① Bearer-Auth → Rolle
                          │                                  ② [MinRole]-Check
                          ▼ in-process                           ▼ in-process
                   Mdex.Mcp.Tools ───▶ Mdex.Core ◀─── Mdex.Mcp.Tools
                          ▼                                       ▼
              lokaler Checkout                          Server-Working-Copy
```

**Kein Bridge (B2, nicht B1).** Das Gateway ist **kein** Proxy, der HTTP↔stdio zu
`mdex-mcp` brückt, sondern ein **eigener** MCP-Server über HTTP, der dieselben Tools
in-process hostet. Begründung: B1 (Brücke) exponiert stdio zwar *nicht* ins Netz
(lokales IPC), kostet aber Session-/Subprozess-Plumbing und Argument-Injection-
Sorgfalt – und ist **mehr** Code, nicht weniger (das MCP-C#-SDK liefert den
HTTP-Transport via `MapMcp` fertig; die Brücke müsste man selbst bauen). „Just a
proxy" versteckt genau diese bewegliche Komplexität.

Transport-Wiring:
- `mdex-mcp`: Generic Host + `AddMcpServer().WithStdioServerTransport()`.
- `mdex-gateway`: ASP.NET Core + `AddMcpServer()` + `MapMcp().RequireAuthorization()`
  (Streamable HTTP; Legacy-SSE wie in Plan-2 §F nur bei Bedarf zugeschaltet).

---

## 3. In-Process verfeinert: `Data`-Objekt statt Envelope-Parse

Plan-2 §B rief in-process `CliDispatcher.RunToJson` (Envelope-**String**) und parste
ihn im `MdexBackend` zurück zu `JsonElement`. Dieser Serialize-dann-Reparse-Umweg
entfällt:

- Der Tool-Wrapper ruft die Operation über `Mdex.Core` und gibt das **`Data`-Objekt**
  zurück; das **MCP-SDK serialisiert es** als Tool-Ergebnis. Kein Zwischen-JSON.
- Fehler propagieren als **typisierte Exceptions** aus `Mdex.Core` (`CommandException`
  inkl. strukturierter `details`, z.B. `candidates` bei mehrdeutigem Slug). Der
  Tool-Layer mappt sie auf einen MCP-Tool-Error; **unerwartete** Exceptions werden
  generisch gemappt – nie Stacktrace/absoluter Pfad an den AI-Client (Plan-2 D-4).

Die guten Plan-2-Eigenschaften bleiben – sauberer: **eine Code-Bahn** (CLI-Verb und
MCP-Tool sind beide dünne Wrapper über dieselbe `Mdex.Core`-Operation, kein Drift),
**in-process = keine Shell = keine Command-Injection**, **Vault-Root serverseitig
gepinnt** (kein `--root` injizierbar).

---

## 4. Rollen: Leiter geerbt, Quelle je Host

Die **Drei-Rollen-Leiter** aus Plan-2 §D bleibt unverändert (geordnet, kumulativ,
default-deny, via `[MinRole]` + CI-Reflection-Guard erzwungen):

| Level | Rolle | schaltet kumulativ frei |
|---|---|---|
| 0 | `read` | 9 Read-Tools |
| 1 | `write-intent` | + 5 Intent-Tools (nicht-destruktiv: anlegen/anhängen nach Konvention) |
| 2 | `fullwrite` | + 5 Raw-FS-Tools (überschreiben/verschieben/löschen) |

Neu ist nur, **woher das Level kommt** – beide speisen denselben `ToolAuthorizer`:

- **Remote (`mdex-gateway`):** Level ← Token (Store-Lookup → Claim), wie Plan-2.
  Per Request/Session.
- **Lokal (`mdex-mcp`):** Level ← **Launch-Flag** `--role <read|write-intent|fullwrite>`,
  **kein Default**. Ohne Flag startet der Host **nicht** (Fehler, fail-closed). Das
  Level gilt für die gesamte Session (ein Nutzer, kein Per-Request-Token).

**Begründung kein-Default:** Berechtigung soll nie implizit sein – man muss sich bei
jedem Start bewusst entscheiden, was man der KI auf dem lokalen Checkout erlaubt.

**Lokales Threat-Model (knapp):** Kein Netz, Vertrauensgrenze = der OS-Nutzer (du).
Das Rollen-Flag ist hier **kein** Auth-Mechanismus, sondern eine **Leitplanke gegen
versehentliche destruktive KI-Aktionen** (z.B. `delete`/`move` ohne Absicht). Das
Containment (Pfad-Jail in `Mdex.Core`, Phase-1-Security-Fixes) schützt **beide**
Hosts, da geteilt.

---

## 5. Client-Topologie: Koexistenz lokal + remote

Das Kernrisiko: Beide Hosts exponieren **dieselben Tool-Namen**, zeigen aber auf
**verschiedene Working Copies**. In einem Client, der beide sieht, könnte das Modell
das Falsche greifen (remote schreiben, obwohl lokal gearbeitet wird).

**Zwei Registry-Ebenen (Claude-Ökosystem):**

| Ebene | registriert auf | fächert auf nach | → mdex |
|---|---|---|---|
| Account-Connectors | claude.ai (Web) | Web-Chat · Desktop-Chat · Cowork · Mobile | **remote** |
| Agent-Config | `.mcp.json` (Claude Code), `config.toml` (Codex CLI) | nur dieser Agent im Repo | **lokal** |

**Leitregel:** Ein Client nutzt **remote nur, wenn er keine eigene Working Copy hat**.
Wo ein Checkout liegt, nutzt er den **lokalen** MCP gegen genau diesen Checkout – und
das Remote ist dort **nicht** konfiguriert. Lokale Ziele und Remote-Konsumenten sind
damit disjunkt; keine Surface sieht je beide.

**Ein logischer Name, pro Umgebung gebunden:** Überall denselben Connector-Namen
verwenden (z.B. `mdex`); lokal an stdio (`mdex-mcp`), remote an die HTTPS-URL gebunden.
Das Modell sieht nie zwei Oberflächen – „welcher Vault" ist eine Eigenschaft der
Umgebung, keine Modell-Entscheidung.

**Desktop-Haken:** Claude Desktop kann **beides** (Account-Connectors ∪ lokale stdio).
Auflösung: **kein** lokales mdex in Desktop verdrahten – Desktop = Chat-Fläche →
remote (synchronisierte, kanonische Server-Kopie). Die lokale Fläche bleibt den
im-Repo-Agenten (Claude Code, Codex CLI) vorbehalten.

**Andere Anbieter (mit Vorbehalt – Verhalten ändert sich schnell, beim Einrichten
prüfen):** Codex CLI = lokale Config (wie Claude Code); ChatGPT / Codex-Cloud =
Account-/Connector-Ebene → remote. Dieselbe Zweiteilung, der „ein Name pro Umgebung"-
Trick überträgt sich.

**Notnagel** bei unvermeidbarem Overlap: distinkte Namen (`mdex` lokal-default /
`mdex-remote`) + Unterscheidung in den MCP-Server-`instructions` + eine Zeile in
`CLAUDE.md` („Vault-Ops über `mdex`; `mdex-remote` nur auf Ansage"). Dokumentierter
Ausnahmefall, nicht der Normalfall.

---

## 6. Unverändert aus Plan 2 übernommen

Diese Plan-2-Abschnitte gelten **unverändert** (für `mdex-gateway` statt „mdex-mcp"
und `mdex-token` statt „mdex-mcpctl" zu lesen):

- **Sicherheits-Annahmen & Threat-Model** (Plan-2 oben): Phase-1-Containment-Fixes
  als Voraussetzung; Exposition nur auf `127.0.0.1` hinter TLS-Proxy; Angreifer-
  Modelle 1–3; `tokens.json` `0600`/`0700`.
- **Token-Store** (Plan-2 §D): `~/.config/mdex-gateway/tokens.json` (Pfad folgt dem
  Gateway-Namen, löst Plan-2s `mdex-mcp`-Pfad ab), nur SHA-256-Hashes,
  Token-Format `mdex_<base64url(32B)>`, atomares Schreiben (Temp + rename), SHA-256
  unsalted korrekt (256 Bit Entropie).
- **Auth-Mechanik & Hot-Reload** (Plan-2 §D): `AuthenticationHandler` →
  `MapMcp().RequireAuthorization`; `[MinRole]` + `ToolAuthorizer` default-deny mit
  CI-Reflection-Guard; FileSystemWatcher → `revoke` wirkt ohne Restart; mcpctl
  (= `mdex-token`) und Server fassen einander nie an, teilen nur die Datei.
- **Token-Admin** (Plan-2 §E): `mdex-token new/list/revoke`, Roh-Token einmalig.
- **Deployment-Koexistenz mit Auto-Commit** (Plan-2 §G, Gesamt-§3/§10): Server
  schreibt nur Dateien, `mdex-autocommit.sh` bleibt einziger Git-Akteur.
- **Tool-Oberfläche (14 Tools)** (Plan-2 §C): Read 9 / Intent 5 / Raw-FS 5,
  `create_note` als ein Tool. Diese Tools leben jetzt in `Mdex.Mcp.Tools` und werden
  von **beiden** Hosts geteilt.

---

## 7. `--json`: kein eingefrorener Vertrag mehr

Im alten Modell war die CLI-`--json`-Ausgabe der **Transport**, den der MCP
(Subprozess) parste – deshalb musste sie eingefroren sein. Mit B2/in-process ist
dieser Konsument **weg**. Es bleiben zwei *unabhängige* Projektionen derselben Quelle:

- **CLI-`--json`** = `CommandResult.Data` im `JsonEnvelope` (für dich / Scripting / Debug).
- **MCP-Tool-Ergebnis** = **dasselbe** `Data`-Objekt, vom MCP-SDK serialisiert.

Folgen:
- **Debug-Spiegel** (CLI zeigt, was das MCP-Tool liefert) ist **strukturell**
  garantiert (geteiltes `Data`), nicht durch Einfrieren – sie können nicht driften.
- **Eigenes Scripting** profitiert von Stabilität, aber **selbstauferlegt**, kein
  prozessübergreifender Vertrag: Form ändern = eigene Skripte atomar nachziehen.

**Stabilisierungswürdig ist daher das `Data`-Objekt in `Mdex.Core`** (die eine
Quelle), nicht „der `--json`-Vertrag". `--json` bleibt erhalten (gebaut, getestet,
nützlich) – nur mit gesenkter Stabilitätspflicht. Die Plan-1-Formulierung „stabiler
Vertrag, den der MCP konsumiert" entfällt.

---

## 8. Sicherheit: Threat-Model (lokal + remote) & Phase-2-Delta

> Das ursprüngliche Phase-2-Design-Review
> ([2026-06-05-mdex-security-review-plan.md](2026-06-05-mdex-security-review-plan.md))
> prüfte das **abgelöste** Plan-2-Design. Dieser Abschnitt ist das **Delta-Review** auf
> die neue Form (zwei Hosts, Core-Extraktion) und konsolidiert das Threat-Model.

### Threat-Model je Host

**Remote (`mdex-gateway`)** — unverändert aus Plan-2: internet-exponiert via
TLS-Reverse-Proxy, Kestrel loopback-only. Angreifer: (a) Netzwerk-Client ohne/mit
geratenem Token → `401`; (b) bösartiger AI-Client *mit* gültigem Token, der seine Rolle
übersteigen oder den Vault verlassen will → Rollen-Leiter + Containment.

**Lokal (`mdex-mcp`)** — neue Fläche, *anderes* Modell: **kein Netz-Listener** (stdio,
vom Client gespawnt), Vertrauensgrenze = der OS-Nutzer. „Aus dem Vault ausbrechen" ist
für den Operator kein Angriff (er hat ohnehin Shell-Zugriff). Der reale Gegner ist ein
**prompt-injizierter / verwirrter AI-Caller** (z.B. durch bösartigen Notiz-/Repo-Inhalt):

- **Containment** (geteiltes `Mdex.Core`) hält ihn *im* Vault → kein Ausbruch.
- **Rollen-Flag** (fail-closed, kein Default) staffelt die Sprengkraft: mit `read` /
  `write-intent` kann selbst ein gekaperter Caller nicht `delete`/`move`/überschreiben.
- **Git** macht jede Mutation reversibel.

Der lokale Host hat damit **weniger** Angriffsfläche als der Gateway: keine Netz-Fläche,
keine Secrets-at-rest (kein Token), kein Bind-Risiko (D-7 n/a).

### Befunde-Remap (Phase 2 → neues Design)

| Phase-2-Befund | im neuen Design |
|---|---|
| D-1 (Ladder ruht auf Containment) | **erfüllt** — Containment in `Mdex.Core`, von beiden Hosts geteilt |
| D-2 (default-deny / Guard-Test) | gilt jetzt für `Mdex.Mcp.Tools` (beide Hosts) |
| D-3 / D-6 (tokens.json Perms / atomar) | Gateway-only, unverändert (Pfad → `~/.config/mdex-gateway/`) |
| D-4 (Stacktrace-Leak) | **verschoben** in den Tool-Layer (kein `RunToJson`-Boundary mehr, §3) |
| D-5 (Root-Wipe `delete ""`) | durch Phase-1-Fix in `Mdex.Core` gedeckt |
| D-7 (TLS/Bind) | Gateway-only, unverändert; lokal n/a (kein Listener) |
| D-8 (keine Token-Expiry) | unveränderter Tradeoff |

### Neue Befunde aus dem Delta

| # | Sev | Befund | Konsequenz |
|---|-----|--------|-----------|
| N-1 | ℹ️ INFO | Lokaler Gegner = prompt-injizierter Caller, nicht Netz | bewusst akzeptiert; Verteidigung Flag + Containment + Git (s.o.) |
| N-2 | 🟡 LOW | Core-Extraktion könnte den einzigen PathGuard-Chokepoint / die Phase-1-Fixes regressieren | Extraktions-Phase: Containment-Tests wandern mit, bleiben grün (Gate); einziger Chokepoint erhalten |
| N-3 | 🟡 LOW | `[MinRole]`/Guard-Test muss auf geteiltem `Mdex.Mcp.Tools` laufen; lokaler Flag-Pfad ungetestet | Guard-Test gegen die geteilte Lib; Test „`--role read` verweigert Write-Tools" |
| N-4 | 🟡 LOW | Remote-Bearer-Token in lokaler Agent-Config = unnötige Creds auf der Dev-Maschine | Topologie-Regel (§5) hat Creds-Hygiene-Dimension: Remote-Token **nie** lokal konfigurieren |

**Fazit des Delta:** **Kein neuer HIGH.** Die neue Form erbt Containment + Rollen-Leiter
aus `Mdex.Core`; der lokale Host bringt *weniger* Fläche mit, nicht mehr. Die Arbeit liegt
in Regressions-Schutz (N-2) und dem Verschieben zweier bestehender Anforderungen (D-2/D-4)
auf die geteilte Tool-Lib.

### Positiv-Befunde (übernommen)

In-Process = keine Shell = keine Command-Injection; Vault-Root serverseitig gepinnt (kein
`--root`-Inject); nur Hashes gespeichert; Hot-Reload fällt sicher (korrupte Datei → leerer
Store → `401`); Auth-Fehler loggen den Token nicht.

---

## Designprinzipien-Bezug

- **Ein Tool, ein Zweck:** vier klar abgegrenzte Binaries; `Mdex.Core` als einzige
  Operations-Quelle; Token-Admin und Gateway über geteilte Datei entkoppelt.
- **Smart grep, not a brain:** keine neue Retrieval-Logik; beide Hosts reichen
  dieselben `Mdex.Core`-Operationen durch.
- **Nichts geht verloren:** Git deckt jede Mutation; die Rollen-Leiter staffelt nach
  Gefährlichkeit – remote per Token, lokal per bewusstem Flag.
- **YAGNI mit Notausgang:** kein Bridge, kein OAuth, kein Index – jeweils reversibel.

## Offene Punkte / nächste Schritte

- **Implementierungsplan** (neu) für die 4-Binary-Familie – ersetzt
  `2026-06-04-mdex-mcp-plan.md`. Eigener Schritt (noch nicht angefangen).
- **Ablöse-Reihenfolge:** `Mdex.Core` herausziehen (Refactor an bestehendem
  `Mdex.Cli`) → `Mdex.Mcp.Tools` → `mdex-mcp` (lokal, zuerst nutzbar) → `Mdex.Tokens`
  + `mdex-token` → `mdex-gateway`.
- Übernommen aus Plan 2: Bootstrapper / Neue-Maschine-Setup, OAuth-Metadata,
  Infisical, `sync`-Tool, evtl. `create_*`-Aufspaltung – alle reversible Nachrüst-Pfade.
