using System.Text.RegularExpressions;
using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;
using ProfileKit.Core.Winget;

namespace ProfileKit.Core.Inventory;

/// <summary>Ein Eintrag aus „Apps &amp; Features“ (Uninstall-Schlüssel).</summary>
public sealed record UninstallEntry(string Name, string? Version, string? Publisher, string? InstallLocation, string Key);

/// <summary>Liest die Uninstall-Einträge (HKLM 64/32 Bit und HKCU) – ohne Systemkomponenten.</summary>
public static class UninstallInventory
{
    public static readonly IReadOnlyList<string> Keys =
    [
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public static IReadOnlyList<UninstallEntry> Read(IRegistryBackend registry, UserContext? user = null)
    {
        var result = new List<UninstallEntry>();
        foreach (var root in Keys)
        {
            foreach (var sub in registry.GetSubKeyNames(root))
            {
                var key = root + "\\" + sub;
                var values = registry.Read(key, ["DisplayName", "DisplayVersion", "Publisher", "InstallLocation", "SystemComponent", "ParentKeyName"], false)
                    .ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
                if (!values.TryGetValue("DisplayName", out var name) || string.IsNullOrWhiteSpace(name.Text)) continue;
                if (values.TryGetValue("SystemComponent", out var sys) && sys.Number == 1) continue;
                if (values.ContainsKey("ParentKeyName")) continue; // Updates/Unterkomponenten
                var location = values.GetValueOrDefault("InstallLocation")?.Text?.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(location)) location = null;
                else if (user is not null) location = PathTokens.TryTokenize(location, user) ?? location;
                result.Add(new UninstallEntry(name.Text!.Trim(), values.GetValueOrDefault("DisplayVersion")?.Text,
                    values.GetValueOrDefault("Publisher")?.Text, location, key));
            }
        }
        return result
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>Inventar eines Rechners, wie es der Sammler neben das Profil legt (<c>inventory.json</c>).</summary>
public sealed record MachineInventory
{
    public const string FileName = "inventory.json";
    public const string WingetExportFileName = "winget-export.json";

    public required string Machine { get; init; }
    public required string UserName { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Pakete laut <c>winget export</c>; leer, wenn winget nicht verfügbar war.</summary>
    public List<InstalledPackage> Winget { get; init; } = [];

    public List<UninstallEntry> Programs { get; init; } = [];

    public static MachineInventory? TryRead(ProfilePackage package)
    {
        var path = Path.Combine(package.Root, FileName);
        return File.Exists(path) ? Json.Read<MachineInventory>(path) : null;
    }

    public void Write(ProfilePackage package)
    {
        Json.Write(Path.Combine(package.Root, FileName), this);
        File.WriteAllText(Path.Combine(package.Root, WingetExportFileName), WingetExport.Create(Winget));
    }
}

public enum Presence
{
    /// <summary>Windows-Modul – immer anwendbar.</summary>
    Windows,
    /// <summary>Programm ist auf dem Zielrechner installiert.</summary>
    Installed,
    /// <summary>Programm fehlt, lässt sich per winget installieren.</summary>
    Installable,
    /// <summary>Programm fehlt, keine winget-ID bekannt.</summary>
    Missing,
}

public sealed record ModulePresence(ModuleEntry Module, Presence Presence, string? MatchedBy = null);

/// <summary>Gleicht Module bzw. Regeln mit dem Inventar eines Rechners ab (winget-ID, sonst Programmname).</summary>
public static class InventoryMatcher
{
    private static readonly Regex Parenthesized = new(@"\s*\([^)]*\)", RegexOptions.CultureInvariant);

    /// <summary>Anzeigename ohne Klammerzusätze: „Everything 1.5 (Alpha)“ → „Everything 1.5“.</summary>
    public static string NormalizeName(string name) => Parenthesized.Replace(name, string.Empty).Trim();

    /// <summary>Ist das Programm installiert? Liefert, woran es erkannt wurde, sonst null.</summary>
    public static string? FindInstalled(string? wingetId, string displayName, IReadOnlyCollection<InstalledPackage> winget, IReadOnlyCollection<UninstallEntry> programs)
    {
        if (wingetId is not null && winget.Any(p => p.Id.Equals(wingetId, StringComparison.OrdinalIgnoreCase)))
            return "winget " + wingetId;
        var name = NormalizeName(displayName);
        if (name.Length < 3) return null;
        var match = programs.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : match.Name;
    }

    public static IReadOnlyList<ModulePresence> Compare(IEnumerable<ModuleEntry> modules, IReadOnlyCollection<InstalledPackage> winget, IReadOnlyCollection<UninstallEntry> programs) =>
        modules.Select(m =>
        {
            if (m.Id.StartsWith("windows.", StringComparison.OrdinalIgnoreCase)) return new ModulePresence(m, Presence.Windows);
            var found = FindInstalled(m.WingetId, m.DisplayName, winget, programs);
            if (found is not null) return new ModulePresence(m, Presence.Installed, found);
            return new ModulePresence(m, m.WingetId is null ? Presence.Missing : Presence.Installable);
        }).ToList();

    /// <summary>Regeln, deren Programm auf dem Rechner installiert ist (für den Sammler).</summary>
    public static IReadOnlyList<ModuleRule> RulesForInstalled(IEnumerable<ModuleRule> rules, IReadOnlyCollection<InstalledPackage> winget, IReadOnlyCollection<UninstallEntry> programs) =>
        rules.Where(r => !r.IsWindowsModule && FindInstalled(r.WingetId, r.DisplayName, winget, programs) is not null).ToList();

    /// <summary>Installierte Programme der Quelle, für die keine Regel greift – Kandidaten für Lernmodus/KI.</summary>
    public static IReadOnlyList<UninstallEntry> Uncovered(IReadOnlyCollection<UninstallEntry> programs, IEnumerable<ModuleRule> rules)
    {
        var names = rules.Where(r => !r.IsWindowsModule).Select(r => NormalizeName(r.DisplayName)).Where(n => n.Length >= 3).ToList();
        return programs.Where(p => !names.Any(n => p.Name.Contains(n, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}
