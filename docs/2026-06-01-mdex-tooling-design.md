# Design: mdex-tooling – CLI, MCP-Server & Token-Admin für den Vault

> **Datum:** 2026-06-01
> **Status:** Entwurf (zum Review)
> **Kontext:** Brainstorming mit Michael, Ergebnis dieser Session.

---

## Ausgangslage

mdex ist Plain-Markdown + Git auf dem Homeserver (`mdadmin@home.rum2011.de`),
mobil per Syncthing/Markor, Desktop per Git. Ein KI-Zugriff existiert bisher nur
manuell.

Normale Integrationen (GitHub-MCP, SharePoint-API, WebDAV) sind für Modelle zu
mechanisch: Welche Datei, welcher Pfad, vorher lesen?, wie schreiben ohne
Konflikt?, welche API-Operation? Lesen geht oft noch, **Schreiben/Verschieben/
strukturiertes Ablegen wird fehleranfällig**.

Drei Quellen prägen das Design:

- **library-mcp** (Will Larson) – MCP über einen Markdown+Frontmatter-Vault,
  bewusst **kein RAG/Embeddings**, sondern Tag-/Text-/Datums-Retrieval. Tools:
  `get_by_tag`, `get_by_text`, `get_by_slug_or_url`, `get_by_date_range`,
  `search_tags`, `list_all_tags`, `rebuild`.
- Eigene Referenz [[local-llm-als-pkm-daily-driver]] – *„treat it like a smart
  grep, not a brain"*, Zitationspflicht, **bei < 1000 Notizen schlägt
  Grep/Tool-Calling RAG**, Bearbeitung bestehender Notizen = reviewbarer Diff.
- Die [[braid-karte]] – **Bibliothekar-Prinzip**: nichts geht ohne Michaels
  Entscheidung verloren; KI darf Referenz sortieren, eigenes Denken nur
  vorbereiten.

**Korrektur gegenüber dem Vorgespräch:** Der dort genannte Content-Typ
„Reflexion" existiert in der aktuellen BRAID-Karte **nicht mehr** (durch das
flüchtige Recap-Ritual ersetzt). Das Intent-Layer richtet sich am *jetzigen*
Modell aus: `inbox` / `zettel` / `projects` / `areas` / `resources` +
`abschlussindex`.

## Ziel

Ein schlanker, robuster **Remote-MCP-Server**, der KI-Systemen einen
*mdex-gerechten* Arbeitszugang gibt: nicht so roh wie ungeführte Shell/Git, nicht
so primitiv wie nur `inbox()`/`search()`, sondern **Guarded Filesystem +
mdex-Intent-Layer**. Die KI spricht nicht „GitHub/API/Shell", sondern „mdex in
mdex-Begriffen".

**Nicht-Ziel:**
- RAG / Embeddings / Vektor-DB (Vault zu klein, Grep reicht).
- Multi-Mandanten: Kunden-KI, Sandbox-Ordner, Ordner-Scoping (Overkill; bei
  Bedarf wird Material rauskopiert).
- Git-Logik im Tool (siehe §3).
- Eigenes Auth-Framework / vollständiges OAuth (siehe §8).

---

## Entscheidungen (mit Begründung)

### 1. Reichweite: Remote, mehrere Clients

MCP läuft **remote** (HTTPS, Homeserver), weil auch gehostete Tools wie Langdock
drankommen sollen (lokales stdio würde die ausschließen). Mehrere Clients sind
vorgesehen.

### 2. Rechte: N Tokens → 2 Rollen

