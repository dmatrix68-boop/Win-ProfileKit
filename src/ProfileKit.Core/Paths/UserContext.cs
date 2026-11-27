namespace ProfileKit.Core.Paths;

/// <summary>
/// Konkrete Pfade eines Benutzers auf einem Rechner. Grundlage für das Auflösen der Pfad-Token
/// und für das Umschreiben von Inhalten.
/// </summary>
public sealed record UserContext
{
    public required string UserName { get; init; }
    public string? Sid { get; init; }

    /// <summary>Absoluter Profilpfad, z. B. <c>C:\Users\max</c>.</summary>
    public required string ProfilePath { get; init; }

    public string ProgramData { get; init; } = @"C:\ProgramData";
    public string ProgramFiles { get; init; } = @"C:\Program Files";
    public string ProgramFilesX86 { get; init; } = @"C:\Program Files (x86)";

    public string AppData => PathText.Join(ProfilePath, "AppData", "Roaming");
    public string LocalAppData => PathText.Join(ProfilePath, "AppData", "Local");
    public string LocalAppDataLow => PathText.Join(ProfilePath, "AppData", "LocalLow");

    /// <summary>Ist das Ziel das Default-Profil (gilt für künftig angelegte Benutzer)?</summary>
    public bool IsDefaultProfile { get; init; }
}

/// <summary>Pfad-Hilfen, die mit Windows-Pfaden auch auf anderen Plattformen (Tests) arbeiten.</summary>
public static class PathText
{
    /// <summary>Trennzeichen des Pfads erkennen: enthält er '\' oder ein Laufwerk, gilt Windows-Syntax.</summary>
    public static char SeparatorOf(string path) =>
        path.Contains('\\') || (path.Length >= 2 && path[1] == ':') ? '\\' : Path.DirectorySeparatorChar;

    public static string Join(string root, params string[] parts)
    {
        var sep = SeparatorOf(root);
        var result = root.TrimEnd('\\', '/');
        foreach (var p in parts)
        {
            var clean = p.Replace('\\', sep).Replace('/', sep).Trim(sep);
            if (clean.Length > 0) result = result + sep + clean;
        }
        return result;
    }

    /// <summary>Vereinheitlicht Trennzeichen auf '\' und entfernt doppelte/abschließende Trenner.</summary>
    public static string NormalizeWindows(string path)
    {
        var p = path.Replace('/', '\\');
        while (p.Contains(@"\\", StringComparison.Ordinal) && !p.StartsWith(@"\\", StringComparison.Ordinal))
            p = p.Replace(@"\\", @"\", StringComparison.Ordinal);
        return p.Length > 3 ? p.TrimEnd('\\') : p;
    }
}
