using System.Text;
using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Tests;

public class CaptureApplyTests
{
    private static ModuleRule Rule() => new()
    {
        Id = "tool",
        DisplayName = "Tool",
        WingetId = "Vendor.Tool",
        Files =
        [
            new FileRule { Path = @"%APPDATA%\Tool" },
            new FileRule { Path = @"%PROGRAMFILES%\Tool\license.key", Kind = FileKind.Keyfile },
            new FileRule { Path = @"%LOCALAPPDATA%\Fehlt" },
        ],
        Registry = [new RegistryRule { Key = @"HKCU\Software\Tool" }],
        Excludes = ["*.log", @"cache\**"],
    };

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Capture_then_apply_to_other_user_rewrites_paths()
    {
        using var root = new TempRoot();
        var alt = root.User("alt");
        var neu = root.User("neu");

        Write(PathTokens.Expand(@"%APPDATA%\Tool\config.ini", alt), $"last={alt.ProfilePath}{Path.DirectorySeparatorChar}Documents");
        Write(PathTokens.Expand(@"%APPDATA%\Tool\sub\data.json", alt), "{}");
        Write(PathTokens.Expand(@"%APPDATA%\Tool\run.log", alt), "log");
        Write(PathTokens.Expand(@"%APPDATA%\Tool\cache\big.bin", alt), "cache");
        Write(PathTokens.Expand(@"%PROGRAMFILES%\Tool\license.key", alt), "KEY-123");

        var sourceReg = new MemoryRegistryBackend();
        sourceReg.Write(RegistryValue.FromString(@"HKCU\Software\Tool", "LastDir", alt.ProfilePath + @"\Downloads"));
        sourceReg.Write(RegistryValue.FromDWord(@"HKCU\Software\Tool\Window", "Width", 800));
        sourceReg.Write(RegistryValue.FromString(@"HKCU\Software\Other", "X", "nicht erfassen"));

        var package = new ProfilePackage(Path.Combine(root.Path, "profile"));
        var capture = new CaptureService().CaptureModule(package, Rule(), alt, sourceReg, "1.2.3");

        Assert.Equal(3, capture.Entry.FileCount);       // config.ini, sub/data.json, license.key
        Assert.Equal(2, capture.Entry.RegistryValueCount);
        Assert.True(capture.Entry.ContainsKeyfiles);
        Assert.Empty(capture.Warnings);                 // fehlender optionaler Pfad ist kein Fehler
        Assert.False(File.Exists(package.PackagedPathFor("tool", @"%APPDATA%\Tool\run.log")));
        Assert.False(File.Exists(package.PackagedPathFor("tool", @"%APPDATA%\Tool\cache\big.bin")));

        var source = new SourceInfo { Machine = "VM1", UserName = "alt", ProfilePath = alt.ProfilePath };
        package.WriteManifest(new ProfileManifest { Name = "Test", Source = source, Modules = [capture.Entry] });

        // vorhandene Datei beim Ziel → muss gesichert werden
        Write(PathTokens.Expand(@"%APPDATA%\Tool\config.ini", neu), "alt-inhalt-von-neu");
        var targetReg = new MemoryRegistryBackend();
        targetReg.Write(RegistryValue.FromDWord(@"HKCU\Software\Tool\Window", "Width", 1024));

        var backup = Path.Combine(root.Path, "backup");
        var report = new ApplyService().ApplyModule(package, "tool", source, neu, targetReg, new ApplyOptions { BackupDirectory = backup });

        Assert.Empty(report.Warnings);
        Assert.Equal(3, report.FilesWritten);
        Assert.Equal(2, report.ValuesWritten);

        var config = File.ReadAllText(PathTokens.Expand(@"%APPDATA%\Tool\config.ini", neu));
        Assert.Equal($"last={neu.ProfilePath}{Path.DirectorySeparatorChar}Documents", config);
        Assert.True(File.Exists(PathTokens.Expand(@"%APPDATA%\Tool\sub\data.json", neu)));
        Assert.Equal(neu.ProfilePath + @"\Downloads", targetReg.Get(@"HKCU\Software\Tool", "LastDir")!.Text);
        Assert.Equal(800, targetReg.Get(@"HKCU\Software\Tool\Window", "Width")!.Number);
        Assert.Null(targetReg.Get(@"HKCU\Software\Other", "X"));

        var backedUp = Directory.GetFiles(backup, "config.ini", SearchOption.AllDirectories).Single();
        Assert.Equal("alt-inhalt-von-neu", File.ReadAllText(backedUp));
        var regBackup = Json.Read<List<ApplyService.RegistryBackupEntry>>(Path.Combine(backup, "tool", "registry-before.json"));
        Assert.Contains(regBackup, e => e.Name == "Width" && e.Previous!.Number == 1024);
        Assert.Contains(regBackup, e => e.Name == "LastDir" && e.Previous is null);
    }

