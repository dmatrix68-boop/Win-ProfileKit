using ProfileKit.Core.Catalog;
using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Agent;

public sealed record PathCheck(string Path, bool Exists, int Files, long Bytes);

public sealed record KeyCheck(string Key, bool Exists, int Values);

/// <summary>Ergebnis der Prüfung eines Regelvorschlags auf dem aktuellen Rechner.</summary>
public sealed record ProposalValidation
{
    public List<PathCheck> Paths { get; init; } = [];
    public List<KeyCheck> Keys { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];

    public bool IsValid => Errors.Count == 0;
}

/// <summary>Ein von einer KI vorgeschlagener, noch nicht übernommener Regelentwurf.</summary>
public sealed record Proposal
{
    public required ModuleRule Rule { get; init; }
    public required string Agent { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public required ProposalValidation Validation { get; init; }
}

/// <summary>
/// Ablage für KI-Vorschläge (<c>%LOCALAPPDATA%\Win-ProfileKit\proposals</c>). Vorschläge werden erst
/// mit <see cref="Accept"/> zu Regeln – nie automatisch.
/// </summary>
public sealed class ProposalStore(string directory)
{
    public string Directory { get; } = directory;

    public static string DefaultDirectory(string localAppData) => Path.Combine(localAppData, "Win-ProfileKit", "proposals");

    public string PathFor(string id)
    {
        ProfilePackage.ValidateId(id);
        return Path.Combine(Directory, id + ".json");
    }

    public void Save(Proposal proposal) => Json.Write(PathFor(proposal.Rule.Id), proposal);

    public Proposal? Find(string id) => File.Exists(PathFor(id)) ? Json.Read<Proposal>(PathFor(id)) : null;

    public IReadOnlyList<Proposal> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var list = new List<Proposal>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try { list.Add(Json.Read<Proposal>(file)); }
            catch (System.Text.Json.JsonException) { /* beschädigte Datei überspringen */ }
        }
        return list.OrderByDescending(p => p.CreatedUtc).ToList();
    }

    /// <summary>Übernimmt einen Vorschlag als Regel in den Regelordner und entfernt ihn aus der Ablage.</summary>
    public string Accept(string id, string rulesDirectory)
    {
        var proposal = Find(id) ?? throw new FileNotFoundException($"Kein Vorschlag '{id}'.");
        RuleCatalog.Validate(proposal.Rule);
        var target = Path.Combine(rulesDirectory, id + ".json");
        Json.Write(target, proposal.Rule with { Origin = RuleOrigin.Ai });
        File.Delete(PathFor(id));
        return target;
    }

    public bool Reject(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}

/// <summary>Prüft einen Regelentwurf formal und gegen den tatsächlichen Rechner.</summary>
public static class ProposalValidator
{
    public const long LargeBytes = 50L * 1024 * 1024;

    public static ProposalValidation Validate(ModuleRule rule, AccessGuard guard, IRegistryBackend registry)
    {
        var v = new ProposalValidation();
        try { RuleCatalog.Validate(rule); }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { v.Errors.Add(ex.Message); return v; }

        if (rule.Files.Count + rule.Registry.Count == 0) v.Errors.Add("The rule contains neither files nor registry keys.");
        if (rule.WingetId is null && !rule.IsWindowsModule) v.Warnings.Add("wingetId is missing – the program cannot be installed automatically.");

        var excludes = new ExcludeSet(rule.Excludes);
        foreach (var file in rule.Files)
        {
            string absolute;
            try { absolute = guard.ResolvePath(file.Path).Absolute; }
            catch (UnauthorizedAccessException ex) { v.Errors.Add(ex.Message); continue; }

            if (File.Exists(absolute))
            {
                var len = new FileInfo(absolute).Length;
                v.Paths.Add(new(file.Path, true, 1, len));
                if (!AccessGuard.IsContentAllowed(file.Path) && file.Kind != FileKind.Keyfile)
                    v.Warnings.Add($"{file.Path} looks like a credential or license file – mark license files as \"kind\": \"Keyfile\", never include credentials.");
            }
            else if (Directory.Exists(absolute))
            {
                int count = 0; long bytes = 0;
                foreach (var f in Directory.EnumerateFiles(absolute, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                {
                    var rel = Path.GetRelativePath(absolute, f);
                    if (excludes.IsExcluded(rel)) continue;
                    count++;
                    try { bytes += new FileInfo(f).Length; } catch (IOException) { }
                    if (count > 20000) break;
                }
                v.Paths.Add(new(file.Path, true, count, bytes));
                if (bytes > LargeBytes) v.Warnings.Add($"{file.Path} is large ({bytes / (1024 * 1024)} MB) – check for caches/databases and add excludes.");
            }
            else
            {
                v.Paths.Add(new(file.Path, false, 0, 0));
                v.Warnings.Add($"{file.Path} does not exist on this computer.");
            }
        }

        foreach (var reg in rule.Registry)
        {
            string key;
            try { key = AccessGuard.NormalizeRegistryKey(reg.Key); }
            catch (UnauthorizedAccessException ex) { v.Errors.Add(ex.Message); continue; }
            var values = registry.Read(key, reg.Values, reg.Recurse).Count;
            var exists = values > 0 || registry.GetSubKeyNames(key).Count > 0;
            v.Keys.Add(new(key, exists, values));
            if (!exists) v.Warnings.Add($"{key} does not exist on this computer.");
            if (reg.Values.Any(AccessGuard.IsSensitiveValueName))
                v.Warnings.Add($"{key}: value names look sensitive ({string.Join(", ", reg.Values.Where(AccessGuard.IsSensitiveValueName))}).");
        }

        if (v.Paths.All(p => !p.Exists) && v.Keys.All(k => !k.Exists))
            v.Errors.Add("None of the proposed paths or keys exist on this computer.");
        return v;
    }
}
