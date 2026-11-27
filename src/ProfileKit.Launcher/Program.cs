using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ProfileKit.Launcher;

/// <summary>
/// Startet die Oberfläche aus <c>app\</c>, damit im Root des Pakets nur die EXE-Dateien liegen.
/// Ist die Oberfläche framework-abhängig, wird vorher die .NET-Runtime geprüft und bei Bedarf
/// über <c>app\Install-DotNetRuntime.ps1</c> installiert.
/// </summary>
internal static class Program
{
    private static readonly bool German = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    [STAThread]
    private static int Main()
    {
        var appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
        var gui = Path.Combine(appDir, "profilekit-gui.exe");
        if (!File.Exists(gui))
            return Fail($"{gui} fehlt – bitte das ZIP vollständig entpacken.", $"{gui} is missing – please extract the whole ZIP.");

        var channel = RequiredRuntimeChannel(Path.Combine(appDir, "profilekit-gui.runtimeconfig.json"));
        if (channel is not null && !RuntimeInstalled(channel))
        {
            var script = Path.Combine(appDir, "Install-DotNetRuntime.ps1");
            if (!File.Exists(script))
                return Fail($".NET-Runtime {channel} fehlt und {script} wurde nicht gefunden.",
                            $".NET runtime {channel} is missing and {script} was not found.");
            using var install = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Channel {channel}") { UseShellExecute = false })!;
            install.WaitForExit();
            if (install.ExitCode != 0 || !RuntimeInstalled(channel))
                return Fail($".NET-Runtime {channel} konnte nicht installiert werden (Exitcode {install.ExitCode}).\n" +
                            $"Manuell: https://dotnet.microsoft.com/download/dotnet/{channel}",
                            $".NET runtime {channel} could not be installed (exit code {install.ExitCode}).\n" +
                            $"Manually: https://dotnet.microsoft.com/download/dotnet/{channel}");
        }

        Process.Start(new ProcessStartInfo(gui) { UseShellExecute = false, WorkingDirectory = appDir })?.Dispose();
        return 0;
    }

    /// <summary>„10.0“ bei framework-abhängiger Oberfläche, <c>null</c> wenn sie self-contained ist.</summary>
    private static string? RequiredRuntimeChannel(string runtimeConfig)
    {
        if (!File.Exists(runtimeConfig)) return null;
        var json = File.ReadAllText(runtimeConfig);
        if (json.Contains("\"includedFrameworks\"")) return null;
        var m = Regex.Match(json, "\"name\"\\s*:\\s*\"Microsoft\\.NETCore\\.App\"\\s*,\\s*\"version\"\\s*:\\s*\"(\\d+\\.\\d+)\\.");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Dieselbe Suche wie Install-DotNetRuntime.ps1: Release-Version mit passender Hauptversion.</summary>
    private static bool RuntimeInstalled(string channel)
    {
        var major = channel.Split('.')[0];
        var pattern = new Regex("^" + major + @"\.\d+\.\d+$");
        return DotNetRoots().Any(root =>
        {
            var shared = Path.Combine(root, "shared", "Microsoft.NETCore.App");
            return Directory.Exists(shared) &&
                   Directory.GetDirectories(shared).Any(d => pattern.IsMatch(Path.GetFileName(d)));
        });
    }

    private static IEnumerable<string> DotNetRoots()
    {
        foreach (var name in new[] { "DOTNET_ROOT_X64", "DOTNET_ROOT" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } v) yield return v;
        string? registered = null;
        try
        {
            using var hklm32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = hklm32.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
            registered = key?.GetValue("InstallLocation") as string;
        }
        catch (Exception) { }
        if (!string.IsNullOrEmpty(registered)) yield return registered!;
        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ??
                           Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "dotnet");
    }

    private static int Fail(string german, string english)
    {
        MessageBoxW(IntPtr.Zero, German ? german : english, "Win-ProfileKit", 0x10 /* MB_ICONERROR */);
        return 1;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
