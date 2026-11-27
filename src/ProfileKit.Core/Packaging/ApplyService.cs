using ProfileKit.Core.Model;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Registry;
using ProfileKit.Core.Text;

namespace ProfileKit.Core.Packaging;

public sealed record ApplyOptions
{
    /// <summary>Nur anzeigen, nichts schreiben.</summary>
    public bool DryRun { get; init; }

    /// <summary>Ordner für Sicherungen vorhandener Dateien/Werte; null = keine Sicherung.</summary>
    public string? BackupDirectory { get; init; }

    /// <summary>Profilpfade in Inhalten umschreiben (Standard: ja).</summary>
    public bool RewritePaths { get; init; } = true;

    /// <summary>Maschinenweite Ziele (%PROGRAMFILES%, %PROGRAMDATA%, HKLM) mit anwenden.</summary>
    public bool IncludeMachineScope { get; init; } = true;
}

public enum ApplyActionKind { WriteFile, WriteRegistry, Skip }

public sealed record ApplyAction(string ModuleId, ApplyActionKind Kind, string Target, string? Detail = null);

public sealed record ApplyReport(IReadOnlyList<ApplyAction> Actions, IReadOnlyList<string> Warnings)
{
    public int FilesWritten => Actions.Count(a => a.Kind == ApplyActionKind.WriteFile);
    public int ValuesWritten => Actions.Count(a => a.Kind == ApplyActionKind.WriteRegistry);
}

/// <summary>Wendet Module eines Profils auf einen Zielbenutzer an.</summary>
public sealed class ApplyService
{
    public ApplyReport ApplyModule(ProfilePackage package, string moduleId, SourceInfo source, UserContext target, IRegistryBackend registry, ApplyOptions options)
    {
        var rule = package.ReadModule(moduleId);
        var actions = new List<ApplyAction>();
        var warnings = new List<string>();
        var rewriter = options.RewritePaths ? new PathRewriter(source.ProfilePath, target.ProfilePath) : null;

        foreach (var file in rule.Files)
        {
            var machineScope = !PathTokens.IsUserScoped(file.Path);
            if (machineScope && !options.IncludeMachineScope)
            {
                actions.Add(new(rule.Id, ApplyActionKind.Skip, file.Path, "maschinenweit"));
                continue;
            }

            var packaged = package.PackagedPathFor(rule.Id, file.Path);
            var destination = PathTokens.Expand(file.Path, target);

            if (File.Exists(packaged))
            {
                ApplyFile(rule, file, packaged, destination, rewriter, options, actions, warnings, file.Path);
            }
            else if (Directory.Exists(packaged))
            {
                foreach (var f in Directory.EnumerateFiles(packaged, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(packaged, f);
                    ApplyFile(rule, file, f, PathText.Join(destination, relative), rewriter, options, actions, warnings,
                        file.Path + "\\" + relative.Replace('/', '\\'));
                }
            }
        }

        foreach (var original in package.ReadRegistry(rule.Id))
        {
            var (root, _) = RegistryPath.Split(original.Key);
            if (root == RegistryPath.LocalMachine && !options.IncludeMachineScope)
            {
                actions.Add(new(rule.Id, ApplyActionKind.Skip, $"{original.Key}\\{original.Name}", "maschinenweit"));
                continue;
            }
            var value = rewriter is null ? original : original.MapStrings(rewriter.Rewrite);
            actions.Add(new(rule.Id, ApplyActionKind.WriteRegistry, $"{value.Key}\\{(value.Name.Length == 0 ? "(Standard)" : value.Name)}", value.Kind.ToString()));
            if (options.DryRun) continue;

            if (options.BackupDirectory is not null) BackupRegistryValue(options.BackupDirectory, rule.Id, registry, value);
            try
            {
                registry.Write(value);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                warnings.Add($"Registry-Wert nicht schreibbar: {value.Key}\\{value.Name} – {ex.Message}");
            }
        }

        return new ApplyReport(actions, warnings);
    }

    private static void ApplyFile(ModuleRule rule, FileRule file, string packaged, string destination, PathRewriter? rewriter,
        ApplyOptions options, List<ApplyAction> actions, List<string> warnings, string displayPath)
    {
        var detail = file.Kind == FileKind.Keyfile ? "Keyfile" : null;
        actions.Add(new(rule.Id, ApplyActionKind.WriteFile, destination, detail));
        if (options.DryRun) return;

        try
        {
            if (options.BackupDirectory is not null && File.Exists(destination))
            {
                var backup = PathText.Join(options.BackupDirectory, rule.Id, "files", PathTokens.ToPackagePath(displayPath));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, overwrite: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var bytes = File.ReadAllBytes(packaged);
            var rewritten = rewriter?.RewriteBytes(bytes);
            File.WriteAllBytes(destination, rewritten ?? bytes);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(packaged));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Datei nicht schreibbar: {destination} – {ex.Message}");
        }
    }

    private static void BackupRegistryValue(string backupDirectory, string moduleId, IRegistryBackend registry, RegistryValue value)
    {
        var path = Path.Combine(backupDirectory, moduleId, "registry-before.json");
        var list = File.Exists(path) ? Json.Read<List<RegistryBackupEntry>>(path) : [];
        list.Add(new RegistryBackupEntry(value.Key, value.Name, registry.Get(value.Key, value.Name)));
        Json.Write(path, list);
    }

    /// <summary>Vorheriger Zustand eines Werts; <see cref="Previous"/> null = Wert existierte nicht.</summary>
    public sealed record RegistryBackupEntry(string Key, string Name, RegistryValue? Previous);
}
