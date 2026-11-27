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

    /// <summary>Benutzer in einem eingebundenen Offline-Image (VHDX einer heruntergefahrenen VM), nur lesend.</summary>
    OfflineImage,
}

/// <summary>
/// Quelle bzw. Ziel einer Erfassung/Anwendung: Pfade des Benutzers und Zugriff auf seinen Hive.
/// Muss nach Gebrauch freigegeben werden (entlädt ggf. Offline-Hives).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UserTarget : IDisposable
{
    private readonly List<IDisposable> _resources = [];

    internal UserTarget(UserContext context, UserTargetMode mode, WindowsRegistryBackend registry)
    {
        Context = context;
        Mode = mode;
        Registry = registry;
    }

    public UserContext Context { get; }
    public UserTargetMode Mode { get; }

    /// <summary>
    /// Bei Offline-Images: Profilpfad, wie er im Image-Windows hieß (z. B. <c>C:\Users\max</c>), während
    /// <see cref="Context"/> auf den eingebundenen Datenträger zeigt. Grundlage für das Umschreiben beim Anwenden.
    /// </summary>
    public string? OriginalProfilePath { get; init; }

    /// <summary>Bei Offline-Images: Name des Quellrechners (sonst der aktuelle Rechner).</summary>
    public string? SourceMachine { get; init; }

    /// <summary>Weitere Aufräumarbeiten beim Freigeben (Hives entladen, Image trennen, Kopien löschen).</summary>
    internal void AddResource(IDisposable resource) => _resources.Add(resource);
    public IRegistryBackend Registry { get; }

    public bool IsCurrentUser => Mode == UserTargetMode.CurrentUser;

    public static UserTarget OpenCurrent() =>
        new(UserProfiles.CurrentContext(), UserTargetMode.CurrentUser, WindowsRegistryBackend.ForCurrentUser());

    /// <summary>Öffnet einen Benutzer anhand des Namens. Für andere Benutzer sind Adminrechte nötig.</summary>
    public static UserTarget OpenUser(string userName, bool writable)
    {
        // Konto ohne Profil (noch nie angemeldet, z. B. aus der autounattend.xml): Profil beim Schreiben erzeugen
        var profile = UserProfiles.Find(userName)
            ?? (writable && LocalUsers.EnsureProfile(userName) is not null ? UserProfiles.Find(userName) : null)
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
        UserTargetMode.OfflineImage => $"{Context.UserName}@{SourceMachine} ({OriginalProfilePath} → {Context.ProfilePath})",
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
