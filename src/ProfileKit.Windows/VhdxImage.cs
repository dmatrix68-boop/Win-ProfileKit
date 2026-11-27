using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using ProfileKit.Core.Inventory;
using ProfileKit.Core.Paths;
using static ProfileKit.Core.Text.Localizer;

namespace ProfileKit.Windows;

/// <summary>
/// VHDX/VHD einer heruntergefahrenen VM als Quelle (SPEC 4.6, Punkt 3). Das Image wird schreibgeschützt eingebunden;
/// Hives werden in einen temporären Ordner kopiert und von dort geladen, weil Windows beim Laden Protokolle schreibt.
/// Beim Freigeben werden Hives entladen, das Image getrennt (sofern wir es eingebunden haben) und Kopien gelöscht.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VhdxImage : IDisposable
{
    private readonly bool _attachedByUs;
    private readonly string _tempDir;
    private HiveMount? _software;
    private bool _disposed;

    private VhdxImage(string imagePath, string root, bool attachedByUs, string tempDir)
    {
        ImagePath = imagePath;
        Root = root;
        _attachedByUs = attachedByUs;
        _tempDir = tempDir;
        MachineName = GuessMachineName(imagePath);
    }

    public string ImagePath { get; }

    /// <summary>Wurzel der Windows-Partition, z. B. <c>F:\</c> oder <c>\\?\Volume{…}\</c>.</summary>
    public string Root { get; }

    /// <summary>Name des Quellrechners – aus dem Dateinamen der VHDX (bei Hyper-V meist der VM-Name).</summary>
    public string MachineName { get; }

    public IReadOnlyList<OfflineUser> Users { get; private set; } = [];

    public static VhdxImage Mount(string imagePath)
    {
        if (!UserProfiles.IsAdministrator()) throw new UnauthorizedAccessException(T("vhdx-needs-admin"));
        var full = Path.GetFullPath(imagePath);
        if (!File.Exists(full)) throw new FileNotFoundException(T("vhdx-not-found", full), full);

        var (roots, alreadyAttached) = Attach(full);
        var tempDir = Path.Combine(Path.GetTempPath(), "Win-ProfileKit", "vhdx-" + Guid.NewGuid().ToString("N")[..8]);
        var root = roots.FirstOrDefault(OfflineImageLayout.IsWindowsRoot);
        if (root is null)
        {
            if (!alreadyAttached) Detach(full);
            throw new InvalidOperationException(T("vhdx-no-windows", full, roots.Count == 0 ? "-" : string.Join(", ", roots)));
        }

        var image = new VhdxImage(full, root, !alreadyAttached, tempDir);
        try
        {
            image.LoadSoftware();
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private void LoadSoftware()
    {
        Directory.CreateDirectory(_tempDir);
        var copy = CopyHive(OfflineImageLayout.SoftwareHive(Root), Path.Combine(_tempDir, "SOFTWARE"));
        _software = HiveMount.Load(copy);

        var profileList = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var root = _software.OpenRoot(writable: false))
        using (var list = root.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion\ProfileList"))
        {
            foreach (var sid in list?.GetSubKeyNames() ?? [])
            {
                if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal)) continue;
                using var key = list!.OpenSubKey(sid);
                if (key?.GetValue("ProfileImagePath", null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) is string path)
                    profileList[sid] = path;
            }
        }
        Users = OfflineImageLayout.FindUsers(Root, profileList);
    }

    /// <summary>Öffnet einen Benutzer des Images nur lesend als Quelle für capture/collect.</summary>
    public UserTarget OpenUser(string userName)
    {
        var user = Users.FirstOrDefault(u => u.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(T("vhdx-user-unknown", userName, string.Join(", ", Users.Select(u => u.UserName))));

        var userTemp = Path.Combine(_tempDir, user.UserName);
        Directory.CreateDirectory(userTemp);
        var userHive = HiveMount.Load(CopyHive(PathText.Join(user.MountedProfilePath, "NTUSER.DAT"), Path.Combine(userTemp, "NTUSER.DAT")));
        HiveMount? classesHive = null;
        try
        {
            var usrClass = PathText.Join(user.MountedProfilePath, "AppData", "Local", "Microsoft", "Windows", "UsrClass.dat");
            if (File.Exists(usrClass))
                classesHive = HiveMount.Load(CopyHive(usrClass, Path.Combine(userTemp, "UsrClass.dat")), userHive.MountName + "_Classes");

            var context = new UserContext
            {
                UserName = user.UserName,
                Sid = user.Sid,
                ProfilePath = user.MountedProfilePath,
                ProgramData = PathText.Join(Root, "ProgramData"),
                ProgramFiles = PathText.Join(Root, "Program Files"),
                ProgramFilesX86 = PathText.Join(Root, "Program Files (x86)"),
            };
            var registry = new WindowsRegistryBackend(userHive.OpenRoot(writable: false), classesHive?.OpenRoot(writable: false),
                ownsUserRoot: true, offlineSoftware: _software!.OpenRoot(writable: false));
            var target = new UserTarget(context, UserTargetMode.OfflineImage, registry)
            {
                OriginalProfilePath = user.OriginalProfilePath,
                SourceMachine = MachineName,
            };
            if (classesHive is not null) target.AddResource(classesHive);
            target.AddResource(userHive);
            return target;
        }
        catch
        {
            classesHive?.Dispose();
            userHive.Dispose();
            throw;
        }
    }

    /// <summary>Kopiert einen Hive samt Transaktionsprotokollen (.LOG1/.LOG2) und liefert den Pfad der Kopie.</summary>
    private static string CopyHive(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);
        File.SetAttributes(destination, FileAttributes.Normal);
        foreach (var log in new[] { ".LOG", ".LOG1", ".LOG2" })
        {
            if (!File.Exists(source + log)) continue;
            File.Copy(source + log, destination + log, overwrite: true);
            File.SetAttributes(destination + log, FileAttributes.Normal);
        }
        return destination;
    }

    private static readonly Regex CheckpointSuffix = new(@"_[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$", RegexOptions.CultureInvariant);

    private static string GuessMachineName(string imagePath) =>
        CheckpointSuffix.Replace(Path.GetFileNameWithoutExtension(imagePath), string.Empty);

    // ----------------------------------------------------------- Einbinden

    private static (List<string> Roots, bool AlreadyAttached) Attach(string path)
    {
        var storageType = path.EndsWith(".vhd", StringComparison.OrdinalIgnoreCase) ? "VHD" : "VHDX";
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            $p = '{{path.Replace("'", "''")}}'
            $img = Get-DiskImage -ImagePath $p -StorageType {{storageType}} -ErrorAction SilentlyContinue
            $already = [bool]($img -and $img.Attached)
            if (-not $already) { $img = Mount-DiskImage -ImagePath $p -StorageType {{storageType}} -Access ReadOnly -PassThru }
            "ALREADY=$already"
            $vols = @()
            for ($i = 0; $i -lt 20; $i++) {
                try {
                    $img = Get-DiskImage -ImagePath $p -StorageType {{storageType}}
                    $vols = @($img | Get-Disk | Get-Partition | Get-Volume -ErrorAction SilentlyContinue)
                } catch { $vols = @() }
                if ($vols.Count -gt 0) { break }
                Start-Sleep -Milliseconds 500
            }
            foreach ($v in $vols) {
                if ($v.DriveLetter) { "ROOT=$($v.DriveLetter):\" } elseif ($v.Path) { "ROOT=$($v.Path)" }
            }
            """;
        var (code, output, error) = RunPowerShell(script);
        if (code != 0)
        {
            var hint = error.Contains("used by another process", StringComparison.OrdinalIgnoreCase)
                       || error.Contains("von einem anderen Prozess verwendet", StringComparison.OrdinalIgnoreCase)
                ? " " + T("vhdx-in-use")
                : string.Empty;
            throw new InvalidOperationException(T("vhdx-mount-failed", path, error.Trim()) + hint);
        }
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var already = lines.Any(l => l.Equals("ALREADY=True", StringComparison.OrdinalIgnoreCase));
        var roots = lines.Where(l => l.StartsWith("ROOT=", StringComparison.Ordinal)).Select(l => l[5..]).ToList();
        return (roots, already);
    }

    private static void Detach(string path)
    {
        var storageType = path.EndsWith(".vhd", StringComparison.OrdinalIgnoreCase) ? "VHD" : "VHDX";
        var (code, _, error) = RunPowerShell($"$ProgressPreference = 'SilentlyContinue'; Dismount-DiskImage -ImagePath '{path.Replace("'", "''")}' -StorageType {storageType} | Out-Null");
        if (code != 0) Console.Error.WriteLine(T("warning", T("vhdx-detach-failed", path, error.Trim())));
    }

    private static (int Code, string Output, string Error) RunPowerShell(string script)
    {
        // -EncodedCommand statt "-Command -": Über stdin führt powershell.exe einen mehrzeiligen Block am Ende
        // des Skripts nicht aus, wenn keine Leerzeile folgt – die Volumes wurden so nie ausgegeben.
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded }) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("powershell.exe");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("Mount-DiskImage");
        }
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _software?.Dispose();
        if (_attachedByUs) Detach(ImagePath);
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Reste in %TEMP% sind unkritisch */ }
    }
}
