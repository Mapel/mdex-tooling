# Design: mdex-mcp & mdex-mcpctl – Remote-MCP-Server + Token-Admin (Plan 2)

> **Datum:** 2026-06-04
> **Status:** Entwurf (zum Review)
> **Kontext:** Brainstorming-Session, baut auf
> [2026-06-01-mdex-tooling-design.md](2026-06-01-mdex-tooling-design.md) (Gesamtdesign)
> und [2026-06-02-mdext-cli-plan.md](2026-06-02-mdext-cli-plan.md) (Plan 1, CLI – fertig)
> auf. Dies ist **Plan 2 von 3**.

---

## Ausgangslage & Scope

Plan 1 (`mdext`-CLI) ist gebaut: jede Vault-Operation existiert als Verb mit
stabiler `--json`-Envelope (`{ ok, command, data }` bzw.
`{ ok, command, error, details? }`). Dieser Kontrakt ist die Grundlage hier.

**Dieser Plan umfasst zwei Binaries zusammen** (damit der Server am Ende sofort
real nutzbar und der ganze Auth-Pfad end-to-end testbar ist):

- **`mdex-mcp`** – der Remote-MCP-Server (ASP.NET Core + MCP-C#-SDK).
- **`mdex-mcpctl`** – das Token-Admin-CLI (Token anlegen/auflisten/widerrufen).

**Nicht in diesem Plan:** der Bootstrapper / „neue-Maschine-Setup" (eigener
späterer Schritt), Index-Cache, OAuth-Metadata, Infisical, `sync`-Tool. Alle
bleiben wie im Gesamtdesign als reversible Nachrüst-Pfade offen.

## Abweichungen vom Gesamtdesign (mit Begründung)

Zwei Entscheidungen des 2026-06-01-Designs werden hier bewusst revidiert. Sie
**superseden** die dortigen Stellen; die alte Begründung war in beiden Fällen
YAGNI bzw. maximale Treue, das neue Argument trifft jeweils die echte Grenze.

1. **In-Process statt Subprozess (supersedes §5).** Der Server ruft die
   mdext-Operationen **in-process** auf, nicht als gespawnten `mdext`-Kindprozess.
   Begründung: CLI und Server liegen in derselben Solution; ein In-Process-Aufruf
   über **denselben Dispatcher und dieselbe `JsonEnvelope`** liefert exakt dieselbe
   Ausgabe (kein Drift – das war das eigentliche Ziel von §5), spart den Spawn,
   gibt sauberere Fehler/Tests. Die einzige verlorene Eigenschaft – „der Server
   shellt wortwörtlich raus" – ist nicht inhaltlich relevant: das Terminal-`mdext`
   bleibt dasselbe Binary, jeder Tool-Aufruf ist 1:1 im Terminal nachstellbar.

2. **Drei Rollen statt zwei (supersedes §2).** Statt `read` / `read-write` gibt es
   die geordnete Leiter **`read` ⊂ `write-intent` ⊂ `fullwrite`**. Begründung: Die
   Grenze zwischen den **Intent-Tools** (nur anlegen/anhängen nach Konvention,
   nicht-destruktiv) und den **Raw-FS-Tools** (überschreiben, verschieben, löschen)
   ist die eigentliche Gefahrengrenze. Ein gehostetes/schwächeres Modell kann so
   produktiv mitschreiben (`write-intent`), ohne den Vault umgraben zu können
   (`fullwrite`). Der Aufwand ist minimal (siehe Rollen-Modell), die Tool-Gruppen
   existieren bereits genau entlang dieser Linie.

---

## Sicherheits-Annahmen & Threat-Model

> Ergänzt nach dem Security-Design-Review (Phase 2,
> [2026-06-05-mdex-security-review-plan.md](2026-06-05-mdex-security-review-plan.md)).

