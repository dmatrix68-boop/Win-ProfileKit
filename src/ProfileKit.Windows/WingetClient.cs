using System.Diagnostics;
using System.Runtime.Versioning;
using ProfileKit.Core.Winget;

namespace ProfileKit.Windows;

/// <summary>
/// Aufrufe von winget.exe. winget ist nur über den App-Execution-Alias im Benutzerkontext
/// zuverlässig erreichbar (als SYSTEM fehlen die MSIX-Abhängigkeiten, siehe OEM-Skriptgenerator).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WingetClient
{
    public TimeSpan InstallTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public string Executable { get; } = ResolveExecutable();

    private static string ResolveExecutable()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(alias) ? alias : "winget.exe";
    }

    /// <summary>Installierte Pakete über <c>winget export</c>.</summary>
    public IReadOnlyList<InstalledPackage> ListInstalled()
    {
        var file = Path.Combine(Path.GetTempPath(), $"profilekit-winget-{Guid.NewGuid():N}.json");
        try
        {
            var (code, output) = Run(TimeSpan.FromMinutes(5), "export", "-o", file, "--include-versions",
                "--accept-source-agreements", "--disable-interactivity");
            if (!File.Exists(file))
                throw new InvalidOperationException($"winget export fehlgeschlagen ({WingetExitCodes.Describe(code)}): {output.Trim()}");
            return WingetExport.Parse(File.ReadAllText(file));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>Installiert ein Paket still. Liefert den Exitcode (siehe <see cref="WingetExitCodes"/>).</summary>
    public int Install(string packageId, string? version = null)
    {
        var args = new List<string>
        {
            "install", "--id", packageId, "--exact", "--silent", "--source", "winget",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity",
        };
        if (!string.IsNullOrEmpty(version)) { args.Add("--version"); args.Add(version); }
        return Run(InstallTimeout, [.. args]).ExitCode;
    }

    private (int ExitCode, string Output) Run(TimeSpan timeout, params string[] args)
    {
        // Direkter Start ohne Shell (CreateProcess): der Alias wird aufgelöst und der echte
        // Prozess liefert seinen Exitcode – anders als bei Start-Process über die Shell.
        var psi = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("winget konnte nicht gestartet werden.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"winget {string.Join(' ', args)} hat das Zeitlimit von {timeout.TotalMinutes:0} min überschritten.");
        }
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
