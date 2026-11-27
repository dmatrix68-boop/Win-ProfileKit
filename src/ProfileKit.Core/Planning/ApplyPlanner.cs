using ProfileKit.Core.Model;
using ProfileKit.Core.Winget;

namespace ProfileKit.Core.Planning;

public enum PlanAction
{
    /// <summary>Programm fehlt, wird per winget installiert, danach konfiguriert.</summary>
    InstallAndConfigure,

    /// <summary>Programm ist bereits installiert (oder Windows-Modul) – nur konfigurieren.</summary>
    ConfigureOnly,

    /// <summary>Programm fehlt und eine Installation ist nicht gewünscht oder nicht möglich.</summary>
    ConfigureWithoutProgram,
}

public sealed record PlanItem(ModuleEntry Module, PlanAction Action, string? Reason = null);

/// <summary>Plant das Anwenden eines Profils anhand des Inventars des Zielrechners.</summary>
public static class ApplyPlanner
{
    public static IReadOnlyList<PlanItem> Plan(IEnumerable<ModuleEntry> modules, IReadOnlyCollection<InstalledPackage> targetInventory, bool installMissing)
    {
        var installed = new HashSet<string>(targetInventory.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        var plan = new List<PlanItem>();
        foreach (var m in modules)
        {
            if (m.WingetId is null)
                plan.Add(new(m, PlanAction.ConfigureOnly, m.Id.StartsWith("windows.", StringComparison.OrdinalIgnoreCase) ? null : "keine winget-ID"));
            else if (installed.Contains(m.WingetId))
                plan.Add(new(m, PlanAction.ConfigureOnly, "bereits installiert"));
            else if (installMissing)
                plan.Add(new(m, PlanAction.InstallAndConfigure));
            else
                plan.Add(new(m, PlanAction.ConfigureWithoutProgram, "nicht installiert, --install nicht gesetzt"));
        }
        // Windows-Module zuletzt, damit z. B. Explorer-Neustarts nicht mitten in Installationen fallen
        return plan.OrderBy(p => p.Module.Id.StartsWith("windows.", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ToList();
    }
}

/// <summary>Filter für Modul-IDs, z. B. <c>windows.*</c> oder <c>notepadplusplus,7zip</c>.</summary>
public sealed class ModuleFilter
{
    private readonly List<Paths.Glob> _globs;

    public ModuleFilter(string? spec)
    {
        _globs = string.IsNullOrWhiteSpace(spec)
            ? []
            : spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => new Paths.Glob(s)).ToList();
    }

    public bool IsEmpty => _globs.Count == 0;

    public bool Matches(string moduleId) => IsEmpty || _globs.Any(g => g.IsMatch(moduleId));
}
