using System.Diagnostics;
using System.Text;

namespace ProfileKit.App.Services;

/// <summary>
/// Führt <c>profilekit.exe</c> aus und liefert die Ausgabe zeilenweise. Alle Aktionen der Oberfläche
/// laufen über die CLI – damit nutzen Oberfläche, Skripte und OEM-Lauf denselben, getesteten Weg.
/// </summary>
public sealed class CliRunner
{
    private Process? _process;

    public static string CliPath => Path.Combine(AppContext.BaseDirectory, "profilekit.exe");

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Wird für jede Ausgabezeile aufgerufen (aus einem Hintergrund-Thread).</summary>
    public event Action<string, bool>? Line;

    public async Task<int> RunAsync(IReadOnlyList<string> args, string? stdin = null)
    {
        if (IsRunning) throw new InvalidOperationException("busy");
        if (!File.Exists(CliPath)) throw new FileNotFoundException("profilekit.exe fehlt neben der Oberfläche.", CliPath);

        var psi = new ProcessStartInfo(CliPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Line?.Invoke("> profilekit " + string.Join(' ', args.Select(Quote)), false);
        var process = Process.Start(psi) ?? throw new InvalidOperationException("profilekit.exe konnte nicht gestartet werden.");
        _process = process;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Line?.Invoke(e.Data, false); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Line?.Invoke(e.Data, true); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (stdin is not null) await process.StandardInput.WriteLineAsync(stdin);
        process.StandardInput.Close();

        await process.WaitForExitAsync();
        var code = process.ExitCode;
        process.Dispose();
        _process = null;
        return code;
    }

    public void Cancel()
    {
        try { _process?.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    private static string Quote(string a) => a.Contains(' ') ? "\"" + a + "\"" : a;
}
