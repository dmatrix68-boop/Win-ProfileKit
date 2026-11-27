using System.Text.Json.Serialization;

namespace ProfileKit.Core.Registry;

[JsonConverter(typeof(JsonStringEnumConverter<RegValueKind>))]
public enum RegValueKind
{
    String,
    ExpandString,
    MultiString,
    DWord,
    QWord,
    Binary,
    None,
}

/// <summary>
/// Ein erfasster Registry-Wert. Genau eines der Datenfelder ist gesetzt, passend zu <see cref="Kind"/>:
/// <see cref="Text"/> (String, ExpandString), <see cref="Lines"/> (MultiString),
/// <see cref="Number"/> (DWord, QWord), <see cref="Base64"/> (Binary, None).
/// </summary>
public sealed record RegistryValue
{
    /// <summary>Schlüssel mit Wurzel <c>HKCU\</c> oder <c>HKLM\</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Wertname; leerer String = Standardwert.</summary>
    public required string Name { get; init; }

    public required RegValueKind Kind { get; init; }

    public string? Text { get; init; }
    public List<string>? Lines { get; init; }
    public long? Number { get; init; }
    public string? Base64 { get; init; }

    public static RegistryValue FromString(string key, string name, string text, bool expand = false) =>
        new() { Key = key, Name = name, Kind = expand ? RegValueKind.ExpandString : RegValueKind.String, Text = text };

    public static RegistryValue FromDWord(string key, string name, int value) =>
        new() { Key = key, Name = name, Kind = RegValueKind.DWord, Number = unchecked((uint)value) };

    public static RegistryValue FromQWord(string key, string name, long value) =>
        new() { Key = key, Name = name, Kind = RegValueKind.QWord, Number = value };

    public static RegistryValue FromBinary(string key, string name, byte[] data, RegValueKind kind = RegValueKind.Binary) =>
        new() { Key = key, Name = name, Kind = kind, Base64 = Convert.ToBase64String(data) };

    public static RegistryValue FromMulti(string key, string name, IEnumerable<string> lines) =>
        new() { Key = key, Name = name, Kind = RegValueKind.MultiString, Lines = lines.ToList() };

    public byte[] GetBytes() => Base64 is null ? [] : Convert.FromBase64String(Base64);

    /// <summary>Wendet eine Textersetzung auf alle String-Daten an (für Pfad-Umschreibung).</summary>
    public RegistryValue MapStrings(Func<string, string> map) => Kind switch
    {
        RegValueKind.String or RegValueKind.ExpandString when Text is not null => this with { Text = map(Text) },
        RegValueKind.MultiString when Lines is not null => this with { Lines = Lines.Select(map).ToList() },
        _ => this,
    };
}
