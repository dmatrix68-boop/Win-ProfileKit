using ProfileKit.Core.Paths;

namespace ProfileKit.Core.Learning;

/// <summary>
/// Was der Lernmodus beobachtet und was er als Rauschen ignoriert. Ausschlüsse sind Glob-Muster
/// auf tokenisierte Pfade bzw. auf <c>Schlüssel\Wertname</c>.
/// </summary>
public sealed record LearnScope
{
    public List<string> FileRoots { get; init; } = ["%APPDATA%", "%LOCALAPPDATA%", "%LOCALAPPDATALOW%", "%USERPROFILE%"];

    /// <summary>Unter %USERPROFILE% nur Dateien und Punkt-Ordner direkt im Profil (z. B. .gitconfig, .config) – keine Dokumente.</summary>
    public bool UserProfileDotEntriesOnly { get; init; } = true;

    public List<string> RegistryRoots { get; init; } = [@"HKCU\Software", @"HKCU\Control Panel"];

    public List<string> FileNoise { get; init; } =
    [
        @"%LOCALAPPDATA%\Temp\**",
        @"%LOCALAPPDATA%\Win-ProfileKit\**",
        @"%LOCALAPPDATA%\CrashDumps\**",
        @"%LOCALAPPDATA%\D3DSCache\**",
        @"%LOCALAPPDATA%\Microsoft\Windows\**",
        @"%LOCALAPPDATA%\Microsoft\Edge\**",
        @"%LOCALAPPDATA%\Microsoft\Windows Sidebar\**",
        @"%LOCALAPPDATA%\Microsoft\Olk\**",
        @"%LOCALAPPDATA%\Microsoft\OneDrive\logs\**",
        @"%LOCALAPPDATA%\Packages\*\AC\**",
        @"%LOCALAPPDATA%\Packages\*\TempState\**",
        @"%LOCALAPPDATA%\Packages\*\LocalCache\**",
        @"%LOCALAPPDATA%\Packages\Microsoft.Windows.*\**",
        @"%LOCALAPPDATA%\Packages\MicrosoftWindows.*\**",
        @"%APPDATA%\Microsoft\Windows\Recent\**",
        @"%APPDATA%\Microsoft\Windows\Themes\CachedFiles\**",
        @"%APPDATA%\Microsoft\Protect\**",
        @"%APPDATA%\Microsoft\Credentials\**",
        @"%APPDATA%\Microsoft\Crypto\**",
        @"%APPDATA%\Microsoft\SystemCertificates\**",
        @"%USERPROFILE%\NTUSER.DAT*",
        @"%USERPROFILE%\ntuser.*",
        @"**\Cache\**",
        @"**\Code Cache\**",
        @"**\GPUCache\**",
        @"**\DawnCache\**",
        @"**\Crashpad\**",
        @"**\CrashReports\**",
        @"**\logs\**",
        @"**\EBWebView\**",
        @"*.log",
        @"*.log.*",
        @"*.tmp",
        @"*.etl",
        @"*.lock",
        @"LOCK",
        @"*-journal",
        @"*.ldb",
    ];

    public List<string> RegistryNoise { get; init; } =
    [
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\CloudStore\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\DeliveryOptimization\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Diagnostics\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Store\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\**",
        @"HKCU\Software\Microsoft\Windows\Shell\**",
        @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\**",
        @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\TaskManager\**",
        @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\HostActivityManager\**",
        @"HKCU\Software\Microsoft\Office\**\Common\**",
        @"HKCU\Software\Microsoft\Edge\**",
        @"HKCU\Software\Microsoft\OneDrive\**",
        @"HKCU\Software\Microsoft\Input\**",
        @"HKCU\Software\Microsoft\InputPersonalization\**",
        @"HKCU\Software\Microsoft\IdentityCRL\**",
        @"HKCU\Software\Microsoft\Speech_OneCore\**",
        @"HKCU\Software\Microsoft\GameBar\**",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\**",
        @"HKCU\Software\Classes\Local Settings\**",
        @"HKCU\Software\Win-ProfileKit\**",
    ];

    /// <summary>Standard-Umfang für den angemeldeten Benutzer.</summary>
    public static LearnScope Default { get; } = new();

    /// <summary>Zusätzlich maschinenweite Orte (Programmordner, ProgramData, HKLM\SOFTWARE).</summary>
    public LearnScope WithMachineScope() => this with
    {
        FileRoots = [.. FileRoots, "%PROGRAMDATA%", "%PROGRAMFILES%", "%PROGRAMFILES(X86)%"],
        RegistryRoots = [.. RegistryRoots, @"HKLM\SOFTWARE"],
        FileNoise = [.. FileNoise, @"%PROGRAMDATA%\Microsoft\**", @"%PROGRAMDATA%\Packages\**", @"%PROGRAMDATA%\Package Cache\**",
                     @"%PROGRAMFILES%\WindowsApps\**", @"%PROGRAMFILES%\Windows Defender*\**"],
        RegistryNoise = [.. RegistryNoise, @"HKLM\SOFTWARE\Microsoft\**", @"HKLM\SOFTWARE\Classes\**", @"HKLM\SOFTWARE\WOW6432Node\Microsoft\**",
                         @"HKLM\SOFTWARE\WOW6432Node\Classes\**"],
    };

    /// <summary>Zusätzliche Ausschlüsse (z. B. per Kommandozeile).</summary>
    public LearnScope WithExtraNoise(IEnumerable<string> patterns)
    {
        var list = patterns.ToList();
        return list.Count == 0 ? this : this with
        {
            FileNoise = [.. FileNoise, .. list.Where(p => p.StartsWith('%') || !p.StartsWith("HK", StringComparison.OrdinalIgnoreCase))],
            RegistryNoise = [.. RegistryNoise, .. list.Where(p => p.StartsWith("HK", StringComparison.OrdinalIgnoreCase))],
        };
    }

    internal ExcludeSet FileExcludes() => new(FileNoise);
    internal ExcludeSet RegistryExcludes() => new(RegistryNoise);
}
