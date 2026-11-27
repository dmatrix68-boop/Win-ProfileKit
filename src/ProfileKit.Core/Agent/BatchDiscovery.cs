using System.Text.Json;
using ProfileKit.Core.Catalog;
using ProfileKit.Core.Winget;

namespace ProfileKit.Core.Agent;

/// <summary>Ein Paket aus einer Quelle (OEM-Projekt, winget-Export, Inventar).</summary>
public sealed record SourcePackage(string Name, string? WingetId, string Manager = "winget");

/// <summary>Liest Paketlisten für den Stapelmodus von <c>ai discover</c>.</summary>
public static class PackageSources
{
    /// <summary>
    /// Pakete aus einer Projektdatei des OEM-Skriptgenerators (<c>*.oemproj.json</c>, <c>apps.items</c>).
    /// Nur aktivierte Pakete; eine winget-ID gibt es nur für Manager winget mit Quelle winget.
    /// Alle anderen Teile der Datei (z. B. Kontodaten) werden nicht gelesen.
    /// </summary>
    public static IReadOnlyList<SourcePackage> FromOemProject(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (!doc.RootElement.TryGetProperty("apps", out var apps) || !apps.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Keine OEM-Projektdatei (apps.items fehlt).");

        var result = new List<SourcePackage>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False) continue;
            var id = Str(item, "id");
            var name = Str(item, "name") ?? id;
            if (name is null) continue;
            var manager = Str(item, "manager") ?? "winget";
            var source = Str(item, "source") ?? "winget";
            var wingetId = manager.Equals("winget", StringComparison.OrdinalIgnoreCase) && source.Equals("winget", StringComparison.OrdinalIgnoreCase) ? id : null;
            result.Add(new SourcePackage(name, wingetId, manager + (source.Equals(manager, StringComparison.OrdinalIgnoreCase) ? "" : "/" + source)));
        }
        return result;
    }

    public static IReadOnlyList<SourcePackage> FromWingetExport(string json) =>
        WingetExport.Parse(json).Select(p => new SourcePackage(p.Id, p.Id)).ToList();

    public static IReadOnlyList<SourcePackage> FromInventory(IEnumerable<InstalledPackage> packages) =>
        packages.Select(p => new SourcePackage(p.Id, p.Id)).ToList();

    /// <summary>
    /// Liste der Pakete ohne eigene Einstellungen (eine winget-ID oder ein Programmname je Zeile, <c>#</c> leitet Kommentare ein,
    /// <c>*</c> am Ende als Platzhalter, z. B. <c>Microsoft.DotNet.*</c>).
    /// </summary>
    public static IReadOnlyList<string> ParseSkipList(string text) =>
        text.Split('\n')
            .Select(l => (l.Contains('#') ? l[..l.IndexOf('#')] : l).Trim())
            .Where(l => l.Length > 0)
            .ToList();

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
}

public enum BatchDecision
{
    /// <summary>Wird von der KI untersucht.</summary>
    Discover,
    /// <summary>Es gibt schon eine Regel.</summary>
    HasRule,
    /// <summary>Es gibt schon einen offenen Vorschlag.</summary>
    HasProposal,
    /// <summary>Laut Liste ohne eigene Einstellungen (Laufzeitumgebungen usw.).</summary>
    NoSettings,
    /// <summary>Die Modul-ID ist schon an ein anderes Programm vergeben.</summary>
    IdConflict,
}

public sealed record BatchItem(SourcePackage Package, string ModuleId, BatchDecision Decision, string? Detail = null);

/// <summary>Entscheidet, welche Pakete der Stapelmodus untersucht.</summary>
public static class BatchPlanner
{
    public static IReadOnlyList<BatchItem> Plan(IEnumerable<SourcePackage> packages, RuleCatalog catalog,
        IReadOnlyCollection<string> openProposalIds, IReadOnlyList<string> skipList, bool retryProposals = false)
    {
        var skip = skipList.Select(p => new Paths.Glob(p)).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<BatchItem>();

        foreach (var pkg in packages)
        {
            var key = pkg.WingetId ?? pkg.Name;
            if (!seen.Add(key)) continue;

            var byWinget = pkg.WingetId is null ? null : catalog.FindByWingetId(pkg.WingetId);
            var id = byWinget?.Id ?? ModuleIds.FromName(pkg.Name);

            if (byWinget is not null) { plan.Add(new(pkg, id, BatchDecision.HasRule, byWinget.DisplayName)); continue; }
            if (skip.Any(g => (pkg.WingetId is not null && g.IsMatch(pkg.WingetId)) || g.IsMatch(pkg.Name))) { plan.Add(new(pkg, id, BatchDecision.NoSettings)); continue; }

            var byId = catalog.Find(id);
            if (byId is not null)
            {
                // gleiche ID, aber anderes oder unbekanntes Programm: ohne winget-ID gilt die Regel als zuständig
                plan.Add(byId.WingetId is null || pkg.WingetId is null
                    ? new(pkg, id, BatchDecision.HasRule, byId.DisplayName)
                    : new(pkg, id, BatchDecision.IdConflict, $"{byId.Id} = {byId.WingetId}"));
                continue;
            }
            if (!retryProposals && openProposalIds.Contains(id, StringComparer.OrdinalIgnoreCase)) { plan.Add(new(pkg, id, BatchDecision.HasProposal)); continue; }
            plan.Add(new(pkg, id, BatchDecision.Discover));
        }
        return plan;
    }
}
