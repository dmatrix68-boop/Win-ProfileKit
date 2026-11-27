using ProfileKit.Core.Inventory;

namespace ProfileKit.Core.Tests;

public class OfflineImageTests
{
    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void Finds_users_from_profile_list_and_users_folder()
    {
        using var root = new TempRoot();
        var image = Path.Combine(root.Path, "F");
        Touch(Path.Combine(image, "Windows", "System32", "config", "SOFTWARE"));
        Touch(Path.Combine(image, "Users", "max", "NTUSER.DAT"));
        Touch(Path.Combine(image, "Users", "test1", "NTUSER.DAT"));
        Touch(Path.Combine(image, "Users", "Default", "NTUSER.DAT"));       // Systemprofil
        Directory.CreateDirectory(Path.Combine(image, "Users", "Public"));
        Directory.CreateDirectory(Path.Combine(image, "Users", "ohneHive"));
        Touch(Path.Combine(image, "Profile", "dev", "NTUSER.DAT"));          // Profil außerhalb von \Users

        var profileList = new Dictionary<string, string>
        {
            ["S-1-5-21-1-1001"] = @"C:\Users\max",
            ["S-1-5-21-1-1002"] = @"%SystemDrive%\Profile\dev",
            ["S-1-5-21-1-1003"] = @"C:\Users\geloescht",                     // ohne Ordner im Image
        };

        Assert.True(OfflineImageLayout.IsWindowsRoot(image));
        var users = OfflineImageLayout.FindUsers(image, profileList);
        Assert.Equal(["dev", "max", "test1"], users.Select(u => u.UserName));

        var max = users.Single(u => u.UserName == "max");
        Assert.Equal(@"C:\Users\max", max.OriginalProfilePath);
        Assert.Equal("S-1-5-21-1-1001", max.Sid);
        Assert.True(File.Exists(Path.Combine(max.MountedProfilePath, "NTUSER.DAT")));

        Assert.Equal(@"C:\Profile\dev", users.Single(u => u.UserName == "dev").OriginalProfilePath);
        var test1 = users.Single(u => u.UserName == "test1");
        Assert.Equal(@"C:\Users\test1", test1.OriginalProfilePath);
        Assert.Null(test1.Sid);
    }

    [Fact]
    public void Detects_hyperv_checkpoints()
    {
        using var root = new TempRoot();
        var vhdx = Path.Combine(root.Path, "Test-VM.vhdx");
        Touch(vhdx);
        Touch(Path.Combine(root.Path, "Test-VM_6F2A1B3C-1234-4D5E-8F90-ABCDEF012345.avhdx"));
        Touch(Path.Combine(root.Path, "Andere-VM_6F2A1B3C-1234-4D5E-8F90-ABCDEF012345.avhdx"));

        Assert.Single(OfflineImageLayout.CheckpointFiles(vhdx));
        Assert.Empty(OfflineImageLayout.CheckpointFiles(Path.Combine(root.Path, "Test-VM_6F2A1B3C-1234-4D5E-8F90-ABCDEF012345.avhdx")));
        Assert.False(OfflineImageLayout.IsWindowsRoot(root.Path));
    }
}