    [Fact]
    public void Dry_run_changes_nothing()
    {
        using var root = new TempRoot();
        var alt = root.User("alt");
        var neu = root.User("neu");
        Write(PathTokens.Expand(@"%APPDATA%\Tool\config.ini", alt), "x");
        var reg = new MemoryRegistryBackend();
        reg.Write(RegistryValue.FromDWord(@"HKCU\Software\Tool", "A", 1));

        var package = new ProfilePackage(Path.Combine(root.Path, "p"));
        var entry = new CaptureService().CaptureModule(package, Rule(), alt, reg).Entry;
        var source = new SourceInfo { Machine = "m", UserName = "alt", ProfilePath = alt.ProfilePath };

        var targetReg = new MemoryRegistryBackend();
        var report = new ApplyService().ApplyModule(package, entry.Id, source, neu, targetReg, new ApplyOptions { DryRun = true });

        Assert.Equal(1, report.FilesWritten);
        Assert.Equal(1, report.ValuesWritten);
        Assert.False(Directory.Exists(neu.ProfilePath));
        Assert.Empty(targetReg.All);
    }

    [Fact]
    public void Machine_scope_can_be_skipped()
    {
        using var root = new TempRoot();
        var alt = root.User("alt");
        Write(PathTokens.Expand(@"%PROGRAMFILES%\Tool\license.key", alt), "KEY");
        var package = new ProfilePackage(Path.Combine(root.Path, "p"));
        var entry = new CaptureService().CaptureModule(package, Rule(), alt, new MemoryRegistryBackend()).Entry;
        var source = new SourceInfo { Machine = "m", UserName = "alt", ProfilePath = alt.ProfilePath };

        var report = new ApplyService().ApplyModule(package, entry.Id, source, root.User("neu"), new MemoryRegistryBackend(),
            new ApplyOptions { IncludeMachineScope = false });
        Assert.Equal(0, report.FilesWritten);
        Assert.Contains(report.Actions, a => a.Kind == ApplyActionKind.Skip);
    }

    [Fact]
    public void Archive_round_trip()
    {
        using var root = new TempRoot();
        var alt = root.User("alt");
        Write(PathTokens.Expand(@"%APPDATA%\Tool\ü.ini", alt), "inhalt");
        var package = new ProfilePackage(Path.Combine(root.Path, "Profil"));
        var entry = new CaptureService().CaptureModule(package, Rule(), alt, new MemoryRegistryBackend()).Entry;
        package.WriteManifest(new ProfileManifest
        {
            Name = "Profil",
            Source = new SourceInfo { Machine = "m", UserName = "alt", ProfilePath = alt.ProfilePath },
            Modules = [entry],
        });

        var archive = Path.Combine(root.Path, "Profil" + ProfilePackage.ArchiveExtension);
        package.ExportArchive(archive);
        var opened = ProfilePackage.Open(archive);
        Assert.Equal("Profil", opened.ReadManifest().Name);
        Assert.Equal("inhalt", File.ReadAllText(opened.PackagedPathFor("tool", @"%APPDATA%\Tool\ü.ini")));
        Assert.Equal("Vendor.Tool", opened.ReadModule("tool").WingetId);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("Großbuchstaben")]
    [InlineData("")]
    public void Invalid_module_ids_are_rejected(string id) =>
        Assert.Throws<ArgumentException>(() => ProfilePackage.ValidateId(id));
}
