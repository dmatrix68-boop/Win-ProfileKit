using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;

namespace ProfileKit.Windows;

public enum UserTargetMode
{
    /// <summary>Der Benutzer, unter dem das Tool läuft.</summary>
    CurrentUser,

    /// <summary>Anderer Benutzer, gerade angemeldet – Hive liegt unter HKU\&lt;SID&gt;.</summary>
    LoggedOnUser,

    /// <summary>Anderer Benutzer, nicht angemeldet – NTUSER.DAT offline geladen.</summary>
    OfflineUser,

    /// <summary>Default-Profil – gilt für alle künftig angelegten Benutzer.</summary>
    DefaultProfile,
}

/// <summary>
/// Quelle bzw. Ziel einer Erfassung/Anwendung: Pfade des Benutzers und Zugriff auf seinen Hive.
/// Muss nach Gebrauch freigegeben werden (entlädt ggf. Offline-Hives).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UserTarget : IDisposable
{
    private readonly List<IDisposable> _resources = [];

    private UserTarget(UserContext context, UserTargetMode mode, WindowsRegistryBackend registry)
    {
        Context = context;
        Mode = mode;
        Registry = registry;
    }

    public UserContext Context { get; }
    public UserTargetMode Mode { get; }
    public IRegistryBackend Registry { get; }

    public bool IsCurrentUser => Mode == UserTargetMode.CurrentUser;

    public static UserTarget OpenCurrent() =>
        new(UserProfiles.CurrentContext(), UserTargetMode.CurrentUser, WindowsRegistryBackend.ForCurrentUser());

    /// <summary>Öffnet einen Benutzer anhand des Namens. Für andere Benutzer sind Adminrechte nötig.</summary>
    public static UserTarget OpenUser(string userName, bool writable)
    {
        var profile = UserProfiles.Find(userName)
            ?? throw new ArgumentException($"Benutzer '{userName}' hat kein Profil auf diesem Rechner.", nameof(userName));

        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (string.Equals(identity.User?.Value, profile.Sid, StringComparison.OrdinalIgnoreCase))
                return OpenCurrent();
        }

        var context = UserProfiles.BuildContext(profile.UserName, profile.ProfilePath, profile.Sid);
        if (profile.IsLoaded)
        {
            var root = Microsoft.Win32.Registry.Users.OpenSubKey(profile.Sid, writable)
                ?? throw new UnauthorizedAccessException($"Kein Zugriff auf HKU\\{profile.Sid}.");
            return new UserTarget(context, UserTargetMode.LoggedOnUser, new WindowsRegistryBackend(root, ownsUserRoot: true));
        }

        return OpenOffline(context, UserTargetMode.OfflineUser, writable);
    }

    public static UserTarget OpenDefaultProfile(bool writable)
    {
        var path = UserProfiles.DefaultProfilePath();
        var context = UserProfiles.BuildContext("Default", path, sid: null, isDefault: true);
        return OpenOffline(context, UserTargetMode.DefaultProfile, writable);
    }

    private static UserTarget OpenOffline(UserContext context, UserTargetMode mode, bool writable)
    {
        if (!UserProfiles.IsAdministrator())
            throw new UnauthorizedAccessException("Für Offline-Hives sind Administratorrechte nötig.");

        var ntuser = Path.Combine(context.ProfilePath, "NTUSER.DAT");
        var userMount = HiveMount.Load(ntuser);
        HiveMount? classesMount = null;
        RegistryKey? classesRoot = null;
        try
        {
            var usrClass = Path.Combine(context.LocalAppData, "Microsoft", "Windows", "UsrClass.dat");
            if (File.Exists(usrClass))
            {
                classesMount = HiveMount.Load(usrClass, userMount.MountName + "_Classes");
                classesRoot = classesMount.OpenRoot(writable);
            }
            var userRoot = userMount.OpenRoot(writable);
            var target = new UserTarget(context, mode, new WindowsRegistryBackend(userRoot, classesRoot, ownsUserRoot: true));
            // Reihenfolge beim Freigeben: erst Registry-Handles, dann Hives entladen
            target._resources.Add(classesMount ?? (IDisposable)new NoOp());
            target._resources.Add(userMount);
            return target;
        }
        catch
        {
            classesRoot?.Dispose();
            classesMount?.Dispose();
            userMount.Dispose();
            throw;
        }
    }

    public string Describe() => Mode switch
    {
        UserTargetMode.DefaultProfile => $"Default ({Context.ProfilePath})",
        _ => $"{Context.UserName} ({Context.ProfilePath})",
    };

    public void Dispose()
    {
        ((WindowsRegistryBackend)Registry).Dispose();
        foreach (var r in _resources) r.Dispose();
        _resources.Clear();
    }

    private sealed class NoOp : IDisposable
    {
        public void Dispose() { }
    }
}
