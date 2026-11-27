# Win-ProfileKit – Spezifikation

Stand: 2026-11-27 · Status: verbindliche Grundlage für die Umsetzung

## 1. Zweck

Win-ProfileKit erfasst die Einstellungen installierter Programme und die Windows-
Einstellungen eines **Referenzbenutzers**, speichert sie als wiederverwendbare
**Module** und **Profile** und wendet sie auf **beliebige Benutzer** an – auf dem
selben oder einem anderen Rechner, interaktiv oder unbeaufsichtigt im OEM-Skriptlauf.

Hauptanwendungsfall des Autors: Auf Installations-VMs (Hyper-V) werden Testbenutzer
angelegt, und jeder bekommt ein eigenes, **bereits konfiguriertes** Software-Set.

Unterschied zu PC-Umzugstools (PCmover, USMT u. a.): Welche Dateien und Registry-
Schlüssel zu einem Programm gehören, steht nicht in einer festen Tabelle, sondern wird
**dynamisch ermittelt** – durch einen KI-Agenten, durch einen Lernmodus (Vorher/Nachher-
Vergleich) und durch einen mitgelieferten, wachsenden Regelkatalog.

## 2. Rahmenbedingungen

| Punkt | Festlegung |
|---|---|
| Einsatz | rein privat, Einzelplatz und eigene VMs |
| Betriebssystem | ausschließlich Windows 11, x64 |
| Virtualisierung | Hyper-V (VHDX) |
| Software-Quelle | winget (kleine Tools, alle per winget installierbar) |
| Benutzer | Wechsel Benutzer → Benutzer ist der Normalfall; Zielbenutzer existiert meist schon, kann optional angelegt werden |
| Lizenzen | werden neu eingegeben. **Ausnahme:** Keyfiles im Konfigurations- oder Programmordner (z. B. WinRAR `rarreg.key`) werden mitkopiert |
| Passwörter / DPAPI-Daten | werden nicht migriert (an Benutzer und Maschine gebunden) |
| Transport | Exportordner/-datei, lokal oder auf Netzwerkfreigabe |
| Sprache | Oberfläche und CLI Deutsch/Englisch, umschaltbar; Standard = Systemsprache |
| Portable Programme | nicht im Umfang |

## 3. Begriffe und Datenmodell

```
Regel (Rule)        Was gehört zu Programm X?  → Dateien, Registry, Keyfiles, Ausschlüsse
Modul (Module)      Regel + erfasste Daten eines Programms bzw. eines Windows-Bereichs
Profil (Profile)    Benannte Auswahl von Modulen, z. B. "Testuser-Dev"
Anwenden (Apply)    Profil → Zielbenutzer (ggf. vorher winget-Installation)
```

### 3.1 Pfad-Token

Pfade werden **nie absolut** gespeichert, sondern mit Token, damit sie bei jedem
Benutzer und Rechner passen:

| Token | Beispiel (Benutzer `max`) |
|---|---|
| `%USERPROFILE%` | `C:\Users\max` |
| `%APPDATA%` | `C:\Users\max\AppData\Roaming` |
| `%LOCALAPPDATA%` | `C:\Users\max\AppData\Local` |
| `%LOCALAPPDATALOW%` | `C:\Users\max\AppData\LocalLow` |
| `%PROGRAMDATA%` | `C:\ProgramData` |
| `%PROGRAMFILES%` | `C:\Program Files` |
| `%PROGRAMFILES(X86)%` | `C:\Program Files (x86)` |

Ordnerumleitung (z. B. Dokumente auf OneDrive) wird in Version 1 nicht aufgelöst.

### 3.2 Inhalte umschreiben

Konfigurationsdateien und Registry-Strings enthalten oft absolute Pfade des alten
Benutzers. Beim Anwenden ersetzt der Core den Profilpfad der Quelle durch den des Ziels,
in allen üblichen Schreibweisen:

* `C:\Users\alt` · `C:\\Users\\alt` (JSON, escaped) · `C:/Users/alt` · `file:///C:/Users/alt`
* nur an Pfadgrenzen (`C:\Users\alt2` wird **nicht** getroffen), Groß-/Kleinschreibung egal
* Kodierung bleibt erhalten (UTF-8 mit/ohne BOM, UTF-16 LE/BE, sonst byte-treu)
* Binärdateien (NUL-Bytes) werden nicht verändert

### 3.3 Paketformat

