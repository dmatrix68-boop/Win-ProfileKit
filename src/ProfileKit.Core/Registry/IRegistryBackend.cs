namespace ProfileKit.Core.Registry;

/// <summary>
/// Abstraktion über die Registry. <c>HKCU\</c> bezeichnet immer den Hive des jeweiligen
/// (Quell- bzw. Ziel-)Benutzers – egal ob angemeldet, offline geladen oder Default-Profil.
/// </summary>
public interface IRegistryBackend
{
    /// <summary>Liest Werte eines Schlüssels. Fehlt der Schlüssel, ist das Ergebnis leer.</summary>
    /// <param name="key">Schlüssel mit Wurzel <c>HKCU\</c> oder <c>HKLM\</c>.</param>
    /// <param name="valueNames">Nur diese Werte; leer = alle.</param>
    /// <param name="recurse">Unterschlüssel einschließen (nur bei allen Werten).</param>
    IReadOnlyList<RegistryValue> Read(string key, IReadOnlyCollection<string> valueNames, bool recurse);

    /// <summary>Schreibt einen Wert, legt fehlende Schlüssel an.</summary>
    void Write(RegistryValue value);

    /// <summary>Liest einen einzelnen Wert oder null.</summary>
    RegistryValue? Get(string key, string name);
}

/// <summary>Hilfsfunktionen für Registry-Pfade.</summary>
public static class RegistryPath
{
    public const string CurrentUser = "HKCU";
    public const string LocalMachine = "HKLM";

    private static readonly Dictionary<string, string> RootAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HKCU"] = CurrentUser,
        ["HKEY_CURRENT_USER"] = CurrentUser,
        ["HKLM"] = LocalMachine,
        ["HKEY_LOCAL_MACHINE"] = LocalMachine,
    };

    /// <summary>Zerlegt <c>HKCU\Software\X</c> in Wurzel (normalisiert) und Unterpfad.</summary>
    public static (string Root, string SubKey) Split(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var trimmed = key.Trim().Trim('\\');
        var idx = trimmed.IndexOf('\\');
        var root = idx < 0 ? trimmed : trimmed[..idx];
        var sub = idx < 0 ? string.Empty : trimmed[(idx + 1)..].Trim('\\');
        if (!RootAliases.TryGetValue(root, out var normalized))
            throw new ArgumentException($"Nicht unterstützte Registry-Wurzel: '{root}' (erlaubt: HKCU, HKLM).", nameof(key));
        return (normalized, sub);
    }

    public static string Normalize(string key)
    {
        var (root, sub) = Split(key);
        return sub.Length == 0 ? root : $"{root}\\{sub}";
    }

    public static string Combine(string key, string child) => $"{Normalize(key)}\\{child.Trim('\\')}";
}
