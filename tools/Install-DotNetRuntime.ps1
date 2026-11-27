<#
.SYNOPSIS
  Prüft, ob die .NET-Runtime (x64) für Win-ProfileKit vorhanden ist, und installiert sie bei Bedarf.
  Checks whether the .NET runtime (x64) required by Win-ProfileKit is present and installs it if needed.

.DESCRIPTION
  Gehört zum framework-abhängigen Paket (Win-ProfileKit-<Version>-win-x64-net10.zip).
  Gesucht wird dort, wo auch profilekit.exe / profilekit-gui.exe die Runtime suchen:
  DOTNET_ROOT(_X64), registrierter Installationsort und %ProgramFiles%\dotnet.
  Fehlt sie, wird der offizielle Installer von Microsoft geladen, die Signatur geprüft und
  still installiert (Administratorrechte nötig – das Skript fordert sie bei Bedarf an).

  Exitcodes: 0 Runtime vorhanden/installiert, 1 Fehler, 2 Runtime fehlt (nur mit -CheckOnly).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Install-DotNetRuntime.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Install-DotNetRuntime.ps1 -CheckOnly
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+$')] [string] $Channel = '10.0',
    [switch] $CheckOnly
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$de = (Get-UICulture).TwoLetterISOLanguageName -eq 'de'
function Say([string] $German, [string] $English, [string] $Color = 'Gray') {
    Write-Host ($(if ($de) { $German } else { $English })) -ForegroundColor $Color
}

$major = [int]($Channel.Split('.')[0])

function Get-DotNetRoots {
    $roots = @($env:DOTNET_ROOT_X64, $env:DOTNET_ROOT)
    try {
        $hklm32 = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry32')
        $key = $hklm32.OpenSubKey('SOFTWARE\dotnet\Setup\InstalledVersions\x64')
        if ($key) { $roots += [string]$key.GetValue('InstallLocation') }
    } catch { }
    $programFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
    $roots += (Join-Path $programFiles 'dotnet')
    $roots | Where-Object { $_ } | Select-Object -Unique
}

function Find-Runtime {
    foreach ($root in Get-DotNetRoots) {
        $shared = Join-Path $root 'shared\Microsoft.NETCore.App'
        if (-not (Test-Path $shared)) { continue }
        $found = Get-ChildItem $shared -Directory |
            Where-Object { $_.Name -match '^(\d+)\.\d+\.\d+$' -and [int]$Matches[1] -eq $major } |
            Sort-Object { [version]$_.Name } -Descending |
            Select-Object -First 1
        if ($found) { return "$($found.Name) ($root)" }
    }
    return $null
}

function Install-Runtime {
    $url = "https://aka.ms/dotnet/$Channel/dotnet-runtime-win-x64.exe"
    $installer = Join-Path $env:TEMP "dotnet-runtime-$Channel-win-x64.exe"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Say "Lade $url …" "Downloading $url …"
        Invoke-WebRequest -Uri $url -OutFile $installer -UseBasicParsing

        $sig = Get-AuthenticodeSignature $installer
        if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            throw "Signatur ungültig / invalid signature ($($sig.Status), $($sig.SignerCertificate.Subject))"
        }

        Say 'Installiere .NET-Runtime (still) …' 'Installing .NET runtime (quiet) …'
        $setup = Start-Process -FilePath $installer -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
        # 3010/1641: erfolgreich, Neustart empfohlen
        if ($setup.ExitCode -notin 0, 3010, 1641) { throw "Installer-Exitcode $($setup.ExitCode)" }
    } catch {
        Say "Installation fehlgeschlagen: $($_.Exception.Message)" "Installation failed: $($_.Exception.Message)" Red
        Say "Manuell installieren: https://dotnet.microsoft.com/download/dotnet/$Channel (.NET Runtime, x64)" `
            "Install manually: https://dotnet.microsoft.com/download/dotnet/$Channel (.NET Runtime, x64)" Yellow
        exit 1
    } finally {
        Remove-Item $installer -Force -ErrorAction SilentlyContinue
    }
}

$present = Find-Runtime
if ($present) {
    Say ".NET-Runtime $major gefunden: $present" ".NET runtime $major found: $present" Green
    exit 0
}

Say ".NET-Runtime $major (Microsoft.NETCore.App, x64) ist nicht installiert." `
    ".NET runtime $major (Microsoft.NETCore.App, x64) is not installed." Yellow
if ($CheckOnly) { exit 2 }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    Install-Runtime
} else {
    Say 'Für die Installation werden Administratorrechte angefordert …' 'Requesting administrator rights for the installation …'
    try {
        $proc = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Channel', $Channel)
    } catch {
        Say "Installation abgebrochen: $($_.Exception.Message)" "Installation cancelled: $($_.Exception.Message)" Red
        exit 1
    }
    if ($proc.ExitCode -ne 0) {
        Say "Installation fehlgeschlagen (Exitcode $($proc.ExitCode))." "Installation failed (exit code $($proc.ExitCode))." Red
        exit 1
    }
}

$present = Find-Runtime
if (-not $present) {
    Say 'Runtime nach der Installation nicht gefunden.' 'Runtime not found after installation.' Red
    exit 1
}
Say ".NET-Runtime installiert: $present" ".NET runtime installed: $present" Green
exit 0