Einzige nötige Unterscheidung: ein Key, der **nur lesen** darf, und einer, der
auch **schreiben** darf (Schutz dagegen, dass schwächere Modelle „Quatsch
zurückschreiben"). **Kein** Ordner-Scoping, **keine** Mandanten.

- **Zwei Rollen** (`read` / `read-write`), aber **beliebig viele Tokens** – jeder
  Client bekommt einen eigenen Token mit Rolle + Label, einzeln widerrufbar.
- **Gehasht at-rest:** Der Server speichert nur den **SHA-256-Hash** + Rolle +
  Label, nie den rohen Token. Roh-Token wird einmal bei Erstellung angezeigt.

### 3. Schreiben: volle FS-Ops, keine Git-Logik im Tool

Der `read-write`-Key darf **alle** FS-Operationen inkl. Verschieben und Löschen.
**Sicherheitsnetz ist allein Git** (Historie) + die Rollen-Trennung – *nicht* ein
Commit pro Operation. Begründung: Commit-pro-Mutation würde die GitHub-MCP-
Awkwardness reproduzieren (KI muss Git „sprechen"), die hier gerade vermieden
werden soll.

- **Keine Git-Logik im MCP/CLI.** Der bestehende `mdex-autocommit.sh` (inotify +
  30-Min-Debounce) bleibt der **einzige Git-Akteur**. Der MCP/das CLI schreibt
  nur Dateien – für den Watcher nicht von einer Mobil-Änderung unterscheidbar.
- Bewusst akzeptierte Trade-offs:
  1. Keine Per-Tool-Provenance im Commit (nur „auto: \<ts>"). Egal – der Diff
     zeigt die Änderung, Schutz ist der `read`-only-Key.
  2. Git-Propagation hinkt bis zum nächsten Debounce (≤ 30 Min). Für Mobil
     irrelevant (Syncthing synct Dateien sofort, an Git vorbei); nur der
     Git-Mirror anderer Git-Clients ist verzögert.
  3. Eine Datei, die die KI *im selben Fenster* anlegt **und** löscht, taucht nie
     in der History auf. Kein Verlust echten Wissens; bestehende Dateien sind
     immer in der History → Löschen bleibt revertierbar.
- *„Nichts geht verloren"* gilt damit weiter auf **Repo-Ebene**.
- Optionale spätere Ergänzung, falls die Latenz aus (2) je stört: ein explizites
  `sync`-Tool oder kürzerer Debounce – **nicht** per Mutation.

### 4. Sprache: C# / .NET

Für ein persönliches Tool, das Michael langfristig **selbst wartet**, gewinnt die
Sprache, in der er flüssig ist – das ist das Hauptkriterium, kein Kompromiss.
Dazu:

- Offizielles **MCP-C#-SDK** (`ModelContextProtocol`, von Microsoft + Anthropic) –
  C# ist erstklassig unterstützt.
- **ASP.NET Core / Kestrel** ist für den Remote-Server + Token-Auth-Middleware
  ein Heimspiel (eher mehr als Python).
- Cross-Platform (Linux-Server + Windows-Entwicklung), **Single-File-Publish**
  fürs Homeserver-Deployment.
- library-mcp wird **nicht geforkt**, nur ideell übernommen → kein Python-Zwang.

### 5. Architektur: CLI ist das Fundament, MCP ist Subprozess-Frontend

Kein separater, als Produkt herausgehobener „Core" mit zwei abgeleiteten
Frontends. Stattdessen:

- **`mdext`-CLI** = das eigentliche Terminal-Tool und Fundament. **Vault-only.**
  Zwei Ausgabemodi: human-lesbar **und** `--json` (Maschinenmodus).
- **MCP-Server** (`mdex-mcp`) = dünnes Frontend, das pro Tool `mdext … --json` als
  **Subprozess** aufruft und das JSON durchreicht. Reimplementiert keine Logik →
  CLI und MCP können nicht driften.

Begründung: eine einzige, wörtliche Implementierung jeder Operation; menschlich
nachvollziehbar (man kann exakt das im Terminal ausführen, was die KI ausführt);
passt zur Unix-Philosophie ([[unix-philosophie-komplexitaetsgrenze]]). Der
Prozess-Spawn pro Aufruf ist bei dieser Vault-Größe (Millisekunden) irrelevant.
Die `--json`-Ausgabe ist ein **stabil zu haltender Vertrag**.

**„Vault-only" heißt:** `mdext` macht ausschließlich Operationen am *Vault-Inhalt*
(anlegen, lesen, suchen, ändern, verschieben) und fasst nie eine Datei außerhalb
des Vault-Roots an. Es übernimmt **keine** Nachbar-Belange – kein Git/Sync (das
macht der Auto-Commit-Dienst), keine Server-/Token-Verwaltung (das macht
`mdex-mcpctl`). „Vault-only" ≠ read-only: schreiben darf `mdext` sehr wohl.

**Token-Verwaltung lebt getrennt** in einem eigenen kleinen Admin-Binary
(`mdex-mcpctl`), damit `mdext` rein vault-bezogen bleibt. Es gehört zum
**MCP-Server**, nicht zum Vault (es verwaltet dessen Tokens); Server und Admin-CLI
teilen sich nur den serverseitigen Token-Store (keine mdext↔MCP-Kopplung).

**Schicksal der bestehenden `pkmt`-Subkommandos:** Das heutige Bash-`pkmt` hat
zwei Befehle. `new` ist eine **Content-Operation** → wird von `mdext` als
Intent-Tools (`capture_inbox`, `create_*`, `promote_inbox_item`) reimplementiert.
`sync` (SSH + commit/push/pull) ist eine **Git/SSH-Operation**, keine
Content-Operation → **außerhalb des Scopes** dieses Projekts. `sync` gehört
bewusst nicht in `mdext`; es bleibt vorerst das bestehende Bash-`pkmt sync`
(Desktop-Konvenienz, unabhängig vom MCP).

### 6. Kein Index – Laufzeit-Auflösung

Der Graph (Links/Backlinks) ist dünn, der Vault klein. Ein persistenter Index
wäre vorgezogene Optimierung. Jeder CLI-Aufruf liest die Dateien **frisch**:
Frontmatter + Inhalt scannen (Millisekunden bei Dutzenden Notizen),
`[[…]]`/`backlinks` live für `find_related` durchlaufen.

- Vorteile: kein Index-Ort-Problem, keine Staleness/Cache-Invalidierung,
  **stateless CLI** (passt perfekt zum Subprozess-Modell).
- `rebuild` entfällt aus dem Tool-Set (ohne Index nichts zu bauen).
- **Reversibler Notausgang:** Wird der Vault je groß genug, ist ein Cache eine
  *interne* CLI-Optimierung – ändert weder MCP-Vertrag noch Tool-Oberfläche.

### 7. Tool-Set

Alle Schreib-Tools verlangen die Rolle `read-write`; alle Pfade sind **strikt auf
den Vault-Root eingesperrt** (kein Zugriff außerhalb).

**Retrieval (read – für beide Rollen):**

| Tool | Tut |
|---|---|
| `search(query)` | Volltext über den Vault → Datei + Snippet + Frontmatter |
| `get_by_tag(tags)` | Notizen mit gegebenem Tag/Tags |
| `list_tags()` | Alle Tags nach Häufigkeit |
| `search_tags(pattern)` | Tag-Namen nach Muster |
| `get_by_type(type)` | Nach BRAID-Typ (`inbox`/`zettel`/`project`/`area`/`reference`) |
| `get_by_date_range(from,to)` | Nach `created`/`modified` |
| `read_note(slug\|path)` | Vollständiger Inhalt einer Notiz |
| `list(path?)` | Verzeichnis/Baum |
| `find_related(slug)` | Über `[[…]]`/`backlinks` verbundene Notizen (mehrere Hops) |

**Intent (write – nach mdex-Konvention; ≈ deckt das heutige `pkmt new` ab):**

| Tool | Tut |
|---|---|
| `capture_inbox(content)` | Neuer Inbox-Eintrag (`YYYYMMDD-NN.md`) |
| `create_note` / `create_project` / `create_area` / `create_reference` | Neue Notiz mit Frontmatter + Template |
| `append_to_note(slug, content)` | An eine Notiz anhängen |
| `append_to_log(project, content)` | An die dated Log-Sektion eines Projekts anhängen |
| `promote_inbox_item(slug, target_type)` | Inbox → zettel/project/… inkl. Frontmatter-Umbau |

> `append_to_log` muss eine bestimmte Log-Sektion finden – die Heading-Konvention
> aus `resources/templates/body-project-default.md` (`## Log` / `### YYYY-MM-DD`)
> ist damit Teil des Operations-/`--json`-Vertrags und beim Umsetzen zu fixieren.

**Raw FS (write – der „guarded filesystem"-Notausgang):**

| Tool | Tut |
|---|---|
| `write_file(path, content)` | Datei anlegen/überschreiben |
| `append_file(path, content)` | Rohes Anhängen |
| `move(from, to)` | Verschieben/Umbenennen |
| `mkdir(path)` | Ordner anlegen |
| `delete(path)` | Löschen (per Git-Historie revertierbar) |

**Bewusst gestrichen (YAGNI):** `rebuild` (kein Index), `summarize_session_to_mdex`
(Prompt-Verhalten, kein Tool), `update_project_status` (geht in `append_to_log`
auf).

### 8. Auth-Mechanik im laufenden System

Der MCP-Spec sieht für HTTP-Transport eigentlich **OAuth 2.1** vor (Resource-
Server, Discovery, PKCE, dynamische Client-Registrierung) – für einen
persönlichen Server überdimensioniert. Die genutzten Clients (Claude
Code/Desktop Custom Connector, Langdock) erlauben, beim Einrichten direkt einen
**Bearer-Token** zu hinterlegen → statischer Token genügt. Falls je ein Client
zwingend OAuth-Discovery verlangt, werden die Metadata-Endpoints nachgerüstet.

**Lebenslauf eines Requests:**

1. Client → HTTPS → **Reverse-Proxy** (TLS) → Kestrel/ASP.NET auf localhost.
2. **Auth-Middleware** liest `Authorization: Bearer <token>`, hasht, schlägt im
   Token-Store nach → Rolle, sonst `401`.
3. **Pro-Tool-Check:** Tool ist `read` oder `write` getaggt. Write-Tool +
   Rolle ≠ `read-write` → `403`.
4. Tool-Handler ruft `mdext … --json` als Subprozess → Ergebnis zurück.

Standard-ASP.NET-Core-Auth-Middleware vor dem MCP-Dispatch, nichts Exotisches.

**Token-Store & Verwaltung:**

- Store = serverseitige Config **außerhalb des Vault-Repos** (z.B.
  `~/.config/mdex-mcp/tokens.json`), enthält nur Hashes + Rolle + Label.
- Verwaltung über `mdex-mcpctl`:
  - `mdex-mcpctl token new --role read-write --label langdock` → zeigt Roh-Token
    **einmal**, legt Hash ab.
  - `mdex-mcpctl token list` → Labels/Rollen/Datum (nie der Roh-Token).
  - `mdex-mcpctl token revoke langdock` → Eintrag entfernen. Rotation = revoke + new.
- Server liest beim Start; nach Änderung Service-Reload/Restart (Hot-Reload
  optional).
- **Infisical** (auf dem Schirm) als späterer Upgrade-Pfad zum injizierten Store –
  heute YAGNI.

### 9. Ablage: eigenes Code-Repo

Der Vault-`main` ist ein Auto-Commit-Firehose – Code-Branches im selben Repo
wären ein ständiger Rebase-Kampf. Deshalb:

- **Code in eigenem Repo `mdex-tooling`** (.NET-Solution), zeigt per Config
  (`MDEX_ROOT`) auf den Vault. Vault bleibt reiner Content.
- Das **BRAID-Vorhaben** lebt im Vault (`projects/mdex-tooling-konzept/`); der
  Code ist sein **Deliver** (Fundort = Repo-URL) – saubere BRAID-Selbstanwendung.
- `mdext` löst die **Content-Befehle** des Bash-`pkmt` (`new`) ab; bis dahin
  bleibt Bash-`pkmt` im Vault unangetastet (keine Big-Bang-Migration). `pkmt sync`
  bleibt Bash und außerhalb des Scopes (siehe §5).

```
mdex-tooling/             (eigenes Repo)
  Mdex.sln
  src/
    Mdex.Cli/        → `mdext`        vault-only Terminal-Tool (human + --json)
    Mdex.Mcp/        → `mdex-mcp`     MCP-Server: ASP.NET Core + MCP-C#-SDK, Remote-HTTP
    Mdex.Mcpctl/     → `mdex-mcpctl`  Token new/list/revoke, gehashter Token-Store
  tests/  ·  README.md
```

### 10. Deployment

- ASP.NET Core / Kestrel als systemd-Service auf dem Homeserver, hinter dem
  bestehenden Reverse-Proxy (TLS-Terminierung dort).
- `MDEX_ROOT` zeigt auf die Server-Working-Copy des Vaults
  (`/home/mdadmin/repos/mdex`).
- **Koexistenz mit Auto-Commit:** Der MCP schreibt nur Dateien; `mdex-autocommit.sh`
  committet/pusht sie wie jede andere Quelle (siehe §3). Kein Lock, kein
  zweiter Git-Akteur. Schreibt der MCP, während der Watcher gerade
  `git pull --rebase` ausführt, ist das unkritisch: die Datei wird vom nächsten
  Debounce-Zyklus erfasst – **bewusst kein Locking** bei dieser Schreibfrequenz.
- Single-File-Publish; Update = Binary tauschen + Service-Restart.

### 11. Distribution & Nutzung

Anders als das alte Bash-`pkmt`, das **im** Vault lag und `PKMT_ROOT` aus seinem
Skriptpfad ableitete, sind Tool und Daten jetzt **entkoppelt**: die Binaries
werden einmal installiert und per `MDEX_ROOT` auf den Vault gezeigt (wie `git`
nicht im Repo liegt).

- **Keine Binaries im Vault.** Sie würden die Git-History aufblähen (jeder
  Rebuild = neuer Blob, `git add -A` committet mit), wären plattform-spezifisch
  und liefen via Syncthing aufs Handy. Der Vault bleibt reiner Content.
- **Desktop (Windows):** `mdext` (+ ggf. `mdex-mcpctl`) als **.NET Global Tool**
  installieren (`dotnet tool install --global …`, Update via `dotnet tool
  update`) – global auf dem `PATH`, kein `cd` in den Vault mehr nötig.
  Alternative ohne installiertes .NET: Single-File-Publish auf den `PATH`. Der
  MCP-Server wird lokal **nicht** gebraucht.
- **Homeserver:** `dotnet publish` der Solution → `mdex-mcp` (systemd-Service),
  `mdext` (vom Server per Subprozess aufgerufen) und `mdex-mcpctl` (Admin)
  shippen aus einem Repo zusammen.
- **Vault-Auflösung** (Reihenfolge): `--root`-Flag > `MDEX_ROOT`-Env-Var >
  Config-Datei (`~/.config/mdex/` bzw. `%APPDATA%\mdex\`).
- **Konsequenz:** Ein Vault-`git clone` allein genügt nicht mehr – bewusst zwei
  Schritte: (1) Vault clonen (Content), (2) Tool einmal installieren +
  `MDEX_ROOT` setzen.

---

## Designprinzipien (Leitplanken für die Umsetzung)

1. **Smart grep, not a brain** – locate-and-quote, Laufzeit-Auflösung, kein RAG.
2. **Nichts geht verloren** – Git-Historie deckt jede Mutation ab; der
   `read`-only-Key schützt vor schwachen Modellen.
3. **Ein Tool, ein Zweck** – `mdext` ist vault-only und das Fundament; `mdex-mcp`
   und `mdex-mcpctl` sind dünne, klar abgegrenzte Aufrufer.
4. **Die KI spricht mdex-Begriffe** – Intent-Layer versteckt Pfad-/Namenslogik;
   Raw FS ist der bewusste Notausgang.
5. **YAGNI** – kein Index, kein OAuth, kein Ordner-Scoping, kein Commit-pro-Op.
   Jeder dieser Verzichte hat einen dokumentierten, reversiblen Nachrüst-Pfad.

---

## Offene Punkte / spätere Schritte

- Index-Cache, falls der Vault Richtung 1000+ Notizen wächst (interne
  CLI-Optimierung).
- OAuth-Metadata-Endpoints, falls je ein Client sie zwingend verlangt.
- Infisical als injizierter Token-Store statt Datei.
- Explizites `sync`-Tool / kürzerer Debounce, falls Git-Propagationslatenz stört.
- **Bootstrapper / „neue-Maschine-Setup"** (`mdex init`-artig): installiert
  `mdext`, klont den Vault, setzt `MDEX_ROOT`, prüft Voraussetzungen. Bewusst
  Step 2 — setzt stabile Binaries + `--json`-Vertrag voraus; die Architektur
  (Tool↔Vault entkoppelt, Auflösungsreihenfolge `--root > MDEX_ROOT > Config`)
  ist schon darauf ausgelegt. Betrifft die *eigenen Arbeitsmaschinen*, nicht die
  MCP-Clients (die brauchen nur URL + Bearer-Token).
- Ablöse-Zeitpunkt für das Bash-`pkmt` (`new`-Befehle).
- Ob `pkmt sync` später in ein dediziertes Infra-/Admin-Tool umzieht (heute Bash,
  außerhalb des Scopes).