Ein Profil ist ein **Ordner** (gut auf Freigaben, diff-bar) und optional eine
**ZIP-Datei** `<Name>.pkprofile` mit identischem Inhalt.

```
Testuser-Dev/
├─ profile.json                    Manifest (Schema-Version, Name, Quelle, Modulliste)
├─ modules/
│  ├─ notepadplusplus/
│  │  ├─ module.json               Regel inkl. winget-ID
│  │  ├─ registry.json             erfasste Registry-Werte
│  │  └─ files/APPDATA/Notepad++/config.xml
│  └─ windows.mouse/ …
└─ LIESMICH.txt / README.txt
```

`profile.json` enthält u. a. `schemaVersion`, `name`, `createdUtc`, `toolVersion`,
`source` (Rechner, Benutzer, SID, Profilpfad) und `modules[]`.
Registry-Schlüssel werden mit Wurzel `HKCU\…` bzw. `HKLM\…` gespeichert; `HKCU`
bedeutet immer „Hive des Zielbenutzers“.

Keyfiles sind in der Regel als `kind: "keyfile"` markiert. Die Oberfläche weist darauf
hin, dass das Profil damit Lizenzdaten enthält. Eine Verschlüsselung des Pakets ist
für Version 1 nicht vorgesehen (privater Einsatz), das Format lässt sie später zu.

## 4. Funktionen

### 4.1 Inventar
* Installierte Software über `winget export --include-versions` (JSON, gleiches Format,
  das der OEM-Skriptgenerator importiert).
* Ergänzend Uninstall-Schlüssel (HKLM/HKCU) für Programme ohne winget-Zuordnung.
* Abgleich mit dem Regelkatalog: Für welche Programme gibt es schon Regeln?

### 4.2 Erfassen (Capture)
* Quelle wählen (siehe 4.6), Programme und Windows-Bereiche auswählen.
* Dateien und Registry gemäß Regel einsammeln, Ausschlüsse (Caches, Logs, Sperrdateien)
  beachten, Größe anzeigen.
* Ergebnis: Module, zu einem Profil zusammengestellt.

### 4.3 Anwenden (Apply)
* Ziel: **aktueller Benutzer**, **anderer vorhandener Benutzer** (Offline-Hive),
  **Default-Profil** (gilt für alle künftig angelegten Benutzer) oder **neuer Benutzer**
  (wird lokal angelegt, Profil wird sofort erzeugt).
* Plan vor Ausführung: je Modul *installieren + konfigurieren*, *nur konfigurieren*
  (bereits installiert) oder *nicht installierbar*.
* Vor dem Überschreiben wird ein Backup im Zielbenutzer angelegt
  (`%LOCALAPPDATA%\Win-ProfileKit\Backups\<Zeitstempel>`).
* Trockenlauf (`--dry-run`) zeigt alle Änderungen ohne sie auszuführen.
* Nach dem Anwenden für den angemeldeten Benutzer: Einstellungen live aktivieren
  (`SystemParametersInfo`, `WM_SETTINGCHANGE`), wo möglich.

### 4.4 Benutzer anlegen
* Lokales Konto anlegen (Name, Kennwort, optional Administrator).
* Profil (Ordner + `NTUSER.DAT`) sofort aus dem Default-Profil erzeugen, damit das
  Profil ohne vorherige Anmeldung angewendet werden kann.

### 4.5 Windows-Einstellungen
Als eigene Module im Katalog (`rules/windows/`), z. B.:

| Modul | Inhalt |
|---|---|
| `windows.theme` | Hell/Dunkel für Apps und System, Akzentfarbe |
| `windows.mouse` | Zeigergeschwindigkeit, Beschleunigung, Doppelklick |
| `windows.clipboard` | Zwischenablageverlauf (Win+V) |
| `windows.explorer` | Dateiendungen, versteckte Dateien, Startordner |
| `windows.taskbar` | Ausrichtung, Suche, Widgets |

Startmenü-Layout und Taskleisten-Pins (Binärblobs) sind ausdrücklich **nicht** in
Version 1. Live-Aktivierung über `postApply`-Hinweise der Module.

### 4.6 Quellen
1. **Lokal** – Tool läuft auf dem Quellrechner.
2. **Sammler über Freigabe** – `profilekit.exe collect` wird auf dem Quellrechner direkt
   von der Freigabe gestartet (keine Installation) und schreibt Inventar, Registry-Auszug
   und Dateien in die Freigabe. Am neuen Rechner: Abgleich mit dessen Inventar,
   fehlende Programme anbieten, dann Einstellungen anwenden.