**Voraussetzung (eigener Plan):** Dieser Server setzt voraus, dass die Phase-1-
Containment-Fixes an `mdext` bereits gelandet sind
([2026-06-05-mdext-security-fixes-plan.md](2026-06-05-mdext-security-fixes-plan.md)):
jede Datei-Operation – **auch die Slug-/Pfad-Auflösung** – ist auf den Vault-Root
gejailt; leere/Root-Ziele und Symlink-Ausbrüche sind abgewiesen. **Ohne diese
Fixes ist die Rollen-Leiter wirkungslos** (`read` = „lies jede `.md` am Server",
`write-intent` = „beschreibe jede existierende `.md`").

**Exposition:** Der Server bindet ausschließlich auf `127.0.0.1` und steht hinter
einem TLS-terminierenden Reverse-Proxy. Bearer-Token gehen nie im Klartext übers
Netz; der Klartext-Hop existiert nur auf dem Loopback (Proxy → Kestrel).

**Angreifer-Modell:**
1. **Netzwerk-Client ohne/mit geratenem Token** → 256-Bit-Zufallstoken, nur Hash
   gespeichert, kein Treffer → `401`.
2. **Kompromittierter/bösartiger AI-Client mit gültigem Token**, der (a) seine
   Rolle übersteigen oder (b) den Vault verlassen will → Rollen-Leiter (default-deny,
   §D) bzw. das mdext-Jail (Voraussetzung oben) fangen das ab.
3. **Lokaler Schreibzugriff auf `tokens.json`** = Vollkompromiss: wer die Datei
   schreiben kann, injiziert sich einen `fullwrite`-Hash (der Watcher vertraut der
   Datei blind). Daher Datei `0600`, Verzeichnis `0700`.

**Bewusste Sicherheits-Eigenschaften (keine offenen Punkte):**
- **In-Process = keine Shell = keine Command-Injection.** Der größte theoretische
  Risikopfad bei „AI ruft CLI-Verben mit Freitext-Args" ist by-design eliminiert
  (der eigentliche Wert von §5, hier ohne Subprozess erreicht).
- **Vault-Root serverseitig gepinnt.** `MdexBackend` übergibt den konfigurierten
  Root; Tool-Args gehen als Positionals hinter `--`. Der AI kann **kein** `--root`
  injizieren.
- **Nur Hashes gespeichert**, Roh-Token einmalig angezeigt. SHA-256 *unsalted* ist
  korrekt: der Token hat 256 Bit Entropie → kein Brute-/Rainbow-Risiko, kein
  bcrypt/argon nötig. Verifikation hasht den präsentierten Token und vergleicht
  **Hashes** (nicht Roh-Token) → ein nicht-konstanter Vergleich leckt höchstens
  einen preimage-resistenten Hash; **Constant-Time ist daher nicht zwingend**.
- **Hot-Reload fällt sicher:** korrupte/halb-geschriebene Datei → leerer Store →
  alle Anfragen `401` (fail-closed). mcpctl schreibt **atomar** (Temp + rename).

**Akzeptierter Tradeoff:** Token haben **keine Ablaufzeit** (gültig bis `revoke`).
Bewusst für langlebige AI-Client-Creds; Rotation/Expiry bleibt reversibler
Nachrüst-Pfad.

---

## A. Solution-Layout & Projekte

```
src/
  Mdex.Cli/        (bestehend) → mdext          vault-only CLI, human + --json
  Mdex.Tokens/     NEU  (Lib)                    Token-Store: Modell, SHA-256, Lesen/Schreiben/Watch
  Mdex.Mcp/        NEU  (ASP.NET Core) → mdex-mcp   Remote-MCP-Server
  Mdex.Mcpctl/     NEU  (Console)      → mdex-mcpctl token new/list/revoke
tests/
  Mdex.Cli.Tests/      (bestehend)
  Mdex.Tokens.Tests/   NEU
  Mdex.Mcp.Tests/      NEU  (Integration via WebApplicationFactory)
```

Referenzen:
- `Mdex.Mcp` → `Mdex.Cli` (für die Operationen) **und** `Mdex.Tokens` (Auth).
- `Mdex.Mcpctl` → nur `Mdex.Tokens`.
- `Mdex.Tokens` ist die **einzige** Stelle, die das `tokens.json`-Format und das
  Hashing kennt. Server (verifizieren/watchen) und mcpctl (verwalten) teilen sich
  diese Lib – keine doppelte Store-Logik, kein Drift im Format.

**Gezielter Refactor an `Mdex.Cli`** (weil wir darin arbeiten): Die Dispatch-Logik
steckt heute inline in `Program.cs`. Sie wird in einen wiederverwendbaren Einstieg
herausgezogen, z.B.

```csharp
public static class CliDispatcher
{
    // liefert immer die JSON-Envelope (ok/data bzw. ok=false/error/details)
    public static string RunToJson(string[] args, CliContext ctx);
}
```

`Program.cs`/`Main` ruft ihn fürs CLI auf, `Mdex.Mcp` für die Tools. **Eine**
Code-Bahn. Der Refactor ändert das beobachtbare CLI-Verhalten nicht (bestehende
Tests bleiben grün).

## B. Backend-Aufruf (in-process)

Ein dünner `MdexBackend` in `Mdex.Mcp` kapselt den Aufruf:

- Baut pro Tool die `args` (Verb + Flags) und einen `CliContext` (Vault-Root aus
  Config), ruft `CliDispatcher.RunToJson`, parst die Envelope zu `JsonElement`.
- Bei `ok=false` wird ein Backend-Fehler geworfen, den der Tool-Layer in einen
  MCP-Tool-Error übersetzt – **inklusive `details`** (z.B. `details.candidates`
  bei mehrdeutigem Slug; der in Plan 1 erweiterte Kontrakt zahlt sich hier aus).
- **Unerwartete Exceptions** (keine `CommandException`) werden bereits im
  `CliDispatcher` abgefangen und auf eine generische Fehler-Envelope gemappt – nie
  als .NET-Stacktrace/absoluter Pfad an den AI-Client (Details nur serverseitig
  geloggt). Schließt das Info-Leak-Finding (Phase 2 D-4); siehe Plan-Task 1.
- Inhalt (`data`) wird unverändert als Tool-Ergebnis durchgereicht.

## C. MCP-Tool-Oberfläche (14 Tools)

Jedes Tool = schlanke `[McpServerTool]`-Methode, die Parameter → mdext-Verb mappt.
Tool-Namen snake_case (MCP-Konvention) → CLI-Verben kebab-case. Jedes Tool trägt
ein **Mindest-Rollen-Level** (siehe D), aus dem die Autorisierung gespeist wird.

**Read (Level `read`, 9):** `search`, `get_by_tag`, `list_tags`, `search_tags`,
`get_by_type`, `get_by_date_range`, `read_note`, `list`, `find_related`

**Intent (Level `write-intent`, 5):** `capture_inbox`, `create_note`,
`append_to_note`, `append_to_log`, `promote_inbox_item`

**Raw FS (Level `fullwrite`, 5):** `write_file`, `append_file`, `move`, `mkdir`,
`delete`

> **`create_note` als ein Tool:** Die CLI hat nur `create <type> <name>`; das
> MCP-Tool bildet das 1:1 ab (`create_note(type, name, …)`) statt vier getrennter
> `create_*`-Tools. Begründung: eine Code-Bahn, deckungsgleich mit der CLI.
> Reversibel, falls die KI getrennte Tools nachweislich besser trifft.

## D. Rollen-Modell, Token-Store & Auth

### Rollen-Leiter

| Level | Rolle | schaltet kumulativ frei |
|---|---|---|
| 0 | `read` | die 9 Read-Tools |
| 1 | `write-intent` | + die 5 Intent-Tools (nicht-destruktiv: nur anlegen/anhängen nach Konvention) |
| 2 | `fullwrite` | + die 5 Raw-FS-Tools (überschreiben/verschieben/löschen) |

Implementierung: Rolle = geordnetes Enum. Jedes Tool deklariert ein
Mindest-Level. Check = `tokenLevel >= toolLevel`. Mehr ist es nicht.

### Token-Store

Datei **außerhalb des Vault-Repos**: `~/.config/mdex-mcp/tokens.json`. Liste von:

```json
{ "hash": "<sha256-hex>", "role": "read|write-intent|fullwrite",
  "label": "langdock", "created": "2026-06-04" }
```

- Gespeichert wird **nur** der SHA-256-Hash des Tokens, nie der Roh-Token.
- **Token-Format:** `mdex_<base64url(32 Random-Bytes)>`. Bei `token new` einmalig
  angezeigt, danach nur noch der Hash im Store.
- **Dateirechte:** Datei `0600`, Verzeichnis `0700` (POSIX). Schreibzugriff auf die
  Datei = Vollkompromiss (siehe Threat-Model). Geschrieben wird **atomar**
  (Temp-Datei + rename), damit der watchende Server nie eine halbe Datei liest.

### Auth-Mechanik (Request-Lebenslauf)

1. Client → HTTPS → Reverse-Proxy (TLS) → Kestrel auf `localhost`.
2. **`AuthenticationHandler`** liest `Authorization: Bearer <token>`, hasht
   (SHA-256), schlägt im Store nach → setzt die Rolle als Claim. Kein Treffer →
   `401`. Gebunden an die MCP-Route via `MapMcp().RequireAuthorization(...)`.
3. **Pro-Tool-Check (default-deny):** Jedes Tool deklariert sein Mindest-Level über
   ein `[MinRole]`-Attribut und ruft zur Laufzeit `ToolAuthorizer.Require` mit der
   Rolle aus dem `HttpContext`. **Default-deny ist als CI-Invariante erzwungen:** ein
   Reflection-Guard-Test rotet den Build, wenn ein `[McpServerTool]` kein `[MinRole]`
   (oder keinen `ToolAuthorizer`) trägt – ein vergessenes Tor öffnet so kein Tool
   (Phase 2 D-2). `tokenLevel < toolLevel` → Fehler (`403`-Äquivalent als Tool-Error).
   Read-Tools brauchen nur einen gültigen Token; Intent-/Raw-FS-Tools das entsprechende
   Level. *Zielzustand:* ein zentraler Invocation-Filter liest `[MinRole]` und ersetzt
   die Pro-Methoden-Aufrufe, sobald der SDK-Hook bestätigt ist.
4. Tool-Handler → `MdexBackend` → Ergebnis zurück.

### Hot-Reload des Stores

Der Server **überwacht `tokens.json`** (FileSystemWatcher, entprellt) und lädt bei
Änderung neu. Folge: `token revoke` über mcpctl wirkt in Sekunden, **ohne
Service-Restart**. Bewusst gewählt gegenüber „nur beim Start lesen", weil ein
geleakter Token sonst bis zum Neustart gültig bliebe.

**Wichtig – Entkopplung:** mcpctl und Server fassen einander **nie** an. Sie teilen
ausschließlich die Datei `tokens.json`. mcpctl schreibt, der Server watcht – keiner
kennt den Lebenszyklus des anderen (sauberes §5-Prinzip).

## E. `mdex-mcpctl`

Reiner Store-Schreiber, ohne jede Kenntnis des laufenden Servers:

- `token new --role <read|write-intent|fullwrite> --label <x>` → generiert Token,
  hasht, hängt Eintrag an, zeigt den **Roh-Token einmal** an.
- `token list` → Label / Rolle / Datum (nie der Roh-Token).
- `token revoke <label>` → Eintrag entfernen.

Nach `new`/`revoke` genügt ein kurzer Hinweis, dass der Server die Änderung
automatisch übernimmt (Hot-Reload, Abschnitt D) – **kein** Restart-Aufruf, **keine**
Kopplung an systemd. Teilt mit dem Server nur den Pfad zur `tokens.json`
(Default `~/.config/mdex-mcp/tokens.json`, per Config/Flag überschreibbar).

## F. Transport & Hosting

- `AddMcpServer().WithTools…()`, dann `MapMcp().RequireAuthorization()`.
- **Beide Transports:** Streamable HTTP ist der Default von `MapMcp`; die
  Legacy-SSE-Endpunkte (`/sse`, `/message`) sind im SDK standardmäßig aus und
  werden via `HttpServerTransportOptions.EnableLegacySse = true` zugeschaltet.
- **Config** (Quelle: `appsettings.json` + Env-Vars, Env gewinnt): `MDEX_ROOT`,
  Listen-URL/Port, Pfad zur `tokens.json`. Kestrel bindet auf `127.0.0.1` hinter
  dem Reverse-Proxy (nie öffentlich ohne TLS-Proxy – siehe Threat-Model).

## G. Deployment

- Single-File-Publish; systemd-Service auf dem Homeserver, hinter dem bestehenden
  Reverse-Proxy (TLS-Terminierung dort).
- `MDEX_ROOT` zeigt auf die Server-Working-Copy des Vaults.
- **Koexistenz mit Auto-Commit (unverändert §3/§10):** Der Server schreibt nur
  Dateien; `mdex-autocommit.sh` bleibt der einzige Git-Akteur. Kein Lock, kein
  zweiter Git-Akteur.
- Update = Binary tauschen + Service-Restart. (Token-Änderungen brauchen **keinen**
  Restart, siehe D.)

## H. Tests

- **`Mdex.Tokens.Tests`:** SHA-256-Hashing, Store-Roundtrip (new→list→revoke),
  Token-Format-Validierung, Hot-Reload-Reaktion auf Dateiänderung.
- **`Mdex.Mcp.Tests` (Integration, `WebApplicationFactory`):**
  - kein/ungültiger Token → `401`.
  - `read`-Token auf ein Intent-Tool → blockiert; `write-intent`-Token auf ein
    Raw-FS-Tool → blockiert; passendes Level → Erfolg.
  - je ein Read- und ein Write-Tool end-to-end gegen einen Temp-Vault;
    Envelope-Gleichheit mit der direkten CLI-Ausgabe.
- **mcpctl:** new→list→revoke-Roundtrip; Roh-Token nur einmal sichtbar.

---

## Designprinzipien-Bezug

- **Ein Tool, ein Zweck / Entkopplung:** mcpctl schreibt den Store, der Server
  watcht ihn – getrennte Lebenszyklen, geteilte Datei (§5 sauber umgesetzt).
- **Smart grep, not a brain:** keine neue Retrieval-Logik; der Server reicht die
  bestehende CLI-Operation durch.
- **Nichts geht verloren:** Git deckt jede Mutation; die Rollen-Leiter schützt
  zusätzlich nach Gefährlichkeit gestaffelt.
- **YAGNI mit Notausgang:** kein OAuth, kein Index, kein Ordner-Scoping – jeweils
  dokumentierter reversibler Nachrüst-Pfad.

## Offene Punkte / spätere Schritte

- Bootstrapper / „neue-Maschine-Setup" (eigener Plan).
- OAuth-Metadata-Endpoints, falls je ein Client sie zwingend verlangt.
- Infisical als injizierter Token-Store statt Datei.
- Explizites `sync`-Tool / kürzerer Auto-Commit-Debounce, falls Git-Latenz stört.
- Ob `create_*` doch in vier getrennte Tools aufgeteilt wird (falls die KI sie
  nachweislich besser trifft).
