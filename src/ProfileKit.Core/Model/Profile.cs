using System.Text.Json.Serialization;

namespace ProfileKit.Core.Model;

/// <summary>Beschreibt, von welchem Benutzer und Rechner ein Profil erfasst wurde.</summary>
public sealed record SourceInfo
{
    public required string Machine { get; init; }
    public required string UserName { get; init; }
    public string? Sid { get; init; }

    /// <summary>Absoluter Profilpfad der Quelle – Grundlage für das Umschreiben von Inhalten.</summary>
    public required string ProfilePath { get; init; }
}

/// <summary>Eintrag in der Modulliste des Manifests.</summary>
public sealed record ModuleEntry
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? WingetId { get; init; }
    public string? Version { get; init; }
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public int RegistryValueCount { get; init; }
    public bool ContainsKeyfiles { get; init; }
}

/// <summary>Inhalt von <c>profile.json</c>.</summary>
public sealed record ProfileManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? ToolVersion { get; init; }
    public required SourceInfo Source { get; init; }
    public List<ModuleEntry> Modules { get; init; } = [];

    [JsonIgnore]
    public bool ContainsKeyfiles => Modules.Any(m => m.ContainsKeyfiles);
}
