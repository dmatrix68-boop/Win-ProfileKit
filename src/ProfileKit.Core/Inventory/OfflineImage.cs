using ProfileKit.Core.Paths;

namespace ProfileKit.Core.Inventory;

/// <summary>Ein Benutzerprofil in einem eingebundenen Offline-Image.</summary>
/// <param name="MountedProfilePath">Pfad auf dem eingebundenen Datenträger, z. B. <c>F:\Users\max</c>.</param>
/// <param name="OriginalProfilePath">Pfad im Image-Windows, z. B. <c>C:\Users\max</c>.</param>
public sealed record OfflineUser(string UserName, string MountedProfilePath, string OriginalProfilePath, string? Sid);

/// <summary>Aufbau eines Windows-Images auf einem eingebundenen Datenträger.</summary>
public static class OfflineImageLayout
{
    private static readonly HashSet<string> SystemProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default", "Default User", "Public", "All Users", "defaultuser0", "defaultuser100000", "WDAGUtilityAccount",
    };

    public static string SoftwareHive(string root) => PathText.Join(root, "Windows", "System32", "config", "SOFTWARE");

    /// <summary>Ist das die Windows-Partition des Images?</summary>
    public static bool IsWindowsRoot(string root) => File.Exists(SoftwareHive(root));

    /// <summary>
    /// Benutzer mit NTUSER.DAT. <paramref name="profileList"/> (SID → ProfileImagePath aus dem SOFTWARE-Hive des Images)
    /// liefert die echten Pfade; ohne Eintrag wird <c>C:\Users\&lt;Ordner&gt;</c> angenommen.
    /// </summary>
    public static IReadOnlyList<OfflineUser> FindUsers(string root, IReadOnlyDictionary<string, string>? profileList)
    {
        var users = new Dictionary<string, OfflineUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sid, imagePath) in profileList ?? new Dictionary<string, string>())
        {
            var original = ExpandSystemDrive(imagePath);
            var relative = RelativeToDrive(original);
            if (relative is null) continue;
            var mounted = PathText.Join(root, relative);
            var name = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (name is null || SystemProfiles.Contains(name) || !File.Exists(PathText.Join(mounted, "NTUSER.DAT"))) continue;
            users[mounted] = new OfflineUser(name, mounted, original, sid);
        }

        var usersDir = PathText.Join(root, "Users");
        if (Directory.Exists(usersDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(usersDir))
            {
                var name = Path.GetFileName(dir);
                if (SystemProfiles.Contains(name) || users.ContainsKey(dir) || !File.Exists(Path.Combine(dir, "NTUSER.DAT"))) continue;
                users[dir] = new OfflineUser(name, dir, $@"C:\Users\{name}", null);
            }
        }
        return users.Values.OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Hyper-V-Prüfpunkte neben der VHDX (<c>&lt;Name&gt;_&lt;GUID&gt;.avhdx</c>) – dann enthält die VHDX nicht den aktuellen Stand.</summary>
    public static IReadOnlyList<string> CheckpointFiles(string vhdxPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(vhdxPath));
        if (dir is null || !Directory.Exists(dir) || vhdxPath.EndsWith(".avhdx", StringComparison.OrdinalIgnoreCase)) return [];
        var stem = Path.GetFileNameWithoutExtension(vhdxPath);
        return Directory.EnumerateFiles(dir, stem + "_*.avhdx").Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ExpandSystemDrive(string path) =>
        path.Replace("%SystemDrive%", "C:", StringComparison.OrdinalIgnoreCase).Trim();

    /// <summary><c>C:\Users\max</c> → <c>Users\max</c>; null, wenn der Pfad kein Laufwerk hat.</summary>
    private static string? RelativeToDrive(string path)
    {
        var p = PathText.NormalizeWindows(path);
        return p.Length >= 3 && p[1] == ':' && p[2] == '\\' ? p[3..] : null;
    }
}
