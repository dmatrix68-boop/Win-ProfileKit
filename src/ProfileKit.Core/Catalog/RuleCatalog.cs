using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Catalog;

/// <summary>Regelkatalog: JSON-Dateien je Modul, rekursiv aus einem oder mehreren Ordnern.</summary>
public sealed class RuleCatalog
{
    private readonly Dictionary<string, ModuleRule> _byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ModuleRule> Rules => _byId.Values;

    public List<string> LoadErrors { get; } = [];

    /// <summary>Lädt alle Regeln; spätere Ordner überschreiben frühere (eigene Regeln vor Katalog).</summary>
    public static RuleCatalog Load(params string[] directories)
    {
        var catalog = new RuleCatalog();
        foreach (var dir in directories.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var rule = Json.Read<ModuleRule>(file);
                    Validate(rule);
                    catalog._byId[rule.Id] = rule;
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or ArgumentException or InvalidDataException)
                {
                    catalog.LoadErrors.Add($"{file}: {ex.Message}");
                }
            }
        }
        return catalog;
    }

    public ModuleRule? Find(string id) => _byId.GetValueOrDefault(id);

    public ModuleRule? FindByWingetId(string wingetId) =>
        _byId.Values.FirstOrDefault(r => string.Equals(r.WingetId, wingetId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Prüft eine Regel auf formale Fehler. Wirft bei Verstößen.</summary>
    public static void Validate(ModuleRule rule)
    {
        ProfilePackage.ValidateId(rule.Id);
        if (string.IsNullOrWhiteSpace(rule.DisplayName)) throw new FormatException($"{rule.Id}: displayName fehlt.");
        foreach (var f in rule.Files) PathTokens.Parse(f.Path);
        foreach (var r in rule.Registry) RegistryPath.Split(r.Key);
        foreach (var e in rule.Excludes) _ = new Glob(e);
    }
}
