using ProfileKit.Core.Text;

namespace ProfileKit.Core.Packaging;

/// <summary>Robuste Dateioperationen für Profilordner (OneDrive, Virenscanner, schreibgeschützte Kopien).</summary>
public static class FileSystem
{
    /// <summary>
    /// Löscht einen Ordner rekursiv. Schreibschutz wird vorher entfernt; kurzzeitige Sperren (OneDrive-Synchronisierung,
    /// Virenscanner) werden mit einigen Wiederholungen abgewartet. Schlägt das endgültig fehl, enthält die Meldung den
    /// vollständigen Pfad und mögliche Ursachen.
    /// </summary>
    public static void DeleteDirectory(string path, int attempts = 5)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                ClearReadOnly(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < attempts)
            {
                Thread.Sleep(150 * attempt);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(Localizer.T("dir-delete-failed", path, ex.Message), ex);
            }
        }
    }

    private static void ClearReadOnly(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }
    }
}