3. **Offline-Datenträger** – VHDX einer heruntergefahrenen Hyper-V-VM wird
   schreibgeschützt eingebunden; Hives (`NTUSER.DAT`, `SOFTWARE`) werden offline geladen.
4. **Reine Freigabe** (`\\rechner\C$`) – nur Dateien; Registry nur, wenn der Benutzer
   dort abgemeldet ist. Installierte Programme werden aus Programmordnern,
   Startmenü und AppData abgeleitet (KI-gestützt).

### 4.7 Regelermittlung
Drei Wege, alle erzeugen dasselbe Regelformat (`module.json`):

1. **Katalog** – mitgelieferte Regeln in `rules/`, per winget-ID zugeordnet.
2. **Lernmodus** – Momentaufnahme von HKCU und den AppData-Ordnern, Benutzer ändert
   Einstellungen, zweite Momentaufnahme, Differenz → Regelvorschlag.
   * Beobachtet: `%APPDATA%`, `%LOCALAPPDATA%`, `%LOCALAPPDATALOW%`, Dateien und Punkt-Ordner
     direkt in `%USERPROFILE%`, `HKCU\Software`, `HKCU\Control Panel`; mit `--machine` zusätzlich
     Programmordner, `%PROGRAMDATA%` und `HKLM\SOFTWARE`.
   * Der Snapshot speichert nur Größe/Zeitstempel und einen Hash der Registry-Daten, keine Inhalte.
   * Bekanntes Rauschen (Temp, Caches, Logs, UserAssist, MRU-Listen, CloudStore …) wird ignoriert;
     eigene Ausschlüsse mit `--exclude`.
   * Vorschlag: geänderte Dateien werden je Programm zum gemeinsamen Ordner zusammengefasst
     (mindestens Hersteller-Ebene); Herstellerschlüssel werden als ganzer Schlüssel übernommen,
     gemeinsam genutzte Windows-Schlüssel (`Control Panel`, `Software\Microsoft\Windows` …) nur mit
     den geänderten Werten. Gelöschte Einträge erzeugen keine Regel.
   * Die Regel landet in `%LOCALAPPDATA%\Win-ProfileKit\rules` und ist sofort Teil des Katalogs.
3. **KI-Agent** – sucht Speicherorte, liest bei Bedarf Dateiinhalte, schlägt Regel vor.

**Grundsatz: Die KI schlägt vor, der Core prüft und führt aus.** Jeder Vorschlag wird
validiert (Pfade existieren, Größe, Ausschluss von Caches) und vom Benutzer bestätigt.
Bestätigte Regeln landen im lokalen Regelordner und können in den Katalog übernommen
werden.

### 4.8 KI-Anbindung (Reihenfolge der Stufen)

| Stufe | Wer steuert | Anbindung |
|---|---|---|
| 1a Desktop-App (Claude Desktop, ChatGPT, Microsoft Copilot u. a.) | die KI | Win-ProfileKit stellt einen **MCP-Server** (stdio) bereit und trägt sich nach Zustimmung in der App ein |
| 1b Agent über die Kommandozeile (Claude Code, Codex CLI, Copilot CLI) | Win-ProfileKit | Headless-Aufruf mit unserem MCP-Server |
| 2 Ollama | Win-ProfileKit | eigener Agenten-Ablauf mit Tool-Calling, `http://localhost:11434` |
| 3 OpenRouter | Win-ProfileKit | eigener Agenten-Ablauf, Modellliste live, kein Modell fest verdrahtet |

* Beim Start werden installierte KI-Anwendungen erkannt und die beste Stufe vorgeschlagen.
* Pfade und Dateiinhalte dürfen an die KI gehen. Bei OpenRouter steht in den
  Einstellungen und vor dem ersten Lauf deutlich, dass die Daten an OpenRouter und den
  Modellanbieter übertragen werden.
* API-Keys liegen in der Windows-Anmeldeinformationsverwaltung, nie im Klartext.

**MCP-Werkzeuge** (gemeinsam für alle Stufen, die Sicherheitsgrenze ist technisch):

| Werkzeug | Zweck |
|---|---|
| `list_installed_apps` | Inventar |
| `search_paths` | Suche nur in Benutzerprofil, ProgramData, Programmordnern und HKCU/HKLM\SOFTWARE |
| `inspect_file` | Metadaten, nach Bedarf Inhalt (Größenlimit) |
| `inspect_registry` | Werte eines Schlüssels |
| `get_rule` / `propose_rule` | vorhandene Regel lesen, neue Regel vorschlagen |

