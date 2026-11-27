<#
  Startet die Oberfläche und prüft, ob sie läuft (fängt Fehler beim Laden von XAML,
  fehlende Abhängigkeiten der Windows App SDK und Abstürze beim Start ab).
  -Launcher: $Exe ist der Starter im Root eines Release-Pakets, der app\profilekit-gui.exe startet.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [int] $Seconds = 20,
    [switch] $Launcher
)
$ErrorActionPreference = 'Stop'
$exePath = (Resolve-Path $Exe).Path
$exeDir = Split-Path $exePath
# CLI und Regelkatalog liegen neben der Oberfläche oder (Release-Paket mit app\) eine Ebene darüber
$kitHome = if (Test-Path (Join-Path $exeDir 'profilekit.exe')) { $exeDir } else { Split-Path $exeDir }
foreach ($f in @('profilekit.exe', 'rules\apps\7zip.json', 'rules\no-settings.list')) {
    $p = Join-Path $kitHome $f
    if (-not (Test-Path $p)) { Write-Host "FEHLT: $p" -ForegroundColor Red; exit 1 }
}

$start = Get-Date
$proc = Start-Process -FilePath $exePath -PassThru -WorkingDirectory $exeDir
if ($Launcher) {
    $null = $proc.Handle
    if (-not $proc.WaitForExit(120000)) { Write-Host 'Starter hat sich nicht beendet.' -ForegroundColor Red; exit 1 }
    if ($proc.ExitCode -ne 0) { Write-Host "Starter-Exitcode $($proc.ExitCode)." -ForegroundColor Red; exit 1 }
    $guiPath = Join-Path $exeDir 'app\profilekit-gui.exe'
    $proc = Get-Process profilekit-gui -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $guiPath } | Select-Object -First 1
    if (-not $proc) { Write-Host "Starter hat $guiPath nicht gestartet." -ForegroundColor Red; exit 1 }
    Write-Host "Starter hat $guiPath gestartet."
}
Start-Sleep -Seconds $Seconds
$proc.Refresh()

if ($proc.HasExited) {
    Write-Host "Oberfläche hat sich nach dem Start beendet (Exitcode $($proc.ExitCode))." -ForegroundColor Red
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $start } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error', 'Windows Error Reporting' } |
        Select-Object -First 5 | ForEach-Object { Write-Host "--- $($_.ProviderName)"; Write-Host $_.Message }
    exit 1
}

$title = $proc.MainWindowTitle
Write-Host "Oberfläche läuft seit $Seconds s (PID $($proc.Id), Fenstertitel '$title', Arbeitsspeicher $([math]::Round($proc.WorkingSet64 / 1MB)) MB)."
Stop-Process -Id $proc.Id -Force
if ($title -ne 'Win-ProfileKit') { Write-Host "Warnung: unerwarteter Fenstertitel '$title' (ohne interaktive Sitzung möglich)." -ForegroundColor Yellow }
Write-Host 'Starttest bestanden.' -ForegroundColor Green
exit 0
