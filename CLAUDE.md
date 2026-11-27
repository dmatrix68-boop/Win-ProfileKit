# Projekt-Notizen für Claude

## Zusammenarbeit

- **Anrede: Du.** Wir duzen uns – auch in Antworten, Rückfragen und Zusammenfassungen.
- Antworten auf Deutsch, sofern nicht anders gewünscht.
- Verbindliche Anforderungen stehen in `SPEC.md`. Weicht eine Änderung davon ab,
  zuerst `SPEC.md` anpassen und die Abweichung im PR begründen.

## Projekt

Win-ProfileKit erfasst App- und Windows-Einstellungen eines Referenzbenutzers als
Module/Profile und wendet sie auf beliebige Benutzer an (Windows 11 x64, winget,
OEM-Skriptlauf). Verwandtes Repo: `dmatrix68-boop/OEM-script-gen`.

## Aufbau

| Pfad | Inhalt |
|---|---|
| `src/ProfileKit.Core` | plattformneutrale Logik – **keine** Windows-APIs hier |
| `src/ProfileKit.Windows` | Registry, Offline-Hives, Benutzer, winget, P/Invoke |
| `src/ProfileKit.Cli` | `profilekit.exe` |
| `tests/ProfileKit.Core.Tests` | xUnit-Tests für Core |
| `rules/apps`, `rules/windows` | Regelkatalog (JSON, eine Datei je Modul) |

## Bauen und testen

Ziel ist .NET 10. Die Zielplattform steht in `Directory.Build.props` (`PkTfm`).

```bash
dotnet build ProfileKit.sln
dotnet test tests/ProfileKit.Core.Tests
```

Im Linux-Container gibt es nur das .NET-8-SDK aus den Ubuntu-Paketen; dort mit
`-p:PkTfm=net8.0` bauen und testen. Windows-Projekte bauen dank
`EnableWindowsTargeting` auch unter Linux, laufen dort aber nicht. Maßgeblich ist die
CI auf `windows-latest` (`.github/workflows/build.yml`).

## Regeln für den Code

- Core-Code muss C# 12 kompatibel bleiben (lokaler Build mit SDK 8).
- Pfade in Modulen/Paketen **immer** mit Token (`%APPDATA%` …), nie absolut.
- Registry-Zugriffe nur über `IRegistryBackend`; neue Logik mit In-Memory-Backend testen.
- KI-Ergebnisse sind Vorschläge: nie ungeprüft ausführen, nie schreibende Werkzeuge
  für den Agenten.
- Keine Passwörter, Tokens oder DPAPI-Daten in Regeln aufnehmen. Keyfiles nur mit
  `"kind": "keyfile"`.
- Alle Texte für Benutzer zweisprachig (DE/EN) über `Localizer`.
- Neue Katalogregel: winget-ID angeben, Caches/Logs in `excludes`, Quelle der
  Information im Feld `notes`.
