using ProfileKit.Core.Catalog;
using ProfileKit.Core.Model;
using ProfileKit.Core.Planning;
using ProfileKit.Core.Text;
using ProfileKit.Core.Winget;

namespace ProfileKit.Core.Tests;

public class CatalogTests
{
    private static readonly string RulesDir = Path.Combine(AppContext.BaseDirectory, "rules");

    [Fact]
    public void Shipped_catalog_is_valid()
    {
        var catalog = RuleCatalog.Load(RulesDir);
        Assert.Empty(catalog.LoadErrors);
        Assert.True(catalog.Rules.Count >= 8);
        foreach (var rule in catalog.Rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Notes), $"{rule.Id}: notes fehlt");
            Assert.True(rule.IsWindowsModule || rule.WingetId is not null, $"{rule.Id}: winget-ID fehlt");
            Assert.True(rule.Files.Count + rule.Registry.Count > 0, $"{rule.Id}: leer");
        }
    }

    [Fact]
    public void WinRar_keyfiles_are_marked()
    {
        var winrar = RuleCatalog.Load(RulesDir).FindByWingetId("rarlab.winrar");
        Assert.NotNull(winrar);
        Assert.True(winrar!.ContainsKeyfiles);
    }

    [Theory]
    [InlineData("totalcommander", @"wcx_ftp.ini")]   // FTP-Zugangsdaten
    [InlineData("powertoys", @"PowerToys Run\Logs\0.1\log.txt")]
    [InlineData("everything", "Everything-1.5a.db")]
    [InlineData("xnviewmp", "Thumb.db")]
    public void Sensitive_or_cache_files_are_excluded(string id, string relative)
    {
        var rule = RuleCatalog.Load(RulesDir).Find(id)!;
        Assert.True(new Paths.ExcludeSet(rule.Excludes).IsExcluded(relative), $"{id}: {relative} sollte ausgeschlossen sein");
    }

    [Fact]
    public void RustDesk_never_captures_id_and_password_file()
    {
        var rule = RuleCatalog.Load(RulesDir).Find("rustdesk")!;
        Assert.DoesNotContain(rule.Files, f => f.Path.EndsWith(@"\RustDesk.toml", StringComparison.OrdinalIgnoreCase)
                                             || f.Path.EndsWith(@"\config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Later_directories_override_earlier()
    {
        using var root = new TempRoot();
        var own = Path.Combine(root.Path, "own");
        Directory.CreateDirectory(own);
        File.WriteAllText(Path.Combine(own, "x.json"), """{ "id": "7zip", "displayName": "Eigene", "wingetId": "7zip.7zip", "notes": "n" }""");
        File.WriteAllText(Path.Combine(own, "kaputt.json"), """{ "id": "kaputt", "displayName": "K", "files": [ { "path": "C:\\absolut" } ] }""");

        var catalog = RuleCatalog.Load(RulesDir, own);
        Assert.Equal("Eigene", catalog.Find("7zip")!.DisplayName);
        Assert.Single(catalog.LoadErrors);
        Assert.Null(catalog.Find("kaputt"));
    }

    [Fact]
    public void Planner_distinguishes_installed_missing_and_windows()
    {
        var modules = new List<ModuleEntry>
        {
            new() { Id = "windows.mouse", DisplayName = "Maus" },
            new() { Id = "7zip", DisplayName = "7-Zip", WingetId = "7zip.7zip" },
            new() { Id = "winrar", DisplayName = "WinRAR", WingetId = "RARLab.WinRAR" },
        };
        var plan = ApplyPlanner.Plan(modules, [new InstalledPackage("7ZIP.7ZIP")], installMissing: true);

        Assert.Equal(["7zip", "winrar", "windows.mouse"], plan.Select(p => p.Module.Id));
        Assert.Equal(PlanAction.ConfigureOnly, plan[0].Action);
        Assert.Equal(PlanAction.InstallAndConfigure, plan[1].Action);
        Assert.Equal(PlanAction.ConfigureOnly, plan[2].Action);

        var noInstall = ApplyPlanner.Plan(modules, [], installMissing: false);
        Assert.Equal(PlanAction.ConfigureWithoutProgram, noInstall.Single(p => p.Module.Id == "winrar").Action);
    }

    [Fact]
    public void Localizer_has_same_keys_in_both_languages() =>
        Assert.Equal(Localizer.German.Keys.Order(), Localizer.English.Keys.Order());
}
