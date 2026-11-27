using System.Diagnostics;
using System.Security.Principal;

namespace ProfileKit.App.Services;

public static class Elevation
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Startet die Oberfläche mit Administratorrechten neu. Liefert false, wenn der Benutzer abbricht.</summary>
    public static bool RestartElevated()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // UAC-Abfrage abgelehnt
        }
    }

    public static void Restart()
    {
        if (Environment.ProcessPath is { } exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }
}