Der Agent kann keine Dateien schreiben, löschen oder Programme starten.

### 4.9 Integration in den OEM-Skriptgenerator
Repository: `dmatrix68-boop/OEM-script-gen`.

* Neuer Importtyp „Win-ProfileKit-Profil“ (Ordner oder `.pkprofile`). Die winget-IDs des
  Profils werden in die Paketliste übernommen.
* Ablage im Medium: `$OEM$\$1\Install\ProfileKit\profilekit.exe` und
  `$OEM$\$1\Install\ProfileKit\Profiles\<Profil>\`.
* **Phase 1** (SYSTEM): Windows-Module ins Default-Profil
  (`profilekit apply --default-user --only windows.*`).
* **Phase 2** (Benutzerkontext, nach `Install-Apps.ps1`): neues Modul
  `Restore-Profile.ps1` ruft `profilekit apply --profile <Pfad> --current-user` auf.
* Erledigt-Marker unter `HKLM\SOFTWARE\OEM-Setup`, wie bei den übrigen Phasen.
* Die CLI ist eine **self-contained Single-File-EXE** (keine .NET-Runtime auf dem
  frisch installierten System nötig) und arbeitet ohne Oberfläche und ohne Rückfragen.
  Exitcodes: `0` OK, `1` Fehler, `2` Teilerfolg, `3` ungültige Argumente.

## 5. Architektur

```
src/ProfileKit.Core      plattformneutral: Modell, Pfad-Token, Umschreiben, Paketformat,
                         Capture/Apply-Logik, Planung, winget-Export-Parser, Katalog
src/ProfileKit.Windows   Registry, Offline-Hives (RegLoadKey), Benutzerprofile,
                         Benutzer anlegen, winget-Aufrufe, Live-Aktivierung
src/ProfileKit.Cli       profilekit.exe – für OEM-Lauf, Skripte und Tests
src/ProfileKit.Mcp       MCP-Server (Phase 3)
src/ProfileKit.App       WinUI 3, unpackaged + self-contained (Phase 4)
rules/                   Regelkatalog (JSON)
```

* .NET 10 (LTS), C#, `Nullable` an, Warnungen sind Fehler.
* Core hat keine Windows-Abhängigkeit und ist vollständig per Unit-Test abgedeckt.
  Registry-Zugriffe laufen über `IRegistryBackend` (Windows-Implementierung und
  In-Memory-Implementierung für Tests).
* Build und Tests in GitHub Actions auf `windows-latest`.

## 6. CLI (Version 1)

```
profilekit users                                   lokale Benutzerprofile anzeigen
profilekit inventory [--out datei.json]            installierte Software + passende Regeln
profilekit catalog                                 Regelkatalog anzeigen
profilekit capture  --out <Ordner> --name <Name> (--modules a,b | --installed)
                    [--user <Name>]
profilekit info     --profile <Ordner>
profilekit apply    --profile <Ordner>
                    (--current-user | --user <Name> | --default-user | --create-user <Name>)
                    [--install] [--only <muster>] [--dry-run] [--no-backup]
profilekit user create <Name> [--admin] [--password-stdin]
profilekit learn start [--machine] [--exclude <Muster;Muster>]
profilekit learn diff
profilekit learn stop --id <modul-id> [--name <Programm>] [--winget <id>] [--out <Datei>] [--force]
Global: --lang de|en, --rules <Ordner>, --verbose
```

## 7. Umsetzungsphasen

| Phase | Inhalt |
|---|---|
| **1** | Core, Windows-Schicht, CLI: Katalog, Inventar, Capture, Apply (alle Zielarten), Benutzer anlegen, Windows-Module, CI |
| **2** | Lernmodus ✅, Sammler über Freigabe, Offline-VHDX, Abgleich Quelle/Ziel |
| **3** | MCP-Server, KI-Stufen 1a/1b/2/3, Regelvorschläge mit Validierung |
| **4** | WinUI-3-Oberfläche (DE/EN) |
| **5** | Integration in den OEM-Skriptgenerator (eigener PR dort) |

## 8. Bewusst ausgeschlossen (Version 1)

Passwörter und DPAPI-geschützte Daten, Lizenzaktivierungen (außer Keyfiles),
Startmenü-Layout und Taskleisten-Pins, Ordnerumleitung, portable Programme,
Domäne/Entra ID, direkte Übertragung von Rechner zu Rechner, Verschlüsselung.
