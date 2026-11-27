using System.Diagnostics;
using ProfileKit.Core.Catalog;
using ProfileKit.Core.Inventory;
using ProfileKit.Core.Learning;
using ProfileKit.Core.Model;
using ProfileKit.Core.Packaging;
using ProfileKit.Core.Paths;
using ProfileKit.Core.Planning;
using ProfileKit.Core.Text;
using ProfileKit.Core.Winget;
using ProfileKit.Windows;
using static ProfileKit.Core.Text.Localizer;

namespace ProfileKit.Cli;

internal sealed class Commands(CliArgs args)
{
    private static string ToolVersion =>
        typeof(Commands).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    internal RuleCatalog LoadCatalog()
    {
        var dirs = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "rules"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win-ProfileKit", "rules"),
        };
        if (args.Get("rules") is { } extra) dirs.Add(extra);
        var catalog = RuleCatalog.Load([.. dirs]);
        foreach (var e in catalog.LoadErrors) Console.Error.WriteLine(T("rule-error", e));
        return catalog;
    }

    // ---------------------------------------------------------------- users

    public int Users()
    {
        Console.WriteLine(T("users-header"));
        foreach (var p in UserProfiles.List())
            Console.WriteLine($"{p.UserName,-24} {(p.IsLoaded ? "✓" : "-"),-11} {p.ProfilePath}");
        Console.WriteLine($"{"Default",-24} {"-",-11} {UserProfiles.DefaultProfilePath()}");
        return ExitCodes.Ok;
    }

    // -------------------------------------------------------------- catalog

    public int Catalog()
    {
        var catalog = LoadCatalog();
        Console.WriteLine(T("catalog-header"));
        foreach (var r in catalog.Rules.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"{r.Id,-28} {r.WingetId ?? "-",-29} {r.DisplayName}");
        Console.WriteLine(T("rules-loaded", catalog.Rules.Count));
        return catalog.LoadErrors.Count > 0 ? ExitCodes.Partial : ExitCodes.Ok;
    }

    // ------------------------------------------------------------ inventory

    public int Inventory()
    {
        var catalog = LoadCatalog();
        var installed = new WingetClient().ListInstalled();
        Console.WriteLine(T("inventory-header"));
        foreach (var p in installed.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            var rule = catalog.FindByWingetId(p.Id);
            Console.WriteLine($"{p.Id,-45} {rule?.Id ?? "-"}");
        }
        if (args.Get("out") is { } outFile)
        {
            File.WriteAllText(outFile, WingetExport.Create(installed));
            Console.WriteLine(outFile);
        }
        return ExitCodes.Ok;
    }

    // -------------------------------------------------------------- capture

    public int Capture()
    {
        var outDir = args.Require("out");
        var name = args.Get("name") ?? Path.GetFileName(Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar));
        var catalog = LoadCatalog();

        IReadOnlyList<InstalledPackage> installed = [];
        if (args.Has("installed")) installed = new WingetClient().ListInstalled();
        var versions = installed.ToDictionary(p => p.Id, p => p.Version, StringComparer.OrdinalIgnoreCase);

        var filter = new ModuleFilter(args.Get("modules"));
        var rules = catalog.Rules.Where(r =>
                (!filter.IsEmpty && filter.Matches(r.Id))
                || (args.Has("installed") && r.WingetId is not null && versions.ContainsKey(r.WingetId)))
            .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (filter.IsEmpty && !args.Has("installed")) throw new CliException(T("missing-option", "--modules / --installed"));
        foreach (var id in RequestedIds().Where(id => !id.Contains('*') && catalog.Find(id) is null))
            Console.Error.WriteLine(T("warning", T("module-unknown", id)));
        if (rules.Count == 0) { Console.Error.WriteLine(T("no-modules")); return ExitCodes.Error; }

        var (source, image) = OpenSource();
        try
        {
            return CaptureRules(source, outDir, name, rules, versions, inventory: null);
        }
        finally
        {
            source.Dispose();
            image?.Dispose();
        }
    }

    /// <summary>
    /// Quelle für capture/collect: aktueller Benutzer, anderer Benutzer (--user) oder Benutzer in einer VHDX
    /// einer heruntergefahrenen VM (--vhdx [--user]). Der Aufrufer gibt beides frei (erst Quelle, dann Image).
    /// </summary>
    private (UserTarget Source, VhdxImage? Image) OpenSource()
    {
        if (args.Get("vhdx") is not { } vhdx)
            return (args.Get("user") is { } u ? UserTarget.OpenUser(u, writable: false) : UserTarget.OpenCurrent(), null);

        var checkpoints = OfflineImageLayout.CheckpointFiles(vhdx);
        if (checkpoints.Count > 0) Console.Error.WriteLine(T("warning", T("vhdx-checkpoint", string.Join(", ", checkpoints.Select(Path.GetFileName)))));
        Console.WriteLine(T("vhdx-mounting", vhdx));
        var image = VhdxImage.Mount(vhdx);
        try
        {
            Console.WriteLine(T("vhdx-mounted", image.Root, image.MachineName, string.Join(", ", image.Users.Select(x => x.UserName))));
            var name = args.Get("user")
                ?? (image.Users.Count == 1 ? image.Users[0].UserName : throw new CliException(T("vhdx-user-required", string.Join(", ", image.Users.Select(x => x.UserName)))));
            return (image.OpenUser(name), image);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    // ----------------------------------------------------------------- vhdx

    /// <summary><c>vhdx users --vhdx &lt;Datei&gt;</c>: Benutzer im Image anzeigen.</summary>
    public int VhdxUsers()
    {
        var vhdx = args.Require("vhdx");
        Console.WriteLine(T("vhdx-mounting", vhdx));
        using var image = VhdxImage.Mount(vhdx);
        Console.WriteLine(T("vhdx-mounted", image.Root, image.MachineName, image.Users.Count));
        Console.WriteLine(T("vhdx-users-header"));
        foreach (var u in image.Users) Console.WriteLine($"{u.UserName,-24} {u.OriginalProfilePath,-33} {u.MountedProfilePath}");
        return ExitCodes.Ok;
    }

    /// <summary>Gemeinsamer Kern von capture und collect: Module erfassen, Manifest, LIESMICH, optional Archiv.</summary>
    private int CaptureRules(UserTarget source, string outDir, string name, IReadOnlyList<ModuleRule> rules,
        IReadOnlyDictionary<string, string?> versions, MachineInventory? inventory)
    {
        var package = new ProfilePackage(outDir);
        Console.WriteLine(T("capture-start", name, source.Describe(), package.Root));

        var service = new CaptureService();
        var entries = new List<ModuleEntry>();
        var warnings = 0;
        foreach (var rule in rules)
        {
            var version = rule.WingetId is not null ? versions.GetValueOrDefault(rule.WingetId) : null;
            var result = service.CaptureModule(package, rule, source.Context, source.Registry, version);
            entries.Add(result.Entry);
            Console.WriteLine(T("capture-module", rule.Id, result.Entry.FileCount, FormatBytes(result.Entry.TotalBytes), result.Entry.RegistryValueCount));
            foreach (var w in result.Warnings) { Console.Error.WriteLine(T("warning", w)); warnings++; }
        }

        var manifest = new ProfileManifest
        {
            Name = name,
            ToolVersion = ToolVersion,
            Source = new SourceInfo
            {
                Machine = source.SourceMachine ?? Environment.MachineName,
                UserName = source.Context.UserName,
                Sid = source.Context.Sid,
                // Beim Offline-Image der Pfad im Image-Windows – nur so stimmt das Umschreiben beim Anwenden
                ProfilePath = source.OriginalProfilePath ?? source.Context.ProfilePath,
            },
            Modules = entries,
        };
        package.WriteManifest(manifest);
        inventory?.Write(package);
        WriteReadme(package, manifest);
        Console.WriteLine(T("capture-done", entries.Count));
        if (manifest.ContainsKeyfiles)
            Console.WriteLine(T("keyfile-notice", string.Join(", ", entries.Where(e => e.ContainsKeyfiles).Select(e => e.Id))));

        if (args.Has("zip"))
        {
            var archive = package.Root.TrimEnd(Path.DirectorySeparatorChar) + ProfilePackage.ArchiveExtension;
            package.ExportArchive(archive);
            Console.WriteLine(T("archive-written", archive));
        }
        return warnings > 0 ? ExitCodes.Partial : ExitCodes.Ok;
    }

    // -------------------------------------------------------------- collect

    /// <summary>
    /// Sammler (SPEC 4.6): auf dem Quellrechner, auch direkt von einer Netzwerkfreigabe gestartet. Erfasst alle
    /// installierten Programme mit Regel plus Windows-Einstellungen und legt das Inventar daneben.
    /// </summary>
    public int Collect()
    {
        var outRoot = args.Require("out");
        var catalog = LoadCatalog();
        var (source, image) = OpenSource();
        try
        {
            return CollectFrom(source, catalog, outRoot);
        }
        finally
        {
            source.Dispose();
            image?.Dispose();
        }
    }

    private int CollectFrom(UserTarget source, RuleCatalog catalog, string outRoot)
    {
        IReadOnlyList<InstalledPackage> winget = [];
        if (source.Mode == UserTargetMode.OfflineImage)
        {
            Console.WriteLine(T("vhdx-no-winget"));
        }
        else
        {
            try { winget = new WingetClient().ListInstalled(); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                Console.Error.WriteLine(T("warning", T("collect-no-winget", ex.Message)));
            }
        }
        var programs = UninstallInventory.Read(source.Registry, source.Mode == UserTargetMode.OfflineImage ? null : source.Context);
        Console.WriteLine(T("collect-inventory", winget.Count, programs.Count));

        var rules = InventoryMatcher.RulesForInstalled(catalog.Rules, winget, programs).ToList();
        if (!args.Has("no-windows")) rules.AddRange(catalog.Rules.Where(r => r.IsWindowsModule));
        var filter = new ModuleFilter(args.Get("only"));
        rules = rules.Where(r => filter.Matches(r.Id)).OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
        if (rules.Count == 0) { Console.Error.WriteLine(T("no-modules")); return ExitCodes.Error; }

        var machine = source.SourceMachine ?? Environment.MachineName;
        var name = args.Get("name") ?? $"{machine}_{source.Context.UserName}";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new CliException(T("invalid-name", name));
        var outDir = Path.Combine(outRoot, name);
        var inventory = new MachineInventory
        {
            Machine = machine,
            UserName = source.Context.UserName,
            Winget = winget.ToList(),
            Programs = programs.ToList(),
        };
        var versions = winget.ToDictionary(p => p.Id, p => p.Version, StringComparer.OrdinalIgnoreCase);
        var code = CaptureRules(source, outDir, name, rules, versions, inventory);

        var uncovered = InventoryMatcher.Uncovered(programs, catalog.Rules);
        Console.WriteLine(T("collect-uncovered", uncovered.Count));
        foreach (var p in uncovered.Take(args.Has("verbose") ? int.MaxValue : 25)) Console.WriteLine("  - " + p.Name);
        if (uncovered.Count > 0)
            Console.WriteLine(T("collect-uncovered-hint", Path.Combine(outDir, MachineInventory.WingetExportFileName)));
        return code;
    }

    // -------------------------------------------------------------- compare

    /// <summary>Abgleich eines Profils mit dem Zielrechner: was ist installiert, was installierbar, was fehlt?</summary>
    public int Compare()
    {
        var package = ProfilePackage.Open(args.Require("profile"));
        var manifest = package.ReadManifest();
        var (winget, programs) = TargetInventory();

        var presence = InventoryMatcher.Compare(manifest.Modules, winget, programs);
        Console.WriteLine(T("compare-header", manifest.Name, manifest.Source.UserName, manifest.Source.Machine, Environment.MachineName));
        foreach (var p in presence.OrderBy(p => p.Presence).ThenBy(p => p.Module.Id, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"  {PresenceLabel(p.Presence),-26} {p.Module.Id,-24} {p.MatchedBy ?? p.Module.WingetId ?? ""}");
        Console.WriteLine(T("compare-summary",
            presence.Count(p => p.Presence == Presence.Installed), presence.Count(p => p.Presence == Presence.Installable),
            presence.Count(p => p.Presence == Presence.Missing), presence.Count(p => p.Presence == Presence.Windows)));

        if (MachineInventory.TryRead(package) is { } sourceInventory)
        {
            var uncovered = InventoryMatcher.Uncovered(sourceInventory.Programs, LoadCatalog().Rules);
            Console.WriteLine(T("compare-source-uncovered", uncovered.Count));
            foreach (var p in uncovered.Take(args.Has("verbose") ? int.MaxValue : 25)) Console.WriteLine("  - " + p.Name);
        }
        return ExitCodes.Ok;
    }

    private static (IReadOnlyList<InstalledPackage> Winget, IReadOnlyList<UninstallEntry> Programs) TargetInventory()
    {
        IReadOnlyList<InstalledPackage> winget = [];
        try { winget = new WingetClient().ListInstalled(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine(T("warning", T("collect-no-winget", ex.Message)));
        }
        using var registry = WindowsRegistryBackend.ForCurrentUser();
        return (winget, UninstallInventory.Read(registry));
    }

    private static string PresenceLabel(Presence p) => p switch
    {
        Presence.Installed => T("presence-installed"),
        Presence.Installable => T("presence-installable"),
        Presence.Missing => T("presence-missing"),
        _ => T("presence-windows"),
    };

    private IEnumerable<string> RequestedIds() =>
        (args.Get("modules") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ----------------------------------------------------------------- info

    public int Info()
    {
        var package = ProfilePackage.Open(args.Require("profile"));
        var m = package.ReadManifest();
        Console.WriteLine($"{m.Name}  ·  {m.CreatedUtc.LocalDateTime:g}  ·  {m.Source.UserName}@{m.Source.Machine}");
        foreach (var e in m.Modules)
            Console.WriteLine($"  {e.Id,-28} {e.WingetId ?? "-",-29} {e.FileCount,5} / {FormatBytes(e.TotalBytes),10} / {e.RegistryValueCount,4}{(e.ContainsKeyfiles ? "  [Keyfile]" : "")}");
        return ExitCodes.Ok;
    }

    // ---------------------------------------------------------------- apply

    public int Apply()
    {
        var package = ProfilePackage.Open(args.Require("profile"));
        var manifest = package.ReadManifest();
        var filter = new ModuleFilter(args.Get("only"));
        var modules = manifest.Modules.Where(m => filter.Matches(m.Id)).ToList();
        if (args.Has("installed-only"))
        {
            // Zielrechner bestimmt: nur Einstellungen für Programme, die hier schon installiert sind
            if (args.Has("install")) throw new CliException(T("installed-only-conflict"));
            var (targetWinget, targetPrograms) = TargetInventory();
            var presence = InventoryMatcher.Compare(modules, targetWinget, targetPrograms);
            foreach (var p in presence.Where(p => p.Presence is Presence.Installable or Presence.Missing))
                Console.WriteLine(T("installed-only-skipped", p.Module.Id));
            modules = presence.Where(p => p.Presence is Presence.Installed or Presence.Windows).Select(p => p.Module).ToList();
        }
        if (modules.Count == 0) { Console.Error.WriteLine(T("no-modules")); return ExitCodes.Error; }

        var dryRun = args.Has("dry-run");
        using var target = OpenApplyTarget(dryRun);
        Console.WriteLine(T("apply-start", manifest.Name, target.Mode == UserTargetMode.DefaultProfile ? T("target-default") : target.Describe()));
        if (dryRun) Console.WriteLine(T("apply-dryrun"));
        if (target.Mode == UserTargetMode.LoggedOnUser) Console.WriteLine(T("user-logged-on"));
        if (target.Mode is UserTargetMode.OfflineUser or UserTargetMode.DefaultProfile) Console.WriteLine(T("user-offline"));

        var install = args.Has("install");
        WingetClient? winget = install ? new WingetClient() : null;
        var inventory = winget?.ListInstalled() ?? [];
        var plan = install
            ? ApplyPlanner.Plan(modules, inventory, installMissing: true)
            : ApplyPlanner.Plan(modules, modules.Where(m => m.WingetId is not null).Select(m => new InstalledPackage(m.WingetId!)).ToList(), false);

        string? backupDir = null;
        if (!args.Has("no-backup") && !dryRun)
        {
            var root = target.Mode == UserTargetMode.DefaultProfile
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Win-ProfileKit", "Backups")
                : PathText.Join(target.Context.LocalAppData, "Win-ProfileKit", "Backups");
            backupDir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        }

        var options = new ApplyOptions
        {
            DryRun = dryRun,
            BackupDirectory = backupDir,
            IncludeMachineScope = !args.Has("no-machine"),
        };

        var service = new ApplyService();
        var postApply = new List<string>();
        var problems = 0;
        foreach (var item in plan)
        {
            var label = item.Action switch
            {
                PlanAction.InstallAndConfigure => T("plan-install"),
                PlanAction.ConfigureWithoutProgram => T("plan-noprogram"),
                _ => T("plan-configure"),
            };
            Console.WriteLine($"{item.Module.Id} – {label}");

            if (item.Action == PlanAction.InstallAndConfigure && !dryRun && winget is not null)
            {
                Console.WriteLine(T("installing", item.Module.WingetId!));
                var code = winget.Install(item.Module.WingetId!);
                Console.WriteLine(T("install-result", WingetExitCodes.Describe(code)));
                if (!WingetExitCodes.IsSuccess(code)) problems++;
            }

            var rule = package.ReadModule(item.Module.Id);
            if ((target.Mode is UserTargetMode.CurrentUser or UserTargetMode.LoggedOnUser) && !dryRun)
            {
                var running = rule.Processes.Where(p => Process.GetProcessesByName(p).Length > 0).ToList();
                if (running.Count > 0) Console.Error.WriteLine(T("warning", T("processes-running", string.Join(", ", running))));
            }

            var report = service.ApplyModule(package, item.Module.Id, manifest.Source, target.Context, target.Registry, options);
            Console.WriteLine(T("apply-module", item.Module.Id, report.FilesWritten, report.ValuesWritten));
            if (args.Has("verbose") || dryRun)
                foreach (var a in report.Actions) Console.WriteLine($"      {a.Kind,-13} {a.Target}{(a.Detail is null ? "" : $"  ({a.Detail})")}");
            foreach (var w in report.Warnings) { Console.Error.WriteLine(T("warning", w)); problems++; }
            postApply.AddRange(rule.PostApply);
        }

        if (!dryRun && target.IsCurrentUser && postApply.Count > 0)
        {
            var applied = LiveSettings.Apply(postApply);
            if (applied.Count > 0) Console.WriteLine(T("live-applied", string.Join(", ", applied)));
        }

        Console.WriteLine(backupDir is null ? T("apply-done-nobackup") : T("apply-done", backupDir));
        return problems > 0 ? ExitCodes.Partial : ExitCodes.Ok;
    }

    private UserTarget OpenApplyTarget(bool dryRun)
    {
        var chosen = new[] { "current-user", "user", "default-user", "create-user" }.Where(args.Has).ToList();
        if (chosen.Count != 1)
            throw new CliException(T("missing-option", "--current-user | --user <Name> | --default-user | --create-user <Name>"));

        var writable = !dryRun;
        switch (chosen[0])
        {
            case "current-user":
                return UserTarget.OpenCurrent();
            case "default-user":
                return UserTarget.OpenDefaultProfile(writable);
            case "create-user":
                var newUser = args.Require("create-user");
                if (dryRun)
                {
                    // Im Trockenlauf wird nichts angelegt – ohne vorhandenes Profil zeigt der Plan das Default-Profil als Vorlage.
                    return UserProfiles.Find(newUser) is not null
                        ? UserTarget.OpenUser(newUser, writable: false)
                        : UserTarget.OpenDefaultProfile(writable: false);
                }
                var path = LocalUsers.Create(newUser, ReadPassword(newUser), args.Has("admin"));
                Console.WriteLine(T("user-created", newUser, path));
                return UserTarget.OpenUser(newUser, writable);
            default:
                return UserTarget.OpenUser(args.Require("user"), writable);
        }
    }

    // ---------------------------------------------------------------- learn

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private string SessionPath => args.Get("session") ?? LearnSession.DefaultPath(LocalAppData);

    public int LearnStart()
    {
        var scope = LearnScope.Default;
        if (args.Has("machine")) scope = scope.WithMachineScope();
        scope = scope.WithExtraNoise(SplitList(args.Get("exclude")));

        using var user = UserTarget.OpenCurrent();
        Console.WriteLine(T("learn-scanning", user.Describe()));
        var before = new SnapshotService().Take(user.Context, user.Registry, scope);
        new LearnSession { Scope = scope, Before = before }.Save(SessionPath);
        Console.WriteLine(T("learn-started", before.Files.Count, before.RegistryValueCount));
        return ExitCodes.Ok;
    }

    public int LearnDiff()
    {
        var diff = ComputeLearnDiff(out _);
        if (diff is null) return ExitCodes.Error;
        PrintDiff(diff);
        return ExitCodes.Ok;
    }

    public int LearnStop()
    {
        var id = args.Require("id");
        ProfilePackage.ValidateId(id);
        var name = args.Get("name") ?? id;

        var target = args.Get("out") ?? Path.Combine(LocalAppData, "Win-ProfileKit", "rules", id + ".json");
        var existing = LoadCatalog().Find(id);
        if ((existing is not null || File.Exists(target)) && !args.Has("force"))
            throw new CliException(T("learn-exists", id, existing?.DisplayName ?? target));

        var diff = ComputeLearnDiff(out _);
        if (diff is null) return ExitCodes.Error;
        PrintDiff(diff);
        if (diff.Files.All(f => f.Kind == ChangeKind.Removed) && diff.Registry.All(r => r.Kind == ChangeKind.Removed))
        {
            Console.WriteLine(T("learn-empty"));
            return ExitCodes.Partial;
        }

        var rule = RuleProposer.Propose(diff, id, name, args.Get("winget"));
        RuleCatalog.Validate(rule);
        Json.Write(target, rule);
        Console.WriteLine();
        Console.WriteLine(T("learn-proposal"));
        Console.WriteLine(File.ReadAllText(target).TrimEnd());
        Console.WriteLine();
        Console.WriteLine(T("learn-written", target));
        return ExitCodes.Ok;
    }

    private SnapshotDiff? ComputeLearnDiff(out LearnSession? session)
    {
        session = LearnSession.TryLoad(SessionPath);
        if (session is null)
        {
            Console.Error.WriteLine(T("learn-no-session"));
            return null;
        }
        using var user = UserTarget.OpenCurrent();
        Console.WriteLine(T("learn-scanning", user.Describe()));
        var after = new SnapshotService().Take(user.Context, user.Registry, session.Scope);
        return SnapshotDiff.Compare(session.Before, after);
    }

    private void PrintDiff(SnapshotDiff diff)
    {
        var limit = args.Has("verbose") ? int.MaxValue : 40;
        static string Mark(ChangeKind k) => k switch { ChangeKind.Added => "+", ChangeKind.Removed => "-", _ => "~" };

        Console.WriteLine(T("learn-files", diff.Files.Count));
        foreach (var f in diff.Files.Take(limit)) Console.WriteLine($"  {Mark(f.Kind)} {f.Path}");
        if (diff.Files.Count > limit) Console.WriteLine(T("learn-more", diff.Files.Count - limit));

        Console.WriteLine(T("learn-registry", diff.Registry.Count));
        foreach (var r in diff.Registry.Take(limit)) Console.WriteLine($"  {Mark(r.Kind)} {r.FullName}");
        if (diff.Registry.Count > limit) Console.WriteLine(T("learn-more", diff.Registry.Count - limit));
    }

    private static IEnumerable<string> SplitList(string? value) =>
        (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ---------------------------------------------------------- user create

    public int CreateUser()
    {
        if (args.Positional.Count < 3) throw new CliException(T("missing-option", "<Name>"));
        var name = args.Positional[2];
        var path = LocalUsers.Create(name, ReadPassword(name), args.Has("admin"));
        Console.WriteLine(T("user-created", name, path));
        return ExitCodes.Ok;
    }

    private string? ReadPassword(string userName)
    {
        if (args.Has("password-stdin")) return Console.In.ReadLine();
        if (Console.IsInputRedirected) return null;

        Console.Write(T("password-prompt", userName));
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            sb.Append(key.KeyChar);
        }
        Console.WriteLine();
        return sb.Length == 0 ? null : sb.ToString();
    }

    // -------------------------------------------------------------- helpers

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / (1024.0 * 1024):0.0} MB",
    };

    private static void WriteReadme(ProfilePackage package, ProfileManifest manifest)
    {
        var de = Language == "de";
        var file = Path.Combine(package.Root, de ? "LIESMICH.txt" : "README.txt");
        var text = de
            ? $"""
              Win-ProfileKit-Profil „{manifest.Name}“
              Erfasst: {manifest.CreatedUtc.LocalDateTime:g} von {manifest.Source.UserName}@{manifest.Source.Machine}
              Module: {string.Join(", ", manifest.Modules.Select(m => m.Id))}

              Anwenden auf den angemeldeten Benutzer:
                profilekit apply --profile "<dieser Ordner>" --current-user --install

              {(manifest.ContainsKeyfiles ? "Achtung: enthält Lizenz-Keyfiles – nicht öffentlich weitergeben." : "")}
              """
            : $"""
              Win-ProfileKit profile "{manifest.Name}"
              Captured: {manifest.CreatedUtc.LocalDateTime:g} from {manifest.Source.UserName}@{manifest.Source.Machine}
              Modules: {string.Join(", ", manifest.Modules.Select(m => m.Id))}

              Apply to the logged-on user:
                profilekit apply --profile "<this folder>" --current-user --install

              {(manifest.ContainsKeyfiles ? "Caution: contains license keyfiles – do not share publicly." : "")}
              """;
        File.WriteAllText(file, text.TrimEnd() + Environment.NewLine);
    }
}
