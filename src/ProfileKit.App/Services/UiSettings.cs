using ProfileKit.Core.Packaging;

namespace ProfileKit.App.Services;

/// <summary>Einstellungen der Oberfläche (<c>%LOCALAPPDATA%\Win-ProfileKit\ui.json</c>).</summary>
public sealed record UiSettings
{
    public string? Language { get; init; }
    public string? LastProfileFolder { get; init; }

    /// <summary>Fensterposition und -größe beim letzten Schließen (physische Pixel, normaler Zustand).</summary>
    public WindowPlacement? Window { get; init; }

    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win-ProfileKit", "ui.json");

    public static UiSettings Load()
    {
        try { return File.Exists(Path) ? Json.Read<UiSettings>(Path) : new UiSettings(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException) { return new UiSettings(); }
    }

    public void Save() => Json.Write(Path, this);
}

/// <summary>Gespeicherte Fensterlage; bei maximiertem Fenster die Lage vor dem Maximieren.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);
