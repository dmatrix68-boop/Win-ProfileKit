using ProfileKit.Core.Model;

namespace ProfileKit.Core.Learning;

/// <summary>
/// Leitet aus einem Snapshot-Vergleich einen Regelvorschlag ab. Gelöschte Einträge werden
/// ignoriert (nur Hinzugefügtes und Geändertes ist übertragbare Konfiguration).
/// <para>Dateien: geänderte Dateien je Programmordner werden zum gemeinsamen Ordner zusammengefasst
/// (z. B. <c>%APPDATA%\Tool</c>), mindestens aber bis zur Hersteller-Ebene.</para>
/// <para>Registry: herstellereigene Schlüssel werden als ganzer Schlüssel übernommen; gemeinsam genutzte
/// Windows-Schlüssel (<c>Software\Microsoft</c>, <c>Control Panel</c>) nur mit den geänderten Werten.</para>
/// </summary>
public static class RuleProposer
{
    /// <summary>Ordner unter einem Token, die mehrere Hersteller/Produkte bündeln: eine Ebene tiefer ansetzen.</summary>
    private static readonly HashSet<string> SharedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Packages", "Programs", "Google", "Mozilla", "JetBrains", "Adobe", ".config", ".local",
    };

    private static readonly HashSet<string> SharedRegistryVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Classes", "Wow6432Node", "Policies",
    };

    public static ModuleRule Propose(SnapshotDiff diff, string id, string displayName, string? wingetId)
    {
        var relevantFiles = diff.Files.Where(f => f.Kind != ChangeKind.Removed).Select(f => f.Path).ToList();
        var relevantRegistry = diff.Registry.Where(r => r.Kind != ChangeKind.Removed).ToList();

        var files = ProposeFiles(relevantFiles);
        var registry = ProposeRegistry(relevantRegistry);

        var notes = $"Lernmodus {DateTime.Now:yyyy-MM-dd}: {relevantFiles.Count} geänderte Dateien, {relevantRegistry.Count} geänderte Registry-Werte. " +
                    "Vorschlag prüfen: Ordner auf Caches durchsehen und ggf. in excludes aufnehmen.";

        return new ModuleRule
        {
            Id = id,
            DisplayName = displayName,
            WingetId = wingetId,
            Files = files,
            Registry = registry,
            Excludes = ["*.log", "*.tmp", "**\\Cache\\**", "**\\logs\\**"],
            Origin = RuleOrigin.LearnMode,
            Notes = notes,
        };
    }

    public static List<FileRule> ProposeFiles(IReadOnlyList<string> paths)
    {
        var groups = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var anchorLength = FileAnchorLength(segments);
            var anchor = string.Join('\\', segments.Take(anchorLength));
            if (!groups.TryGetValue(anchor, out var list)) groups[anchor] = list = [];
            list.Add(segments);
        }

        var result = new List<FileRule>();
        foreach (var (anchor, members) in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var anchorLength = anchor.Split('\\').Length;
            // gemeinsamer Ordner = gemeinsamer Präfix der Elternordner
            var parents = members.Select(m => m[..^1]).ToList();
            var common = CommonPrefix(parents);
            var proposed = common.Length >= anchorLength
                ? string.Join('\\', common)
                : members.Count == 1 ? string.Join('\\', members[0]) : anchor;
            result.Add(new FileRule { Path = proposed });
        }
        return result;
    }

    private static int FileAnchorLength(string[] segments)
    {
        // Token + Hersteller/Produkt; bei Sammelordnern eine Ebene tiefer, bei Packages\<Paket>\LocalState zwei
        if (segments.Length <= 2) return segments.Length;
        if (segments[1].Equals("Packages", StringComparison.OrdinalIgnoreCase)) return Math.Min(segments.Length, 3);
        return SharedFolders.Contains(segments[1]) ? Math.Min(segments.Length, 3) : 2;
    }

    public static List<RegistryRule> ProposeRegistry(IReadOnlyList<RegistryChange> changes)
    {
        var result = new List<RegistryRule>();
        var vendorGroups = new Dictionary<string, List<RegistryChange>>(StringComparer.OrdinalIgnoreCase);
        var sharedKeys = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in changes)
        {
            var segments = change.Key.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var anchorLength = RegistryAnchorLength(segments);
            if (anchorLength == 0)
            {
                if (!sharedKeys.TryGetValue(change.Key, out var names)) sharedKeys[change.Key] = names = new(StringComparer.OrdinalIgnoreCase);
                names.Add(change.Name);
                continue;
            }
            var anchor = string.Join('\\', segments.Take(anchorLength));
            if (!vendorGroups.TryGetValue(anchor, out var list)) vendorGroups[anchor] = list = [];
            list.Add(change);
        }

        foreach (var (anchor, members) in vendorGroups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var common = CommonPrefix(members.Select(m => m.Key.Split('\\', StringSplitOptions.RemoveEmptyEntries)).ToList());
            var key = common.Length >= anchor.Split('\\').Length ? string.Join('\\', common) : anchor;
            result.Add(new RegistryRule { Key = key, Recurse = true });
        }
        foreach (var (key, names) in sharedKeys.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            result.Add(new RegistryRule { Key = key, Values = [.. names], Recurse = false });

        return result;
    }

    /// <summary>Windows-eigene Bereiche unter Software\Microsoft – dort nur einzelne Werte übernehmen.</summary>
    private static readonly HashSet<string> SharedMicrosoftAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Windows NT", "Office", "Internet Explorer", "Active Setup", "CTF", "Windows Defender",
    };

    /// <summary>
    /// Länge des Hersteller-Ankers (z. B. <c>HKCU\Software\Ghisler</c> = 3,
    /// <c>HKCU\Software\Microsoft\PowerToys</c> = 4) oder 0 für gemeinsam genutzte Schlüssel
    /// (Control Panel, Software\Microsoft\Windows …), von denen nur einzelne Werte übernommen werden.
    /// </summary>
    private static int RegistryAnchorLength(string[] segments)
    {
        if (segments.Length < 3 || !segments[1].Equals("Software", StringComparison.OrdinalIgnoreCase)) return 0;
        var vendor = segments[2];
        if (vendor.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
            return segments.Length >= 4 && !SharedMicrosoftAreas.Contains(segments[3]) ? 4 : 0;
        return SharedRegistryVendors.Contains(vendor) ? 0 : 3;
    }

    private static string[] CommonPrefix(IReadOnlyList<string[]> items)
    {
        if (items.Count == 0) return [];
        var prefix = items[0];
        foreach (var item in items.Skip(1))
        {
            var n = 0;
            while (n < prefix.Length && n < item.Length && string.Equals(prefix[n], item[n], StringComparison.OrdinalIgnoreCase)) n++;
            prefix = prefix[..n];
        }
        return prefix;
    }
}
