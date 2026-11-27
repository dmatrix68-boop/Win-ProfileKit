<#
  Startet die Oberfläche und prüft, ob sie läuft (fängt Fehler beim Laden von XAML,
  fehlende Abhängigkeiten der Windows App SDK und Abstürze beim Start ab).
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [int] $Seconds = 20
)
$ErrorActionPreference = 'Stop'
$exePath = (Resolve-Path $Exe).Path
foreach ($f in @('profilekit.exe', 'rules\apps\7zip.json', 'rules\no-settings.list')) {
    $p = Join-Path (Split-Path $exePath) $f
    if (-not (Test-Path $p)) { Write-Host "FEHLT: $f" -ForegroundColor Red; exit 1 }
}

$start = Get-Date
$proc = Start-Process -FilePath $exePath -PassThru -WorkingDirectory (Split-Path $exePath)
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
