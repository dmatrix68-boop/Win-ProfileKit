using System.Text.Json.Serialization;

namespace ProfileKit.Core.Model;

/// <summary>Woher eine Regel stammt.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RuleOrigin>))]
public enum RuleOrigin
{
    Manual,
    Catalog,
    LearnMode,
    Ai,
}

/// <summary>Art eines Dateieintrags.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FileKind>))]
public enum FileKind
{
    /// <summary>Normale Konfigurationsdatei bzw. -ordner.</summary>
    Config,

    /// <summary>Lizenz-Keyfile (z. B. WinRAR rarreg.key) – wird kopiert, aber gesondert ausgewiesen.</summary>
    Keyfile,
}

/// <summary>Datei oder Ordner, der zu einem Modul gehört. <see cref="Path"/> ist immer tokenisiert.</summary>
public sealed record FileRule
{
    /// <summary>Tokenisierter Pfad, z. B. <c>%APPDATA%\Notepad++</c>.</summary>
    public required string Path { get; init; }

    public FileKind Kind { get; init; } = FileKind.Config;

    /// <summary>Fehlt die Datei auf der Quelle, ist das kein Fehler (Standard: true).</summary>
    public bool Optional { get; init; } = true;
}

/// <summary>Registry-Schlüssel oder einzelner Wert, der zu einem Modul gehört.</summary>
public sealed record RegistryRule
{
    /// <summary>Schlüssel mit Wurzel <c>HKCU\</c> oder <c>HKLM\</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Einzelne Werte; leer = alle Werte des Schlüssels.</summary>
    public List<string> Values { get; init; } = [];

    /// <summary>Unterschlüssel mit erfassen (nur wenn <see cref="Values"/> leer ist).</summary>
    public bool Recurse { get; init; } = true;
}

/// <summary>Zu entfernende Einträge in einer Konfigurationsdatei.</summary>
public sealed record RedactRule
{
    /// <summary>Datei als Glob relativ zum Dateieintrag, z. B. <c>settings.json</c> oder <c>**\*.ini</c>.</summary>
    public required string File { get; init; }

    /// <summary>
    /// JSON-Pfade mit Punkt getrennt, z. B. <c>Configuration.User.TokenProtected</c>;
    /// <c>**.Name</c> entfernt die Eigenschaft in jeder Tiefe (Name als Glob, z. B. <c>**.*Protected</c>).
    /// </summary>
    public List<string> Json { get; init; } = [];

    /// <summary>INI-Schlüssel, wahlweise mit Abschnitt: <c>SMTPPassword</c> (alle Abschnitte) oder <c>Generic.SMTPPassword</c>.</summary>
    public List<string> Ini { get; init; } = [];

    /// <summary>XPath-Ausdrücke für XML-Dateien; gefundene Elemente bzw. Attribute werden entfernt, z. B. <c>//setting[@name='RecentFindThats']</c>.</summary>
    public List<string> Xml { get; init; } = [];
}

/// <summary>Regel: was gehört zu einem Programm bzw. Windows-Bereich?</summary>
public sealed record ModuleRule
{
    /// <summary>Eindeutige, dateinamentaugliche Kennung, z. B. <c>notepadplusplus</c> oder <c>windows.mouse</c>.</summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>winget-Paket-ID; null für Windows-Module.</summary>
    public string? WingetId { get; init; }

    public List<FileRule> Files { get; init; } = [];

    public List<RegistryRule> Registry { get; init; } = [];

    /// <summary>Ausschlüsse als Glob relativ zum jeweiligen Dateieintrag, z. B. <c>**\*.log</c>, <c>backup\**</c>.</summary>
    public List<string> Excludes { get; init; } = [];

    /// <summary>
    /// Einträge, die beim Erfassen aus Konfigurationsdateien entfernt werden (Zugangsdaten, Tokens).
    /// Die Datei selbst wird übernommen, die genannten Schlüssel landen nie im Profil.
    /// </summary>
    public List<RedactRule> Redact { get; init; } = [];

    /// <summary>Hinweise zur Live-Aktivierung nach dem Anwenden, z. B. <c>mouse</c>, <c>theme</c>, <c>explorer</c>.</summary>
    public List<string> PostApply { get; init; } = [];

    /// <summary>Prozesse, die vor dem Anwenden beendet sein sollten (ohne .exe).</summary>
    public List<string> Processes { get; init; } = [];

    public RuleOrigin Origin { get; init; } = RuleOrigin.Manual;

    /// <summary>Freitext: Quelle der Information, Besonderheiten.</summary>
    public string? Notes { get; init; }

    [JsonIgnore]
    public bool IsWindowsModule => Id.StartsWith("windows.", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool ContainsKeyfiles => Files.Any(f => f.Kind == FileKind.Keyfile);
}
