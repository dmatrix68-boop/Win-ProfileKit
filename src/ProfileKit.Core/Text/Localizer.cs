using System.Globalization;

namespace ProfileKit.Core.Text;

/// <summary>
/// Zweisprachige Texte (DE/EN). Standard ist die UI-Sprache des Systems; Deutsch für "de-*",
/// sonst Englisch. Fehlende Schlüssel liefern den Schlüssel selbst.
/// </summary>
public static class Localizer
{
    public static string Language { get; private set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";

    public static void SetLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return;
        Language = language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
    }

    public static string T(string key, params object[] args)
    {
        var table = Language == "de" ? De : En;
        var text = table.TryGetValue(key, out var t) ? t : En.TryGetValue(key, out var e) ? e : key;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>Alle Schlüssel müssen in beiden Sprachen vorhanden sein (von Tests geprüft).</summary>
    public static IReadOnlyDictionary<string, string> German => De;
    public static IReadOnlyDictionary<string, string> English => En;

    private static readonly Dictionary<string, string> De = new()
    {
        ["error"] = "Fehler: {0}",
        ["warning"] = "Warnung: {0}",
        ["usage"] = "Aufruf: profilekit <Befehl> [Optionen]   ·   profilekit help",
        ["unknown-command"] = "Unbekannter Befehl '{0}'.",
        ["missing-option"] = "Option {0} fehlt.",
        ["rules-loaded"] = "{0} Regeln geladen.",
        ["rule-error"] = "Regel fehlerhaft: {0}",
        ["capture-start"] = "Erfasse Profil '{0}' von Benutzer {1} nach {2}",
        ["capture-module"] = "  {0,-28} {1,5} Dateien {2,10}  {3,4} Registry-Werte",
        ["capture-done"] = "Fertig: {0} Module erfasst.",
        ["keyfile-notice"] = "Hinweis: Das Profil enthält Lizenz-Keyfiles ({0}). Nicht öffentlich weitergeben.",
        ["no-modules"] = "Keine passenden Module gefunden.",
        ["module-unknown"] = "Keine Regel für Modul '{0}'.",
        ["apply-start"] = "Wende Profil '{0}' auf {1} an",
        ["apply-dryrun"] = "Trockenlauf – es wird nichts geändert.",
        ["plan-install"] = "installieren + konfigurieren",
        ["plan-configure"] = "konfigurieren",
        ["plan-noprogram"] = "nur konfigurieren (Programm fehlt)",
        ["installing"] = "  winget install {0} …",
        ["install-result"] = "    → {0}",
        ["apply-module"] = "  {0,-28} {1,4} Dateien  {2,4} Registry-Werte",
        ["apply-done"] = "Fertig. Sicherung: {0}",
        ["apply-done-nobackup"] = "Fertig.",
        ["needs-admin"] = "Dafür sind Administratorrechte nötig (Eingabeaufforderung als Administrator starten).",
        ["user-not-found"] = "Benutzer '{0}' hat kein Profil auf diesem Rechner.",
        ["user-logged-on"] = "Benutzer ist angemeldet – Registry wird live geschrieben.",
        ["user-offline"] = "Benutzer ist nicht angemeldet – NTUSER.DAT wird offline geladen.",
        ["user-created"] = "Benutzer '{0}' angelegt, Profil: {1}",
        ["password-prompt"] = "Kennwort für {0} (leer = kein Kennwort): ",
        ["users-header"] = "Benutzer                 Angemeldet  Profilpfad",
        ["inventory-header"] = "Installiert (winget)                          Regel",
        ["catalog-header"] = "Modul                        winget-ID                     Name",
        ["processes-running"] = "Bitte zuerst beenden: {0}",
        ["target-default"] = "Default-Profil (gilt für neue Benutzer)",
        ["live-applied"] = "Einstellungen für den angemeldeten Benutzer aktiviert: {0}",
        ["archive-written"] = "Archiv geschrieben: {0}",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["error"] = "Error: {0}",
        ["warning"] = "Warning: {0}",
        ["usage"] = "Usage: profilekit <command> [options]   ·   profilekit help",
        ["unknown-command"] = "Unknown command '{0}'.",
        ["missing-option"] = "Option {0} is required.",
        ["rules-loaded"] = "{0} rules loaded.",
        ["rule-error"] = "Invalid rule: {0}",
        ["capture-start"] = "Capturing profile '{0}' from user {1} to {2}",
        ["capture-module"] = "  {0,-28} {1,5} files {2,10}  {3,4} registry values",
        ["capture-done"] = "Done: {0} modules captured.",
        ["keyfile-notice"] = "Note: the profile contains license keyfiles ({0}). Do not share it publicly.",
        ["no-modules"] = "No matching modules found.",
        ["module-unknown"] = "No rule for module '{0}'.",
        ["apply-start"] = "Applying profile '{0}' to {1}",
        ["apply-dryrun"] = "Dry run – nothing will be changed.",
        ["plan-install"] = "install + configure",
        ["plan-configure"] = "configure",
        ["plan-noprogram"] = "configure only (program missing)",
        ["installing"] = "  winget install {0} …",
        ["install-result"] = "    → {0}",
        ["apply-module"] = "  {0,-28} {1,4} files  {2,4} registry values",
        ["apply-done"] = "Done. Backup: {0}",
        ["apply-done-nobackup"] = "Done.",
        ["needs-admin"] = "Administrator rights are required (run the command prompt as administrator).",
        ["user-not-found"] = "User '{0}' has no profile on this computer.",
        ["user-logged-on"] = "User is logged on – registry is written live.",
        ["user-offline"] = "User is not logged on – NTUSER.DAT is loaded offline.",
        ["user-created"] = "User '{0}' created, profile: {1}",
        ["password-prompt"] = "Password for {0} (empty = no password): ",
        ["users-header"] = "User                     Logged on   Profile path",
        ["inventory-header"] = "Installed (winget)                            Rule",
        ["catalog-header"] = "Module                       winget ID                     Name",
        ["processes-running"] = "Please close first: {0}",
        ["target-default"] = "Default profile (applies to new users)",
        ["live-applied"] = "Settings activated for the logged-on user: {0}",
        ["archive-written"] = "Archive written: {0}",
    };
}
