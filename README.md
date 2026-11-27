# Win-ProfileKit

*Deutsch · [English below](#english)*

Erfasst die Einstellungen installierter Programme und die Windows-Einstellungen eines
Referenzbenutzers als wiederverwendbare **Profile** und wendet sie auf **beliebige
Benutzer** an – inklusive winget-Installation fehlender Programme. Gedacht für
Testbenutzer auf Installations-VMs und für den OEM-Skriptlauf
([OEM-script-gen](https://github.com/dmatrix68-boop/OEM-script-gen)).

> Status: Phase 1 (Core + CLI). KI-Regelermittlung, Lernmodus und WinUI-Oberfläche
> folgen – siehe [SPEC.md](SPEC.md).

## Funktionen (Phase 1)

* **Regelkatalog** (`rules/`): je Programm, welche Dateien und Registry-Schlüssel dazugehören –
  aktuell 16 Programme (u. a. PowerToys, Windows Terminal, Total Commander und WinRAR inkl.
  Keyfile, Everything, XnView MP, VLC, HandBrake, Ditto, RustDesk, UniGetUI) und Windows-Module
  für Maus, Design, Zwischenablageverlauf, Explorer und Taskleiste. Noch offene Programme:
  [rules/KANDIDATEN.md](rules/KANDIDATEN.md).
* **Erfassen** vom aktuellen oder einem anderen Benutzer; Pfade werden neutral gespeichert
  (`%APPDATA%\…`), Caches und Logs ausgeschlossen.
* **Anwenden** auf den aktuellen Benutzer, einen anderen Benutzer (auch abgemeldet – der Hive
  wird offline geladen), das Default-Profil oder einen **neu angelegten** Benutzer.
  Profilpfade in Dateien und Registry-Werten werden auf den Zielbenutzer umgeschrieben.
* **winget**: fehlende Programme vor dem Konfigurieren installieren (`--install`).
* **Sicherung** vorhandener Dateien und Werte vor dem Überschreiben, **Trockenlauf** mit `--dry-run`.
* Zweisprachig (DE/EN), Profil als Ordner oder `.pkprofile`-Archiv.

## Beispiele

```bat
:: Profil vom aktuellen Benutzer erfassen
profilekit capture --out \\nas\profile\Dev --name Dev --modules notepadplusplus,7zip,windows.*

:: alle installierten Programme mit Regel erfassen, zusätzlich als Archiv
profilekit capture --out D:\Profile\Alles --installed --modules windows.* --zip

:: auf einen neuen Testbenutzer anwenden, fehlende Programme installieren (als Administrator)
profilekit apply --profile \\nas\profile\Dev --create-user test1 --admin --install

:: im OEM-Lauf (Phase 2, Benutzerkontext)
profilekit apply --profile C:\Install\ProfileKit\Profiles\Dev --current-user --install
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
