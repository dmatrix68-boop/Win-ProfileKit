using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ProfileKit.Windows;

/// <summary>
/// Aktiviert frisch geschriebene Einstellungen für den angemeldeten Benutzer, ohne Abmelden.
/// Nur sinnvoll, wenn das Ziel der aktuelle Benutzer ist.
/// </summary>
[SupportedOSPlatform("windows")]
public static class LiveSettings
{
    /// <summary>Führt die <c>postApply</c>-Hinweise aus; liefert die tatsächlich angewendeten.</summary>
    public static IReadOnlyList<string> Apply(IEnumerable<string> hints)
    {
        var applied = new List<string>();
        foreach (var hint in hints.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var ok = hint.ToLowerInvariant() switch
            {
                "mouse" => ApplyMouse(),
                "theme" => Broadcast("ImmersiveColorSet"),
                "explorer" => Broadcast(null) && Broadcast("TraySettings"),
                "taskbar" => Broadcast("TraySettings"),
                _ => false,
            };
            if (ok) applied.Add(hint);
        }
        return applied;
    }

    private static bool ApplyMouse()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
        if (key is null) return false;
        const uint flags = NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE;

        if (ReadInt(key, "MouseSensitivity") is { } speed and >= 1 and <= 20)
            NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETMOUSESPEED, 0, new IntPtr(speed), flags);

        if (ReadInt(key, "DoubleClickSpeed") is { } dbl and > 0)
            NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETDOUBLECLICKTIME, (uint)dbl, IntPtr.Zero, flags);

        // Zeigerbeschleunigung ("Zeigerpräzision verbessern"): MouseThreshold1/2 + MouseSpeed
        if (ReadInt(key, "MouseThreshold1") is { } t1 && ReadInt(key, "MouseThreshold2") is { } t2 && ReadInt(key, "MouseSpeed") is { } accel)
            NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETMOUSE, 0, [t1, t2, accel], flags);

        return true;
    }

    private static int? ReadInt(RegistryKey key, string name) =>
        int.TryParse(key.GetValue(name)?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool Broadcast(string? area)
    {
        NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, UIntPtr.Zero, area,
            NativeMethods.SMTO_ABORTIFHUNG, 3000, out _);
        return true;
    }
}
