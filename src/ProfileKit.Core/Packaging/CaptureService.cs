using ProfileKit.Core.Model;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Packaging;

/// <summary>Ergebnis der Erfassung eines Moduls.</summary>
public sealed record CaptureResult(ModuleEntry Entry, IReadOnlyList<string> Warnings);

/// <summary>Erfasst Module eines Quellbenutzers in einen Profilordner.</summary>
public sealed class CaptureService
{
    /// <summary>Dateien über dieser Größe werden übersprungen (Caches, Datenbanken).</summary>
    public long MaxFileBytes { get; init; } = 200L * 1024 * 1024;

    public CaptureResult CaptureModule(ProfilePackage package, ModuleRule rule, UserContext source, IRegistryBackend registry, string? version = null)
    {
        ProfilePackage.ValidateId(rule.Id);
        var warnings = new List<string>();
        var moduleDir = package.ModuleDirectory(rule.Id);
        FileSystem.DeleteDirectory(moduleDir);
        Directory.CreateDirectory(moduleDir);

        var excludes = new ExcludeSet(rule.Excludes);
        int fileCount = 0, redacted = 0;
        long totalBytes = 0;

        foreach (var file in rule.Files)
        {
            var sourcePath = PathTokens.Expand(file.Path, source);
            var target = package.PackagedPathFor(rule.Id, file.Path);

            if (File.Exists(sourcePath))
            {
                if (CopyFile(sourcePath, target, warnings) && RedactCopy(rule, Path.GetFileName(sourcePath), target, warnings) is { } n)
                {
                    redacted += n;
                    fileCount++; totalBytes += new FileInfo(target).Length;
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                foreach (var f in Directory.EnumerateFiles(sourcePath, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                {
                    var relative = Path.GetRelativePath(sourcePath, f);
                    if (excludes.IsExcluded(relative)) continue;
                    var dest = PathText.Join(target, relative);
                    if (CopyFile(f, dest, warnings) && RedactCopy(rule, relative, dest, warnings) is { } n)
                    {
                        redacted += n;
                        fileCount++; totalBytes += new FileInfo(dest).Length;
                    }
                }
            }
            else if (!file.Optional)
            {
                warnings.Add($"Pflichtpfad fehlt auf der Quelle: {file.Path} ({sourcePath})");
            }
        }

        var values = new List<RegistryValue>();
        foreach (var reg in rule.Registry)
            values.AddRange(registry.Read(reg.Key, reg.Values, reg.Recurse));
        package.WriteRegistry(rule.Id, values);
        package.WriteModule(rule);

        var entry = new ModuleEntry
        {
            Id = rule.Id,
            DisplayName = rule.DisplayName,
            WingetId = rule.WingetId,
            Version = version,
            FileCount = fileCount,
            TotalBytes = totalBytes,
            RegistryValueCount = values.Count,
            ContainsKeyfiles = rule.ContainsKeyfiles,
            RedactedEntries = redacted,
        };
        return new CaptureResult(entry, warnings);
    }

    /// <summary>
    /// Entfernt Zugangsdaten aus der Kopie im Profil (laut <see cref="ModuleRule.Redact"/>); liefert die Anzahl entfernter
    /// Einträge oder null, wenn die Datei nicht bereinigt werden konnte – dann wird die Kopie gelöscht (fail closed).
    /// </summary>
    private static int? RedactCopy(ModuleRule rule, string relativePath, string packagedFile, List<string> warnings)
    {
        if (rule.Redact.Count == 0) return 0;
        var rules = Text.Redactor.RulesFor(rule.Redact, relativePath);
        if (rules.Count == 0) return 0;
        try
        {
            var result = Text.Redactor.Redact(File.ReadAllBytes(packagedFile), rules);
            if (result is null) return 0;
            File.WriteAllBytes(packagedFile, result.Value.Bytes);
            return result.Value.Removed;
        }
        catch (Text.RedactionException ex)
        {
            File.Delete(packagedFile);
            warnings.Add($"Nicht übernommen, weil Zugangsdaten nicht entfernt werden konnten ({ex.Message}): {relativePath}");
            return null;
        }
    }

    private bool CopyFile(string source, string dest, List<string> warnings)
    {
        try
        {
            var info = new FileInfo(source);
            if (info.Length > MaxFileBytes)
            {
                warnings.Add($"Übersprungen (größer als {MaxFileBytes / (1024 * 1024)} MB): {source}");
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            // FileShare.ReadWrite: Konfigurationsdateien laufender Programme trotzdem lesen
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(dest, FileMode.Create, FileAccess.Write);
            input.CopyTo(output);
            output.Flush();
            File.SetLastWriteTimeUtc(dest, info.LastWriteTimeUtc);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Nicht lesbar: {source} – {ex.Message}");
            return false;
        }
    }
}
