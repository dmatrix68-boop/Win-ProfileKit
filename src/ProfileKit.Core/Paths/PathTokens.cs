namespace ProfileKit.Core.Paths;

/// <summary>
/// Wandelt absolute Pfade in tokenisierte Pfade (<c>%APPDATA%\X</c>) und zurück.
/// </summary>
public static class PathTokens
{
    public sealed record Token(string Name, string FolderName, Func<UserContext, string> Resolve);

    /// <summary>Alle Token; Reihenfolge = Priorität beim Tokenisieren (spezifischster zuerst).</summary>
    public static readonly IReadOnlyList<Token> All =
    [
        new("LOCALAPPDATALOW", "LOCALAPPDATALOW", c => c.LocalAppDataLow),
        new("LOCALAPPDATA", "LOCALAPPDATA", c => c.LocalAppData),
        new("APPDATA", "APPDATA", c => c.AppData),
        new("USERPROFILE", "USERPROFILE", c => c.ProfilePath),
        new("PROGRAMFILES(X86)", "PROGRAMFILESX86", c => c.ProgramFilesX86),
        new("PROGRAMFILES", "PROGRAMFILES", c => c.ProgramFiles),
        new("PROGRAMDATA", "PROGRAMDATA", c => c.ProgramData),
    ];

    /// <summary>Zerlegt <c>%APPDATA%\Notepad++\x.xml</c> in Token und Restpfad (mit '\').</summary>
    public static (Token Token, string Remainder) Parse(string tokenizedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenizedPath);
        var p = tokenizedPath.Trim();
        if (!p.StartsWith('%'))
            throw new FormatException($"Pfad ist nicht tokenisiert: '{tokenizedPath}'. Erwartet z. B. %APPDATA%\\Programm.");
        var end = p.IndexOf('%', 1);
        if (end < 0) throw new FormatException($"Token nicht abgeschlossen: '{tokenizedPath}'.");
        var name = p[1..end];
        var token = All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException($"Unbekanntes Token '%{name}%'. Erlaubt: {string.Join(", ", All.Select(t => $"%{t.Name}%"))}.");
        var rest = p[(end + 1)..].Replace('/', '\\').Trim('\\');
        if (rest.Split('\\').Any(s => s == ".."))
            throw new FormatException($"'..' ist in tokenisierten Pfaden nicht erlaubt: '{tokenizedPath}'.");
        return (token, rest);
    }

    /// <summary>Löst einen tokenisierten Pfad für den Benutzer auf.</summary>
    public static string Expand(string tokenizedPath, UserContext context)
    {
        var (token, rest) = Parse(tokenizedPath);
        var root = token.Resolve(context);
        return rest.Length == 0 ? root : PathText.Join(root, rest);
    }

    /// <summary>Relativer Ablagepfad im Paket, z. B. <c>APPDATA\Notepad++</c>.</summary>
    public static string ToPackagePath(string tokenizedPath)
    {
        var (token, rest) = Parse(tokenizedPath);
        return rest.Length == 0 ? token.FolderName : token.FolderName + "\\" + rest;
    }

    /// <summary>
    /// Wandelt einen absoluten Pfad in einen tokenisierten. Liefert null, wenn kein Token passt
    /// (Pfad liegt außerhalb der bekannten Orte).
    /// </summary>
    public static string? TryTokenize(string absolutePath, UserContext context)
    {
        var path = PathText.NormalizeWindows(absolutePath);
        foreach (var token in All)
        {
            var root = PathText.NormalizeWindows(token.Resolve(context));
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                return $"%{token.Name}%";
            if (path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                return $"%{token.Name}%\\{path[(root.Length + 1)..]}";
        }
        return null;
    }

    /// <summary>Liegt der Pfad im Benutzerprofil (und nicht maschinenweit)?</summary>
    public static bool IsUserScoped(string tokenizedPath) =>
        Parse(tokenizedPath).Token.Name is "LOCALAPPDATALOW" or "LOCALAPPDATA" or "APPDATA" or "USERPROFILE";
}
