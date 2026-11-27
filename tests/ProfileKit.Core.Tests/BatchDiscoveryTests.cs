using ProfileKit.Core.Agent;
using ProfileKit.Core.Catalog;

namespace ProfileKit.Core.Tests;

public class BatchDiscoveryTests
{
    private const string OemProject = """
        {
          "formatVersion": 2,
          "apps": { "items": [
            { "uid": "1", "enabled": true,  "id": "7zip.7zip", "name": "7-Zip", "version": "26.02", "source": "winget", "manager": "winget", "args": "" },
            { "uid": "2", "enabled": true,  "id": "AntibodySoftware.WizTree", "name": "WizTree", "source": "winget", "manager": "winget" },
            { "uid": "3", "enabled": true,  "id": "Contoso.PhotoTool", "name": "Contoso PhotoTool", "source": "winget", "manager": "winget" },
            { "uid": "4", "enabled": true,  "id": "Microsoft.DotNet.DesktopRuntime.10", "name": ".NET Desktop Runtime 10", "source": "winget", "manager": "winget" },
            { "uid": "5", "enabled": false, "id": "Disabled.App", "name": "Deaktiviert", "source": "winget", "manager": "winget" },
            { "uid": "6", "enabled": true,  "id": "9ND14WHFRGSX", "name": "Modern Winver", "source": "msstore", "manager": "winget" },
            { "uid": "7", "enabled": true,  "id": "rustdesk.install", "name": "Rustdesk", "source": "community", "manager": "chocolatey" },
            { "uid": "8", "enabled": true,  "id": "Contoso.Burner", "name": "Contoso Burner", "source": "winget", "manager": "winget" },
            { "uid": "9", "enabled": true,  "id": "Other.SevenZip", "name": "7zip", "source": "winget", "manager": "winget" }
          ] },
          "answer": { "account": { "accounts": [ { "name": "x", "password": "nicht-lesen" } ] } }
        }
        """;

    private static RuleCatalog Catalog => RuleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "rules"));

    private static IReadOnlyList<string> Skip =>
        PackageSources.ParseSkipList(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules", "no-settings.list")));

    [Fact]
    public void Reads_enabled_packages_and_winget_ids_only_for_winget_source()
    {
        var packages = PackageSources.FromOemProject(OemProject);
        Assert.Equal(8, packages.Count);                                   // deaktiviertes Paket fehlt
        Assert.Null(packages.Single(p => p.Name == "Modern Winver").WingetId);   // msstore
        Assert.Null(packages.Single(p => p.Name == "Rustdesk").WingetId);        // chocolatey
        Assert.Equal("chocolatey/community", packages.Single(p => p.Name == "Rustdesk").Manager);
        Assert.Equal("Contoso.PhotoTool", packages.Single(p => p.Name == "Contoso PhotoTool").WingetId);
    }

    [Fact]
    public void Plans_only_programs_without_rule_proposal_or_skip_entry()
    {
        var plan = BatchPlanner.Plan(PackageSources.FromOemProject(OemProject), Catalog, ["contosoburner"], Skip)
            .ToDictionary(i => i.Package.Name);

        Assert.Equal(BatchDecision.HasRule, plan["7-Zip"].Decision);
        Assert.Equal(BatchDecision.HasRule, plan["WizTree"].Decision);
        Assert.Equal("wiztree", plan["WizTree"].ModuleId);
        Assert.Equal(BatchDecision.Discover, plan["Contoso PhotoTool"].Decision);
        Assert.Equal("contosophototool", plan["Contoso PhotoTool"].ModuleId);
        Assert.Equal(BatchDecision.NoSettings, plan[".NET Desktop Runtime 10"].Decision);
        Assert.Equal(BatchDecision.HasProposal, plan["Contoso Burner"].Decision);
        Assert.Equal(BatchDecision.HasRule, plan["Rustdesk"].Decision);          // ID "rustdesk" ohne winget-ID → Katalogregel zuständig
        Assert.Equal(BatchDecision.NoSettings, plan["Modern Winver"].Decision);  // Store-Paket, per Name übersprungen
        Assert.Equal(BatchDecision.IdConflict, plan["7zip"].Decision);           // gleiche ID, andere winget-ID

        var retry = BatchPlanner.Plan(PackageSources.FromOemProject(OemProject), Catalog, ["contosoburner"], Skip, retryProposals: true);
        Assert.Equal(BatchDecision.Discover, retry.Single(i => i.Package.Name == "Contoso Burner").Decision);
    }

    [Fact]
    public void Skip_list_supports_comments_and_wildcards()
    {
        var list = PackageSources.ParseSkipList("# Kommentar\nMicrosoft.DotNet.*  # Runtimes\n\n  Google.QuickShare\n");
        Assert.Equal(["Microsoft.DotNet.*", "Google.QuickShare"], list);
        Assert.Contains("Microsoft.Teams", Skip);
    }

    [Fact]
    public void Winget_export_and_errors()
    {
        var packages = PackageSources.FromWingetExport(Winget.WingetExport.Create([new("VideoLAN.VLC"), new("New.Tool")]));
        Assert.Equal(["VideoLAN.VLC", "New.Tool"], packages.Select(p => p.WingetId));
        Assert.Throws<InvalidDataException>(() => PackageSources.FromOemProject("""{ "apps": {} }"""));
    }
}
