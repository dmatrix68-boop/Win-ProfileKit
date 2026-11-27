using ProfileKit.Core.Text;

namespace ProfileKit.Cli;

internal static class HelpText
{
    public static string Get() => Localizer.Language == "de" ? German : English;

    private const string German = """
        Win-ProfileKit – App- und Windows-Einstellungen als Profil erfassen und anwenden

        Befehle:
          users                           lokale Benutzerprofile anzeigen
          catalog                         Regelkatalog anzeigen
          inventory [--out datei.json]    installierte Software (winget) und passende Regeln
          capture --out <Ordner> --name <Name> (--modules a,b | --installed) [--user <Name>] [--zip]
                                          Profil erfassen; Module als Liste oder Muster (windows.*),
                                          --installed = alle installierten Programme mit Regel
          info --profile <Ordner|.pkprofile>
                                          Inhalt eines Profils anzeigen
          apply --profile <Ordner|.pkprofile>
                (--current-user | --user <Name> | --default-user | --create-user <Name>)
                [--install] [--only <Muster>] [--dry-run] [--no-backup] [--no-machine]
                                          Profil anwenden; --install installiert fehlende Programme
          user create <Name> [--admin] [--password-stdin]
                                          lokalen Benutzer anlegen und Profil sofort erzeugen
          learn start [--machine] [--exclude <Muster;Muster>]
                                          Lernmodus: Momentaufnahme vor dem Ändern der Einstellungen
          learn diff                      bisherige Änderungen anzeigen (Sitzung läuft weiter)
          learn stop --id <modul-id> [--name <Programm>] [--winget <winget-id>] [--out <datei>] [--force]
                                          Änderungen auswerten und als Regel speichern
                                          (Standard: %LOCALAPPDATA%\Win-ProfileKit\rules)

        KI:
          ai status                       gefundene KI-Anwendungen, Ollama, OpenRouter, gewählte Stufe
          ai discover --app <Programm> [--winget <id>] [--id <modul-id>]
                [--provider auto|claude-code|ollama|openrouter] [--model <m>] [--accept-cloud]
                                          KI sucht die Einstellungen und legt einen Regelvorschlag ab
          ai proposals [--show <id>]      offene Vorschläge anzeigen
          ai accept <id> [--force]        Vorschlag als Regel übernehmen
          ai reject <id>                  Vorschlag verwerfen
          ai models --provider ollama|openrouter [--filter <text>] [--all]
                                          verfügbare Modelle (live)
          ai config [--provider …] [--ollama-url …] [--ollama-model …] [--openrouter-model …]
                [--max-steps n] [--accept-cloud | --revoke-cloud]
          ai key set [--stdin] | ai key remove
                                          OpenRouter-Key in der Anmeldeinformationsverwaltung
          mcp                             MCP-Server (stdio) für Claude Desktop, Claude Code u. a.
          mcp install [--uninstall]       in Claude Desktop eintragen bzw. austragen
          mcp config                      Konfiguration für andere MCP-Clients ausgeben

        Global:
          --lang de|en       Sprache (Standard: Systemsprache, auch PROFILEKIT_LANG)
          --rules <Ordner>   zusätzlicher Regelordner
          --verbose          Details bei Fehlern

        Exitcodes: 0 OK · 1 Fehler · 2 Teilerfolg (Warnungen) · 3 ungültige Argumente
        """;

    private const string English = """
        Win-ProfileKit – capture app and Windows settings as a profile and apply them

        Commands:
          users                           list local user profiles
          catalog                         list the rule catalog
          inventory [--out file.json]     installed software (winget) and matching rules
          capture --out <folder> --name <name> (--modules a,b | --installed) [--user <name>] [--zip]
                                          capture a profile; modules as list or pattern (windows.*),
                                          --installed = every installed program that has a rule
          info --profile <folder|.pkprofile>
                                          show the contents of a profile
          apply --profile <folder|.pkprofile>
                (--current-user | --user <name> | --default-user | --create-user <name>)
                [--install] [--only <pattern>] [--dry-run] [--no-backup] [--no-machine]
                                          apply a profile; --install installs missing programs
          user create <name> [--admin] [--password-stdin]
                                          create a local user and its profile right away
          learn start [--machine] [--exclude <pattern;pattern>]
                                          learning mode: snapshot before changing the settings
          learn diff                      show changes so far (session keeps running)
          learn stop --id <module-id> [--name <program>] [--winget <winget-id>] [--out <file>] [--force]
                                          evaluate the changes and save them as a rule
                                          (default: %LOCALAPPDATA%\Win-ProfileKit\rules)

        AI:
          ai status                       AI applications found, Ollama, OpenRouter, chosen tier
          ai discover --app <program> [--winget <id>] [--id <module-id>]
                [--provider auto|claude-code|ollama|openrouter] [--model <m>] [--accept-cloud]
                                          the AI looks for the settings and stores a rule proposal
          ai proposals [--show <id>]      list open proposals
          ai accept <id> [--force]        accept a proposal as rule
          ai reject <id>                  discard a proposal
          ai models --provider ollama|openrouter [--filter <text>] [--all]
                                          available models (live)
          ai config [--provider …] [--ollama-url …] [--ollama-model …] [--openrouter-model …]
                [--max-steps n] [--accept-cloud | --revoke-cloud]
          ai key set [--stdin] | ai key remove
                                          OpenRouter key in Windows Credential Manager
          mcp                             MCP server (stdio) for Claude Desktop, Claude Code and others
          mcp install [--uninstall]       register in / remove from Claude Desktop
          mcp config                      print configuration for other MCP clients

        Global:
          --lang de|en       language (default: system language, also PROFILEKIT_LANG)
          --rules <folder>   additional rules folder
          --verbose          details on errors

        Exit codes: 0 OK · 1 error · 2 partial success (warnings) · 3 invalid arguments
        """;
}
