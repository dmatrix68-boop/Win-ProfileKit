using ProfileKit.Core.Packaging;
using ProfileKit.Core.Registry;

namespace ProfileKit.Core.Tests;

public class RegistryTests
{
    [Theory]
    [InlineData(@"HKEY_CURRENT_USER\Software\X\", "HKCU", @"Software\X")]
    [InlineData(@"hklm\SOFTWARE\Y", "HKLM", @"SOFTWARE\Y")]
    [InlineData("HKCU", "HKCU", "")]
    public void Split_normalizes_roots(string key, string root, string sub) =>
        Assert.Equal((root, sub), RegistryPath.Split(key));

    [Fact]
    public void Unsupported_root_throws() =>
        Assert.Throws<ArgumentException>(() => RegistryPath.Split(@"HKU\S-1-5-21\Software"));

    [Fact]
    public void Memory_backend_reads_recursively_and_selectively()
    {
        var reg = new MemoryRegistryBackend();
        reg.Write(RegistryValue.FromDWord(@"HKCU\Software\A", "x", 1));
        reg.Write(RegistryValue.FromDWord(@"HKCU\Software\A\B", "y", 2));
        reg.Write(RegistryValue.FromDWord(@"HKCU\Software\AB", "z", 3));

        Assert.Equal(2, reg.Read(@"HKCU\Software\A", [], recurse: true).Count);
        Assert.Single(reg.Read(@"HKCU\Software\A", [], recurse: false));
        Assert.Single(reg.Read(@"hkey_current_user\software\a", ["X", "fehlt"], recurse: true));
    }

    [Fact]
    public void Values_round_trip_through_json()
    {
        using var root = new TempRoot();
        var file = Path.Combine(root.Path, "r.json");
        var values = new List<RegistryValue>
        {
            RegistryValue.FromString(@"HKCU\K", "", "Standardwert"),
            RegistryValue.FromString(@"HKCU\K", "e", @"%USERPROFILE%\x", expand: true),
            RegistryValue.FromDWord(@"HKCU\K", "d", -1),
            RegistryValue.FromQWord(@"HKCU\K", "q", long.MaxValue),
            RegistryValue.FromBinary(@"HKCU\K", "b", [0, 1, 255]),
            RegistryValue.FromMulti(@"HKCU\K", "m", ["a", @"C:\Users\x"]),
        };
        Json.Write(file, values);
        var back = Json.Read<List<RegistryValue>>(file);

        Assert.Equal(RegValueKind.ExpandString, back[1].Kind);
        Assert.Equal(uint.MaxValue, back[2].Number);
        Assert.Equal(long.MaxValue, back[3].Number);
        Assert.Equal(new byte[] { 0, 1, 255 }, back[4].GetBytes());
        Assert.Equal(["a", @"C:\Users\x"], back[5].Lines!);
        Assert.Contains("\"kind\": \"DWord\"", File.ReadAllText(file));
    }

    [Fact]
    public void MapStrings_only_touches_string_kinds()
    {
        var map = (string s) => s.ToUpperInvariant();
        Assert.Equal("ABC", RegistryValue.FromString("HKCU\\K", "n", "abc").MapStrings(map).Text);
        Assert.Equal(["A"], RegistryValue.FromMulti("HKCU\\K", "n", ["a"]).MapStrings(map).Lines!);
        Assert.Equal(5, RegistryValue.FromDWord("HKCU\\K", "n", 5).MapStrings(map).Number);
    }
}
