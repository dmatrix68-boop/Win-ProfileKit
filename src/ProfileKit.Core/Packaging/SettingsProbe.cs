using ProfileKit.Core.Model;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Packaging;

/// <summary>
/// Schnelle Prüfung, ob ein Quellbenutzer zu einer Regel überhaupt Einstellungen hat – für die Programmliste der
/// Oberfläche. Liest nur Existenz (Datei/Ordner/Registry-Schlüssel), nichts wird kopiert.
/// </summary>
public static class SettingsProbe
{
    /// <summary>True, wenn mindestens ein Pfad oder Registry-Schlüssel der Regel beim Benutzer existiert.</summary>
    /// <param name="registry">null = Registry nicht prüfbar (z. B. ohne Adminrechte) – dann zählen nur Dateien.</param>
    public static bool HasSettings(ModuleRule rule, UserContext user, IRegistryBackend? registry)
    {
        foreach (var file in rule.Files)
        {
            string path;
            try { path = PathTokens.Expand(file.Path, user); }
            catch (ArgumentException) { continue; }
            if (File.Exists(path) || Directory.Exists(path)) return true;
        }
        if (registry is null) return false;
        foreach (var reg in rule.Registry)
        {
            try
            {
                if (reg.Values.Count > 0)
                {
                    if (reg.Values.Any(v => registry.Get(reg.Key, v) is not null)) return true;
                }
                else if (registry.Read(reg.Key, [], recurse: false).Count > 0 || registry.GetSubKeyNames(reg.Key).Count > 0)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // nicht lesbar = nicht gefunden
            }
        }
        return false;
    }
}
