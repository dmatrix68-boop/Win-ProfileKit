using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Tests;

public class SettingsProbeTests
{
    private static readonly ModuleRule Rule = new()
    {
        Id = "tool",
        DisplayName = "Tool",
        Files = [new FileRule { Path = @"%APPDATA%\Tool" }],
        Registry = [new RegistryRule { Key = @"HKCU\Software\Tool" }, new RegistryRule { Key = @"HKCU\Software\Other", Values = ["Mode"] }],
    };

    [Fact]
    public void Nothing_present_means_no_settings()
    {
        using var root = new TempRoot();
        Assert.False(SettingsProbe.HasSettings(Rule, root.User("a"), new MemoryRegistryBackend()));
    }

    [Fact]
    public void File_or_folder_counts_per_user()
    {
        using var root = new TempRoot();
        var a = root.User("a");
        Directory.CreateDirectory(PathTokens.Expand(@"%APPDATA%\Tool", a));
        Assert.True(SettingsProbe.HasSettings(Rule, a, registry: null));
        Assert.False(SettingsProbe.HasSettings(Rule, root.User("b"), registry: null));
    }

    [Fact]
    public void Registry_key_or_listed_value_counts()
    {
        using var root = new TempRoot();
        var user = root.User("a");
        var reg = new MemoryRegistryBackend();
        reg.Write(RegistryValue.FromString(@"HKCU\Software\Other", "Unrelated", "x"));
        Assert.False(SettingsProbe.HasSettings(Rule, user, reg));
        reg.Write(RegistryValue.FromDWord(@"HKCU\Software\Other", "Mode", 1));
        Assert.True(SettingsProbe.HasSettings(Rule, user, reg));

        var reg2 = new MemoryRegistryBackend();
        reg2.Write(RegistryValue.FromDWord(@"HKCU\Software\Tool\Sub", "X", 1));
        Assert.True(SettingsProbe.HasSettings(Rule, user, reg2));
    }

    [Fact]
    public void DeleteDirectory_removes_read_only_files()
    {
        using var root = new TempRoot();
        var dir = Path.Combine(root.Path, "module");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "module.json");
        File.WriteAllText(file, "{}");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        FileSystem.DeleteDirectory(dir);
        Assert.False(Directory.Exists(dir));
    }
}
