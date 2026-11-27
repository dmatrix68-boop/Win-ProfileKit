using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ProfileKit.Windows;

/// <summary>
/// Lädt eine Hive-Datei (NTUSER.DAT, UsrClass.dat) offline unter HKEY_USERS\&lt;Name&gt; und
/// entlädt sie beim Dispose wieder. Erfordert Administratorrechte.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HiveMount : IDisposable
{
    private bool _disposed;

    private HiveMount(string mountName, string file)
    {
        MountName = mountName;
        File = file;
    }

    public string MountName { get; }
    public string File { get; }

    public static HiveMount Load(string hiveFile, string? mountName = null)
    {
        if (!System.IO.File.Exists(hiveFile))
            throw new FileNotFoundException("Hive-Datei nicht gefunden.", hiveFile);

        Privileges.Enable("SeBackupPrivilege");
        Privileges.Enable("SeRestorePrivilege");

        var name = mountName ?? "ProfileKit_" + Guid.NewGuid().ToString("N")[..8];
        var rc = NativeMethods.RegLoadKey(new UIntPtr(NativeMethods.HKEY_USERS), name, hiveFile);
        if (rc != 0)
        {
            var hint = rc == 32 ? " Die Datei ist in Benutzung – ist der Benutzer angemeldet?" : string.Empty;
            throw new Win32Exception(rc, $"Hive konnte nicht geladen werden: {hiveFile}.{hint}");
        }
        return new HiveMount(name, hiveFile);
    }

    /// <summary>Öffnet die Wurzel des geladenen Hives. Aufrufer muss den Schlüssel vor dem Entladen schließen.</summary>
    public RegistryKey OpenRoot(bool writable) =>
        Registry.Users.OpenSubKey(MountName, writable)
        ?? throw new InvalidOperationException($"HKU\\{MountName} ist nicht geladen.");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Offene Handles (z. B. noch nicht finalisierte RegistryKey-Objekte) verhindern das Entladen.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var rc = NativeMethods.RegUnLoadKey(new UIntPtr(NativeMethods.HKEY_USERS), MountName);
            if (rc == 0) return;
            Thread.Sleep(200 * (attempt + 1));
        }
        Console.Error.WriteLine($"Warnung: HKU\\{MountName} konnte nicht entladen werden (wird beim Neustart frei).");
    }
}

[SupportedOSPlatform("windows")]
internal static class Privileges
{
    public static void Enable(string privilege)
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!NativeMethods.LookupPrivilegeValue(null, privilege, out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var tp = new NativeMethods.TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = NativeMethods.SE_PRIVILEGE_ENABLED };
            if (!NativeMethods.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var err = Marshal.GetLastWin32Error();
            if (err == 1300) // ERROR_NOT_ALL_ASSIGNED
                throw new UnauthorizedAccessException($"Privileg {privilege} fehlt – als Administrator ausführen.");
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }
}
