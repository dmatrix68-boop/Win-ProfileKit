using System.Diagnostics;
using System.Runtime.Versioning;
using ProfileKit.Core.Agent;
using ProfileKit.Core.Mcp;

namespace ProfileKit.Windows;

/// <summary>Eine auf dem Rechner gefundene KI-Anwendung.</summary>
public sealed record AiApp(string Name, string Kind, string? Location, string Integration);

/// <summary>Erkennt KI-Anwendungen (SPEC 4.8) und bindet den MCP-Server ein.</summary>
[SupportedOSPlatform("windows")]
public static class AiEnvironment
{
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static IReadOnlyList<AiApp> Detect()
    {
        var apps = new List<AiApp>();

        foreach (var dir in ClaudeDesktopConfigDirectories(includeMissing: false))
            apps.Add(new("Claude Desktop", "desktop", dir, "MCP: profilekit mcp install"));
        if (FindPackage("OpenAI.ChatGPT-Desktop_") is { } chatgpt)
            apps.Add(new("ChatGPT", "desktop", chatgpt, "MCP: profilekit mcp config (manual)"));
        if (FindPackage("Microsoft.Copilot_") is { } copilot)
            apps.Add(new("Microsoft Copilot", "desktop", copilot, "MCP: profilekit mcp config (manual)"));

        if (FindExecutable("claude") is { } claude) apps.Add(new("Claude Code", "cli", claude, "ai discover --provider claude-code"));
        if (FindExecutable("codex") is { } codex) apps.Add(new("Codex CLI", "cli", codex, "MCP: profilekit mcp config (manual)"));
        if (FindExecutable("copilot") is { } ghcp) apps.Add(new("GitHub Copilot CLI", "cli", ghcp, "MCP: profilekit mcp config (manual)"));
        return apps;
    }

    /// <summary>
    /// Mögliche Konfigurationsordner von Claude Desktop: klassische Installation (<c>%APPDATA%\Claude</c>)
    /// und MSIX-Installation (virtualisiert unter <c>Packages\Claude_*\LocalCache\Roaming\Claude</c>).
    /// </summary>
    public static IReadOnlyList<string> ClaudeDesktopConfigDirectories(bool includeMissing)
    {
        var dirs = new List<string>();
        var classic = Path.Combine(AppData, "Claude");
        if (includeMissing || Directory.Exists(classic) || File.Exists(Path.Combine(LocalAppData, "AnthropicClaude", "claude.exe")))
            dirs.Add(classic);
        var packages = Path.Combine(LocalAppData, "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var pkg in Directory.EnumerateDirectories(packages, "Claude_*"))
            {
                var virtualized = Path.Combine(pkg, "LocalCache", "Roaming", "Claude");
                if (includeMissing || Directory.Exists(virtualized)) dirs.Add(virtualized);
            }
        }
        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Trägt profilekit als MCP-Server in Claude Desktop ein. Liefert die geänderten Dateien.</summary>
    public static IReadOnlyList<string> InstallIntoClaudeDesktop(string exePath)
    {
        var dirs = ClaudeDesktopConfigDirectories(includeMissing: false);
        if (dirs.Count == 0) throw new InvalidOperationException("Claude Desktop was not found.");
        var changed = new List<string>();
        foreach (var dir in dirs)
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "claude_desktop_config.json");
            var existing = File.Exists(file) ? File.ReadAllText(file) : null;
            if (existing is not null) File.Copy(file, file + ".bak", overwrite: true);
            File.WriteAllText(file, McpClientConfig.AddServer(existing, exePath, ["mcp", "--agent", "claude-desktop"]));
            changed.Add(file);
        }
        return changed;
    }

    public static IReadOnlyList<string> UninstallFromClaudeDesktop()
    {
        var changed = new List<string>();
        foreach (var dir in ClaudeDesktopConfigDirectories(includeMissing: false))
        {
            var file = Path.Combine(dir, "claude_desktop_config.json");
            if (!File.Exists(file)) continue;
            var json = File.ReadAllText(file);
            if (!McpClientConfig.Contains(json)) continue;
            File.WriteAllText(file, McpClientConfig.RemoveServer(json));
            changed.Add(file);
        }
        return changed;
    }

    public static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extra = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"),
            Path.Combine(AppData, "npm"),
        };
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Concat(extra))
        {
            foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim('"'), name + ext);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { /* ungültiger PATH-Eintrag */ }
            }
        }
        return null;
    }

    private static string? FindPackage(string prefix)
    {
        var packages = Path.Combine(LocalAppData, "Packages");
        return Directory.Exists(packages) ? Directory.EnumerateDirectories(packages, prefix + "*").FirstOrDefault() : null;
    }

    /// <summary>Prüft Ollama; liefert installierte Modelle oder null, wenn nicht erreichbar.</summary>
    public static async Task<IReadOnlyList<string>?> ProbeOllamaAsync(HttpClient http, string? url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            return (await ModelCatalog.OllamaAsync(http, url, cts.Token).ConfigureAwait(false)).Select(m => m.Id).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>
/// Stufe 1b: Claude Code ohne Oberfläche (<c>claude -p</c>) mit dem eigenen MCP-Server. Claude Code darf nur
/// die profilekit-Werkzeuge sowie Websuche/-abruf nutzen; Shell, Dateizugriff und Bearbeiten sind gesperrt.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClaudeCodeRunner(string claudePath, string profileKitExe)
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);

    public static readonly string[] AllowedTools = ["mcp__" + McpClientConfig.ServerName, "WebSearch", "WebFetch"];
    public static readonly string[] DisallowedTools = ["Bash", "PowerShell", "Edit", "Write", "MultiEdit", "NotebookEdit", "Read", "Glob", "Grep", "Task"];

    /// <summary>Startet Claude Code und liefert dessen Textausgabe.</summary>
    public (int ExitCode, string Output) Run(string prompt, string? model, Action<string>? onLine = null)
    {
        var config = Path.Combine(Path.GetTempPath(), $"profilekit-mcp-{Guid.NewGuid():N}.json");
        File.WriteAllText(config, McpClientConfig.AddServer(null, profileKitExe, ["mcp", "--agent", "claude-code"]));
        try
        {
            var args = new List<string>
            {
                "-p",
                "--mcp-config", config,
                "--strict-mcp-config",
                "--allowedTools", string.Join(',', AllowedTools),
                "--disallowedTools", string.Join(',', DisallowedTools),
            };
            if (!string.IsNullOrEmpty(model)) { args.Add("--model"); args.Add(model); }

            var isScript = claudePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || claudePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            var psi = new ProcessStartInfo(isScript ? "cmd.exe" : claudePath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (isScript) { psi.ArgumentList.Add("/d"); psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(claudePath); }
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Claude Code could not be started.");
            var output = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is null) return; output.AppendLine(e.Data); onLine?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine?.Invoke(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // Prompt über stdin – keine Probleme mit Anführungszeichen und Längenbegrenzung der Kommandozeile
            process.StandardInput.Write(prompt);
            process.StandardInput.Close();

            if (!process.WaitForExit(Timeout))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new TimeoutException($"Claude Code did not finish within {Timeout.TotalMinutes:0} minutes.");
            }
            process.WaitForExit();
            return (process.ExitCode, output.ToString());
        }
        finally
        {
            try { File.Delete(config); } catch (IOException) { }
        }
    }
}
