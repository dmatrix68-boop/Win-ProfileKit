# Regel-Kandidaten

Programme aus dem Standard-Set (`MusterPC2.oemproj.json`), für die es **noch keine**
Katalogregel gibt. Die Regeln im Katalog enthalten nur Speicherorte, die gesichert bekannt
sind. Alles hier wird mit Lernmodus bzw. KI-Agent (Phase 2/3) ermittelt und dann als Regel
übernommen.

## Zu ermitteln (Lernmodus / KI)

| Programm | winget-ID | Vermutung / offene Frage |
|---|---|---|
| WizTree | `AntibodySoftware.WizTree` | `WizTree3.ini` neben der EXE oder in `%APPDATA%\WizTree` |
| FastStone Image Viewer | `FastStone.Viewer` | `FSSettings.db` unter `%LOCALAPPDATA%\FastStone\FSIV`? |
| AIDA64 Extreme | `FinalWire.AIDA64.Extreme` | `aida64.ini` im Programmordner; Lizenz dort oder in der Registry? |
| FinePrint | `FineprintSoftware.FinePrint` | Registry unter `HKCU\Software\FinePrint Software`; Lizenz maschinenweit |
| DeepL | `DeepL.DeepL` | Einstellungen teils im Konto; lokaler Anteil in `%APPDATA%`/`%LOCALAPPDATA%` unklar |
| Link Shell Extension | `HermannSchinagl.LinkShellExtension` | Optionen in HKCU oder HKLM? |
| Registry Finder | `SergeyFilippov.RegistryFinder` | Registry oder INI? |
| AnyBurn | `PowerSoftware.AnyBurn` | Registry oder INI? |
| ExplorerTabUtility | `w4po.ExplorerTabUtility` | JSON in `%APPDATA%` oder neben der EXE? |
| MediaInfo | `MediaArea.MediaInfo.GUI` | `HKCU\Software\MediaArea`? |
| AusweisApp | `Governikus.AusweisApp` | Qt-Einstellungen in HKCU – genauer Schlüssel offen |
| Cinema 4D 2025 | `Maxon.Cinema4D.2025` | `%APPDATA%\Maxon\Maxon Cinema 4D 2025_<Hash>` – braucht Platzhalter für den Hash im Regelformat |
| Hasleo Backup Suite | `Hasleo.BackupSuiteFree` | Sicherungsaufträge maschinenweit; sinnvoll? |

## Bewusst ohne Regel

| Programm | Grund |
|---|---|
| App Installer, .NET Desktop/Native Runtime, Microsoft.UI.Xaml, Windows App SDK | Laufzeitumgebungen ohne Einstellungen |
| Outlook für Windows, Teams | Einstellungen liegen im Microsoft-Konto |
| Quick Share, Bing Wallpaper, Modern Winver, LockHunter, ForensiT Transwiz | keine nennenswerten Einstellungen |
