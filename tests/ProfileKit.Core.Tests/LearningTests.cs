using ProfileKit.Core.Catalog;
using ProfileKit.Core.Learning;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Tests;

public class LearningTests
{
    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string P(UserContext u, string tokenized) => PathTokens.Expand(tokenized, u);

    [Fact]
    public void End_to_end_learns_files_and_registry_of_one_program()
    {
        using var root = new TempRoot();
        var user = root.User("max");
        var reg = new MemoryRegistryBackend();

        // Ausgangszustand inkl. Rauschen, das ignoriert werden muss
        Write(P(user, @"%APPDATA%\Tool\config.ini"), "a=1");
        Write(P(user, @"%APPDATA%\Other\x.ini"), "unverändert");
        Write(P(user, @"%USERPROFILE%\Documents\brief.docx"), "kein Konfig");
        reg.Write(RegistryValue.FromDWord(@"HKCU\Software\Vendor\Tool", "Width", 800));
        reg.Write(RegistryValue.FromString(@"HKCU\Control Panel\Mouse", "MouseSensitivity", "10"));

        var service = new SnapshotService();
        var before = service.Take(user, reg, LearnScope.Default);
        Assert.Contains(@"%APPDATA%\Tool\config.ini", before.Files.Keys);
        Assert.DoesNotContain(before.Files.Keys, k => k.Contains("Documents"));

        // Benutzer ändert Einstellungen …
        Write(P(user, @"%APPDATA%\Tool\config.ini"), "a=2-länger");
        Write(P(user, @"%APPDATA%\Tool\profiles\default.json"), "{}");
        Write(P(user, @"%APPDATA%\Tool\cache\blob.bin"), "rauschen");
        Write(P(user, @"%APPDATA%\Tool\run.log"), "rauschen");
        Write(P(user, @"%LOCALAPPDATA%\Temp\x.tmp"), "rauschen");
        Write(P(user, @"%USERPROFILE%\.toolrc"), "dotfile");
        reg.Write(RegistryValue.FromDWord(@"HKCU\Software\Vendor\Tool", "Width", 1024));
        reg.Write(RegistryValue.FromString(@"HKCU\Software\Vendor\Tool\Recent", "File1", "x"));
        reg.Write(RegistryValue.FromString(@"HKCU\Control Panel\Mouse", "MouseSensitivity", "14"));
        reg.Write(RegistryValue.FromBinary(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{GUID}\Count", "x", [1]));

        // Session über JSON speichern/laden, wie zwischen learn start und learn stop
        var sessionFile = Path.Combine(root.Path, "session.json");
        new LearnSession { Scope = LearnScope.Default, Before = before }.Save(sessionFile);
        var session = LearnSession.TryLoad(sessionFile)!;
        var after = service.Take(user, reg, session.Scope);
        var diff = SnapshotDiff.Compare(session.Before, after);

        Assert.Equal(
            [@"%APPDATA%\Tool\config.ini", @"%APPDATA%\Tool\profiles\default.json", @"%USERPROFILE%\.toolrc"],
            diff.Files.Select(f => f.Path));
        Assert.Equal(ChangeKind.Modified, diff.Files[0].Kind);
        Assert.Equal(ChangeKind.Added, diff.Files[1].Kind);
        Assert.Equal(3, diff.Registry.Count);   // UserAssist ist Rauschen

        var rule = RuleProposer.Propose(diff, "tool", "Tool", "Vendor.Tool");
        RuleCatalog.Validate(rule);
        Assert.Equal([@"%APPDATA%\Tool", @"%USERPROFILE%\.toolrc"], rule.Files.Select(f => f.Path));
        var vendor = Assert.Single(rule.Registry, r => r.Key.StartsWith(@"HKCU\Software\Vendor"));
        Assert.Equal(@"HKCU\Software\Vendor\Tool", vendor.Key);
        Assert.True(vendor.Recurse);
        var mouse = Assert.Single(rule.Registry, r => r.Key == @"HKCU\Control Panel\Mouse");
        Assert.Equal(["MouseSensitivity"], mouse.Values);
        Assert.Equal(Model.RuleOrigin.LearnMode, rule.Origin);

        // Die gelernte Regel lässt sich direkt erfassen
        var package = new ProfilePackage(Path.Combine(root.Path, "profil"));
        var captured = new CaptureService().CaptureModule(package, rule, user, reg);
        Assert.Equal(3, captured.Entry.FileCount);          // config.ini, profiles\default.json, .toolrc
        Assert.Equal(3, captured.Entry.RegistryValueCount); // Width, Recent\File1, MouseSensitivity
    }

    [Fact]
    public void Removed_entries_do_not_create_rules()
    {
        var diff = new SnapshotDiff([new FileChange(@"%APPDATA%\Weg\x.ini", ChangeKind.Removed)],
            [new RegistryChange(@"HKCU\Software\Weg", "x", ChangeKind.Removed)]);
        var rule = RuleProposer.Propose(diff, "weg", "Weg", null);
        Assert.Empty(rule.Files);
        Assert.Empty(rule.Registry);
    }

    [Theory]
    [InlineData(new[] { @"%LOCALAPPDATA%\Packages\Vendor.App_abc\LocalState\settings.json" }, @"%LOCALAPPDATA%\Packages\Vendor.App_abc\LocalState")]
    [InlineData(new[] { @"%LOCALAPPDATA%\Microsoft\PowerToys\a\settings.json", @"%LOCALAPPDATA%\Microsoft\PowerToys\b\settings.json" }, @"%LOCALAPPDATA%\Microsoft\PowerToys")]
    [InlineData(new[] { @"%APPDATA%\Vendor\ProductA\x.ini", @"%APPDATA%\Vendor\ProductB\y.ini" }, @"%APPDATA%\Vendor")]
    [InlineData(new[] { @"%APPDATA%\settings.ini" }, @"%APPDATA%\settings.ini")]
    [InlineData(new[] { @"%USERPROFILE%\.config\tool\conf" }, @"%USERPROFILE%\.config\tool")]
    public void File_grouping(string[] paths, string expected) =>
        Assert.Equal(expected, Assert.Single(RuleProposer.ProposeFiles(paths)).Path);

    [Fact]
    public void Microsoft_products_get_own_key_but_windows_keys_only_values()
    {
        var changes = new List<RegistryChange>
        {
            new(@"HKCU\Software\Microsoft\PowerToys\Run", "Hotkey", ChangeKind.Modified),
            new(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", ChangeKind.Modified),
            new(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", ChangeKind.Modified),
            new(@"HKCU\Software\Classes\.txt", "", ChangeKind.Added),
        };
        var rules = RuleProposer.ProposeRegistry(changes);

        Assert.Contains(rules, r => r.Key == @"HKCU\Software\Microsoft\PowerToys\Run" && r.Recurse && r.Values.Count == 0);
        var adv = Assert.Single(rules, r => r.Key.EndsWith("Advanced"));
        Assert.Equal(["Hidden", "HideFileExt"], adv.Values);
        Assert.False(adv.Recurse);
        Assert.Contains(rules, r => r.Key == @"HKCU\Software\Classes\.txt" && r.Values.SequenceEqual([""]));
    }

    [Fact]
    public void Machine_scope_and_extra_noise()
    {
        var scope = LearnScope.Default.WithMachineScope().WithExtraNoise([@"%APPDATA%\Laut\**", @"HKCU\Software\Laut\**"]);
        Assert.Contains("%PROGRAMFILES%", scope.FileRoots);
        Assert.Contains(@"HKLM\SOFTWARE", scope.RegistryRoots);
        Assert.Contains(@"%APPDATA%\Laut\**", scope.FileNoise);
        Assert.Contains(@"HKCU\Software\Laut\**", scope.RegistryNoise);
        Assert.DoesNotContain(@"HKCU\Software\Laut\**", scope.FileNoise);
    }

    [Fact]
    public void Fingerprint_detects_data_changes_without_storing_content()
    {
        var a = SnapshotService.Fingerprint(RegistryValue.FromString(@"HKCU\K", "p", "geheim"));
        var b = SnapshotService.Fingerprint(RegistryValue.FromString(@"HKCU\K", "p", "geheim2"));
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("geheim", a);
        Assert.StartsWith("String:", a);
        Assert.NotEqual(SnapshotService.Fingerprint(RegistryValue.FromDWord(@"HKCU\K", "d", 1)),
                        SnapshotService.Fingerprint(RegistryValue.FromQWord(@"HKCU\K", "d", 1)));
    }
}
