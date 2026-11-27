# Regel-Kandidaten

Programme aus dem Standard-Set (`MusterPC2.oemproj.json`), für die es **noch keine**
Katalogregel gibt. Die Regeln im Katalog enthalten nur Speicherorte, die gesichert bekannt
sind. Alles hier wird mit Lernmodus bzw. KI-Agent (Phase 2/3) ermittelt und dann als Regel
übernommen.

## Zu ermitteln (Lernmodus / KI)

| Programm | winget-ID | Vermutung / offene Frage |
|---|---|---|
| – | | Alle Programme aus dem Standard-Set haben eine Regel. |

## Bewusst ohne Regel

| Programm | Grund |
|---|---|
| App Installer, .NET Desktop/Native Runtime, Microsoft.UI.Xaml, Windows App SDK | Laufzeitumgebungen ohne Einstellungen |
| Outlook für Windows, Teams | Einstellungen liegen im Microsoft-Konto |
| Quick Share, Bing Wallpaper, Modern Winver, LockHunter, ForensiT Transwiz | keine nennenswerten Einstellungen |
