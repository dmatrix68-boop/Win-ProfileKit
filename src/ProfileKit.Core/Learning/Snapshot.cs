using System.Text.Json.Serialization;

namespace ProfileKit.Core.Learning;

/// <summary>Zeitstempel einer Datei im Snapshot: Größe und letzte Änderung (UTC-Ticks).</summary>
public sealed record FileStamp(long Size, long ModifiedTicks);

/// <summary>
/// Momentaufnahme der Dateien und Registry-Werte eines Benutzers. Dateien sind tokenisiert
/// (<c>%APPDATA%\X\y.ini</c>), Registry-Werte als Fingerabdruck (Typ + Hash der Daten) gespeichert –
/// der Snapshot enthält also keine Inhalte.
/// </summary>
public sealed record Snapshot
{
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public required string UserName { get; init; }
    public required string ProfilePath { get; init; }

    /// <summary>Tokenisierter Pfad → Stempel.</summary>
    public Dictionary<string, FileStamp> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registry-Schlüssel → (Wertname → Fingerabdruck).</summary>
    public Dictionary<string, Dictionary<string, string>> Registry { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public int RegistryValueCount => Registry.Values.Sum(v => v.Count);
}
