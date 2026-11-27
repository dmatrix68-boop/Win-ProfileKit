using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using ProfileKit.Core.Paths;

namespace ProfileKit.Windows;

/// <summary>Ein lokales Benutzerprofil laut ProfileList.</summary>
public sealed record LocalProfile(string Sid, string UserName, string ProfilePath, bool IsLoaded);

[SupportedOSPlatform("windows")]
public static class UserProfiles
{
    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Alle echten Benutzerprofile (SIDs S-1-5-21-…), ohne Dienstkonten.</summary>
    public static IReadOnlyList<LocalProfile> List()
    {
        var result = new List<LocalProfile>();
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var list = hklm.OpenSubKey(ProfileListKey);
        if (list is null) return result;

        foreach (var sid in list.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !s.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)))
        {
            using var key = list.OpenSubKey(sid);
            var path = key?.GetValue("ProfileImagePath") as string;
            if (string.IsNullOrEmpty(path)) continue;
            path = Environment.ExpandEnvironmentVariables(path);
            using var loaded = Registry.Users.OpenSubKey(sid);
            result.Add(new LocalProfile(sid, ResolveName(sid) ?? Path.GetFileName(path), path, loaded is not null));
        }
        return result.OrderBy(p => p.UserName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static LocalProfile? Find(string userName) =>
        List().FirstOrDefault(p => string.Equals(p.UserName, userName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pfad des Default-Profils (Vorlage für neue Benutzer).</summary>
    public static string DefaultProfilePath()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var list = hklm.OpenSubKey(ProfileListKey);
        var value = list?.GetValue("Default") as string ?? @"%SystemDrive%\Users\Default";
        return Environment.ExpandEnvironmentVariables(value);
    }

    public static UserContext CurrentContext()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return BuildContext(Environment.UserName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), identity.User?.Value);
    }

    public static UserContext BuildContext(string userName, string profilePath, string? sid, bool isDefault = false) => new()
    {
        UserName = userName,
        Sid = sid,
        ProfilePath = profilePath,
        ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        IsDefaultProfile = isDefault,
    };

    private static string? ResolveName(string sid)
    {
        try
        {
            var account = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            var idx = account.IndexOf('\\');
            return idx >= 0 ? account[(idx + 1)..] : account;
        }
        catch (IdentityNotMappedException)
        {
            return null; // Konto gelöscht, Profil verwaist
        }
    }
}
