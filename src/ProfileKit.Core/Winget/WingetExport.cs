using System.Text.Json;

namespace ProfileKit.Core.Winget;

/// <summary>Ein installiertes Paket, wie winget es kennt.</summary>
public sealed record InstalledPackage(string Id, string? Version = null, string? Source = "winget");

/// <summary>
/// Liest die Ausgabe von <c>winget export</c> (Schema 2.0) – dasselbe Format importiert der
/// OEM-Skriptgenerator.
/// </summary>
public static class WingetExport
{
    public static IReadOnlyList<InstalledPackage> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var result = new List<InstalledPackage>();
        if (!doc.RootElement.TryGetProperty("Sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var source in sources.EnumerateArray())
        {
            string? sourceName = null;
            if (source.TryGetProperty("SourceDetails", out var details) && details.TryGetProperty("Name", out var name))
                sourceName = name.GetString();

            if (!source.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array) continue;
            foreach (var p in packages.EnumerateArray())
            {
                if (!p.TryGetProperty("PackageIdentifier", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                string? version = p.TryGetProperty("Version", out var v) ? v.GetString() : null;
                result.Add(new InstalledPackage(id, version, sourceName ?? "winget"));
            }
        }
        return result;
    }

    /// <summary>Erzeugt eine winget-Exportdatei, z. B. zur Übergabe an den OEM-Skriptgenerator.</summary>
    public static string Create(IEnumerable<InstalledPackage> packages)
    {
        var doc = new
        {
            schema = "https://aka.ms/winget-packages.schema.2.0.json",
            CreationDate = DateTimeOffset.UtcNow.ToString("o"),
            Sources = new[]
            {
                new
                {
                    Packages = packages.Select(p => p.Version is null
                        ? (object)new { PackageIdentifier = p.Id }
                        : new { PackageIdentifier = p.Id, Version = p.Version }).ToArray(),
                    SourceDetails = new
                    {
                        Argument = "https://cdn.winget.microsoft.com/cache",
                        Identifier = "Microsoft.Winget.Source_8wekyb3d8bbwe",
                        Name = "winget",
                        Type = "Microsoft.PreIndexed.Package",
                    },
                },
            },
        };
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        return json.Replace("\"schema\"", "\"$schema\"", StringComparison.Ordinal);
    }
}

/// <summary>Bewertung von winget-Exitcodes (nach AppInstallerErrors.h aus winget-cli).</summary>
public static class WingetExitCodes
{
    public const int RestartRequired = 3010;
    public const int RestartInitiated = 1641;
    public const int AlreadyInstalled = unchecked((int)0x8A150061);
    public const int NoApplicableUpgrade = unchecked((int)0x8A15002B);
    public const int NoPackageFound = unchecked((int)0x8A150014);

    public static bool IsSuccess(int exitCode) =>
        exitCode is 0 or RestartRequired or RestartInitiated or AlreadyInstalled or NoApplicableUpgrade;

    public static string Describe(int exitCode) => exitCode switch
    {
        0 => "OK",
        RestartRequired => "OK, Neustart erforderlich",
        RestartInitiated => "OK, Neustart eingeleitet",
        AlreadyInstalled => "bereits installiert",
        NoApplicableUpgrade => "bereits aktuell",
        NoPackageFound => "kein Paket mit dieser ID gefunden",
        unchecked((int)0x8A150011) => "Installation durch den Benutzer abgebrochen",
        unchecked((int)0x8A15010D) => "anderer Installer läuft bereits",
        -1073741515 => "winget-Abhängigkeiten fehlen (Aufruf als SYSTEM?)",
        _ => $"Fehler 0x{exitCode:X8}",
    };
}
