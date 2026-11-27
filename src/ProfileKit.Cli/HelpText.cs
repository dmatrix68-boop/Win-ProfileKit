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

        Global:
          --lang de|en       language (default: system language, also PROFILEKIT_LANG)
          --rules <folder>   additional rules folder
          --verbose          details on errors

        Exit codes: 0 OK · 1 error · 2 partial success (warnings) · 3 invalid arguments
        """;
}
