using ProfileKit.Core.Catalog;
using ProfileKit.Core.Inventory;
using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Registry;
using ProfileKit.Core.Winget;

namespace ProfileKit.Core.Tests;

public class InventoryTests
{
    private const string U = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static MemoryRegistryBackend Registry()
    {
        var reg = new MemoryRegistryBackend();
        void App(string key, string name, string? location = null, bool system = false, bool child = false)
        {
            reg.Write(RegistryValue.FromString($@"{U}\{key}", "DisplayName", name));
            if (location is not null) reg.Write(RegistryValue.FromString($@"{U}\{key}", "InstallLocation", location));
            if (system) reg.Write(RegistryValue.FromDWord($@"{U}\{key}", "SystemComponent", 1));
            if (child) reg.Write(RegistryValue.FromString($@"{U}\{key}", "ParentKeyName", "OperatingSystem"));
        }
        App("Totalcmd64", "Total Commander 64-bit (x64) 11.58", @"C:\Program Files\totalcmd\");
        App("Everything", "Everything 1.5.0.1409a (x64)");
        App("{GUID-1}", "Contoso Unbekannt 3.0");
        App("{GUID-2}", "Microsoft Edge Update", system: true);
        App("KB123", "Update für Irgendwas", child: true);
        reg.Write(RegistryValue.FromString(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Ditto", "DisplayName", "Ditto"));
        return reg;
    }

    private static RuleCatalog Catalog => RuleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "rules"));

    [Fact]
    public void Reads_uninstall_entries_without_system_components_and_updates()
    {
        var entries = UninstallInventory.Read(Registry(), Win.User("max"));
        Assert.Equal(["Contoso Unbekannt 3.0", "Ditto", "Everything 1.5.0.1409a (x64)", "Total Commander 64-bit (x64) 11.58"], entries.Select(e => e.Name));
        Assert.Equal(@"%PROGRAMFILES%\totalcmd", entries.Single(e => e.Name.StartsWith("Total")).InstallLocation);
    }

    [Theory]
    [InlineData("Everything 1.5 (Alpha)", "Everything 1.5")]
    [InlineData("Windows: Maus / Mouse", "Windows: Maus / Mouse")]
    [InlineData("WizTree", "WizTree")]
    public void Normalizes_display_names(string name, string expected) => Assert.Equal(expected, InventoryMatcher.NormalizeName(name));

    [Fact]
    public void Matches_by_winget_id_or_program_name()
    {
        var programs = UninstallInventory.Read(Registry());
        IReadOnlyList<InstalledPackage> winget = [new("7zip.7zip")];
        var rules = InventoryMatcher.RulesForInstalled(Catalog.Rules, winget, programs).Select(r => r.Id).Order().ToList();
        Assert.Equal(["7zip", "ditto", "everything", "totalcommander"], rules);   // 7-Zip über winget, Rest über den Namen

        var uncovered = InventoryMatcher.Uncovered(programs, Catalog.Rules);
        Assert.Equal(["Contoso Unbekannt 3.0"], uncovered.Select(u => u.Name));
    }

    [Fact]
    public void Compare_classifies_modules_for_the_target()
    {
        var modules = new List<ModuleEntry>
        {
            new() { Id = "windows.mouse", DisplayName = "Maus" },
            new() { Id = "7zip", DisplayName = "7-Zip", WingetId = "7zip.7zip" },
            new() { Id = "totalcommander", DisplayName = "Total Commander", WingetId = "Ghisler.TotalCommander" },
            new() { Id = "vlc", DisplayName = "VLC media player", WingetId = "VideoLAN.VLC" },
            new() { Id = "portable", DisplayName = "Portable Tool" },
        };
        var result = InventoryMatcher.Compare(modules, [new InstalledPackage("7ZIP.7ZIP")], UninstallInventory.Read(Registry()))
            .ToDictionary(p => p.Module.Id, p => p.Presence);

        Assert.Equal(Presence.Windows, result["windows.mouse"]);
        Assert.Equal(Presence.Installed, result["7zip"]);
        Assert.Equal(Presence.Installed, result["totalcommander"]);   // per Programmname, obwohl winget es nicht kennt
        Assert.Equal(Presence.Installable, result["vlc"]);
        Assert.Equal(Presence.Missing, result["portable"]);
    }

    [Fact]
    public void Inventory_is_written_next_to_the_profile_with_winget_export()
    {
        using var root = new TempRoot();
        var package = new ProfilePackage(Path.Combine(root.Path, "VM1_max"));
        Directory.CreateDirectory(package.Root);
        new MachineInventory
        {
            Machine = "VM1",
            UserName = "max",
            Winget = [new("7zip.7zip", "26.02"), new("VideoLAN.VLC")],
            Programs = UninstallInventory.Read(Registry()).ToList(),
        }.Write(package);

        var back = MachineInventory.TryRead(package)!;
        Assert.Equal("VM1", back.Machine);
        Assert.Equal(4, back.Programs.Count);
        var export = WingetExport.Parse(File.ReadAllText(Path.Combine(package.Root, MachineInventory.WingetExportFileName)));
        Assert.Equal(["7zip.7zip", "VideoLAN.VLC"], export.Select(p => p.Id));
        Assert.Null(MachineInventory.TryRead(new ProfilePackage(Path.Combine(root.Path, "leer"))));
    }
}
