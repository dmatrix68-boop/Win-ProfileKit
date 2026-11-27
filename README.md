# Win-ProfileKit

*Deutsch · [English below](#english)*

Erfasst die Einstellungen installierter Programme und die Windows-Einstellungen eines
Referenzbenutzers als wiederverwendbare **Profile** und wendet sie auf **beliebige
Benutzer** an – inklusive winget-Installation fehlender Programme. Gedacht für
Testbenutzer auf Installations-VMs und für den OEM-Skriptlauf
([OEM-script-gen](https://github.com/dmatrix68-boop/OEM-script-gen)).

> Status: Core + CLI, Lernmodus, MCP-Server, KI-Anbindung und WinUI-3-Oberfläche.
> Einbindung in den OEM-Skriptgenerator folgt – siehe [SPEC.md](SPEC.md).

## Funktionen (Phase 1)

* **Regelkatalog** (`rules/`): je Programm, welche Dateien und Registry-Schlüssel dazugehören –
  aktuell 29 Programme (u. a. PowerToys, Windows Terminal, Total Commander und WinRAR inkl.
  Keyfile, Everything, XnView MP, VLC, HandBrake, Ditto, RustDesk, UniGetUI) und Windows-Module
  für Maus, Design, Zwischenablageverlauf, Explorer und Taskleiste. Noch offene Programme:
  [rules/KANDIDATEN.md](rules/KANDIDATEN.md).
* **Erfassen** vom aktuellen oder einem anderen Benutzer; Pfade werden neutral gespeichert
  (`%APPDATA%\…`), Caches und Logs ausgeschlossen.
* **Anwenden** auf den aktuellen Benutzer, einen anderen Benutzer (auch abgemeldet – der Hive
  wird offline geladen), das Default-Profil oder einen **neu angelegten** Benutzer.
  Profilpfade in Dateien und Registry-Werten werden auf den Zielbenutzer umgeschrieben.
* **Lernmodus**: `learn start` → Einstellungen im Programm ändern → `learn stop --id <modul>`
  erzeugt aus dem Vorher/Nachher-Vergleich eine Regel (Rauschen wie Caches, Logs und MRU-Listen
  wird herausgefiltert).
* **KI-Suche**: `ai discover --app <Programm>` lässt eine KI die Speicherorte ermitteln – über
  Claude Code, eine lokale Ollama-Installation oder OpenRouter. Über `mcp install` steht dieselbe
  Funktion direkt in Claude Desktop zur Verfügung. Die KI kann nur lesen, Zugangsdaten und
  Lizenzdateien bekommt sie nie zu sehen; ihre Ergebnisse sind Vorschläge, die du mit
  `ai accept` übernimmst.
* **winget**: fehlende Programme vor dem Konfigurieren installieren (`--install`).
* **Sicherung** vorhandener Dateien und Werte vor dem Überschreiben, **Trockenlauf** mit `--dry-run`.
* Zweisprachig (DE/EN), Profil als Ordner oder `.pkprofile`-Archiv.

## Oberfläche

`profilekit-gui.exe` (WinUI 3) bietet dieselben Funktionen mit Fenstern: Profil erfassen
(Programme ankreuzen, „Installierte markieren“), Profil anwenden (aktueller, anderer, neuer Benutzer
oder Default-Profil, mit Trockenlauf), Lernmodus, KI-Suche mit Stapelmodus und Vorschlagsliste sowie
die Einstellungen für KI-Stufen, OpenRouter-Key und Claude Desktop. Alle Aktionen laufen über
`profilekit.exe`, deren Ausgabe im Protokoll unten erscheint. Ohne Administratorrechte bietet die
Oberfläche einen Neustart als Administrator an.

Download: Artefakt `profilekit-gui-win-x64` der CI (Ordner entpacken, `profilekit-gui.exe` starten –
keine Installation nötig).

## Beispiele

```bat
:: Profil vom aktuellen Benutzer erfassen
profilekit capture --out \\nas\profile\Dev --name Dev --modules notepadplusplus,7zip,windows.*

:: alle installierten Programme mit Regel erfassen, zusätzlich als Archiv
profilekit capture --out D:\Profile\Alles --installed --modules windows.* --zip

:: auf einen neuen Testbenutzer anwenden, fehlende Programme installieren (als Administrator)
profilekit apply --profile \\nas\profile\Dev --create-user test1 --admin --install

:: Umzug über eine Netzwerkfreigabe – auf dem alten Rechner (direkt von der Freigabe gestartet):
\\nas\tools\profilekit.exe collect --out \\nas\profile
:: … auf dem neuen Rechner: abgleichen, dann Fehlendes installieren oder nur Vorhandenes konfigurieren
profilekit compare --profile \\nas\profile\ALT-PC_max
profilekit apply --profile \\nas\profile\ALT-PC_max --current-user --install
profilekit apply --profile \\nas\profile\ALT-PC_max --current-user --installed-only

:: aus der VHDX einer heruntergefahrenen Hyper-V-VM (als Administrator)
profilekit vhdx users --vhdx "D:\Hyper-V\Test-VM\Virtual Hard Disks\Test-VM.vhdx"
profilekit collect --vhdx "D:\Hyper-V\Test-VM\Virtual Hard Disks\Test-VM.vhdx" --user test1 --out \\nas\profile

:: im OEM-Lauf (Phase 2, Benutzerkontext)
profilekit apply --profile C:\Install\ProfileKit\Profiles\Dev --current-user --install
```

Regel für ein neues Programm lernen:

```bat
profilekit learn start
:: … Programm öffnen, Einstellungen ändern, Programm schließen …
profilekit learn stop --id wiztree --name WizTree --winget AntibodySoftware.WizTree
```

Regel per KI ermitteln:

```bat
profilekit ai status                              :: was ist installiert, welche Stufe wird genutzt?
profilekit ai discover --app WizTree --winget AntibodySoftware.WizTree
profilekit ai accept wiztree

:: alle Programme eines OEM-Projekts ohne Regel nacheinander untersuchen
profilekit ai discover --from-oemproj MusterPC2.oemproj.json --dry-run
profilekit ai discover --from-oemproj MusterPC2.oemproj.json

:: lokal mit Ollama
profilekit ai discover --app WizTree --provider ollama --model qwen3

:: in Claude Desktop nutzen
profilekit mcp install
```

Alle Befehle: `profilekit help`.

## Bauen

.NET 10 SDK, Windows 11 x64:

```bat
dotnet build ProfileKit.sln -c Release
dotnet test tests\ProfileKit.Core.Tests
dotnet publish src\ProfileKit.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts\profilekit
```

Die CI baut auf `windows-latest`, führt die Unit-Tests und einen Integrationstest
(`tools/smoke-test.ps1`: Benutzer anlegen, Offline-Hive, Default-Profil) aus und stellt
`profilekit.exe` als Artefakt bereit.

## Grenzen

Passwörter, DPAPI-geschützte Daten und Lizenzaktivierungen werden nicht übertragen
(Ausnahme: Keyfiles wie `rarreg.key`). Startmenü-Layout und Taskleisten-Pins sind nicht
enthalten. Details: [SPEC.md](SPEC.md), Abschnitt 8.

---

## English

Win-ProfileKit captures app settings and Windows preferences of a reference user into
reusable **profiles** and applies them to **any user** – current, another (offline hive),
the Default profile or a newly created one – with winget installs for missing programs and
rewriting of profile paths inside files and registry values. Built for test users on
installation VMs and for unattended OEM setup runs.

Run `profilekit help --lang en` for all commands. Requirements and roadmap: [SPEC.md](SPEC.md)
(German).

## Lizenz / License

Apache-2.0
