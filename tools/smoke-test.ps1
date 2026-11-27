<#
  Integrationstest auf einem echten Windows (CI-Runner oder Test-VM, als Administrator).
  Legt einen Testbenutzer an, erfasst ein Profil vom aktuellen Benutzer und wendet es
  offline auf den Testbenutzer und auf das Default-Profil an.

  Achtung: verändert das System (Benutzer pktest1, HKCU\Software\ProfileKitSmoke,
  Default-Profil). Nur auf Wegwerf-Systemen ausführen.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Work = (Join-Path $env:RUNNER_TEMP 'pk-smoke')
)
$ErrorActionPreference = 'Stop'
$rules = Join-Path $PSScriptRoot 'smoke'
$failures = 0

function Invoke-Pk([string[]] $PkArgs, [int[]] $Allowed = @(0)) {
    Write-Host "`n> profilekit $($PkArgs -join ' ')" -ForegroundColor Cyan
    & $Exe @PkArgs --rules $rules --lang de
    if ($Allowed -notcontains $LASTEXITCODE) {
        Write-Host "FEHLER: Exitcode $LASTEXITCODE" -ForegroundColor Red
        $script:failures++
    }
}

function Assert-True([bool] $Condition, [string] $Message) {
    if ($Condition) { Write-Host "OK   $Message" -ForegroundColor Green }
    else { Write-Host "FAIL $Message" -ForegroundColor Red; $script:failures++ }
}

Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item $Work -ItemType Directory | Out-Null

# --- Quelle vorbereiten ---------------------------------------------------------
$appDir = Join-Path $env:APPDATA 'ProfileKitSmoke'
New-Item $appDir -ItemType Directory -Force | Out-Null
Set-Content (Join-Path $appDir 'conf.ini') "last=$env:USERPROFILE\Documents" -Encoding Unicode
Set-Content (Join-Path $appDir 'run.log') 'soll nicht mit' -Encoding UTF8
New-Item 'HKCU:\Software\ProfileKitSmoke\Sub' -Force | Out-Null
Set-ItemProperty 'HKCU:\Software\ProfileKitSmoke' -Name LastDir -Value "$env:USERPROFILE\Downloads"
Set-ItemProperty 'HKCU:\Software\ProfileKitSmoke\Sub' -Name Width -Value 800 -Type DWord
Set-ItemProperty 'HKCU:\Software\Microsoft\Clipboard' -Name EnableClipboardHistory -Value 1 -Type DWord

# --- Grundbefehle -------------------------------------------------------------------
Invoke-Pk @('help')
Invoke-Pk @('catalog')
Invoke-Pk @('users')

# --- Erfassen -------------------------------------------------------------------------
$profileDir = Join-Path $Work 'Smoke'
Invoke-Pk @('capture', '--out', $profileDir, '--name', 'Smoke', '--modules', 'smoke.test,windows.*', '--zip') -Allowed 0,2
Assert-True (Test-Path (Join-Path $profileDir 'profile.json')) 'profile.json geschrieben'
Assert-True (Test-Path "$profileDir.pkprofile") 'Archiv geschrieben'
Assert-True (-not (Test-Path (Join-Path $profileDir 'modules\smoke.test\files\APPDATA\ProfileKitSmoke\run.log'))) 'Ausschluss *.log wirkt'
Invoke-Pk @('info', '--profile', "$profileDir.pkprofile")

# --- Benutzer anlegen und offline anwenden ----------------------------------------
$user = 'pktest1'
'Pk!Smoke-2026' | & $Exe user create $user --password-stdin --lang de
Assert-True ($LASTEXITCODE -eq 0) "Benutzer $user angelegt"

Invoke-Pk @('apply', '--profile', "$profileDir.pkprofile", '--user', $user, '--dry-run')
Invoke-Pk @('apply', '--profile', $profileDir, '--user', $user, '--verbose') -Allowed 0,2

$target = (Get-CimInstance Win32_UserProfile | Where-Object { $_.LocalPath -like "*\$user" }).LocalPath
Assert-True ([bool]$target) "Profil von $user existiert ($target)"
$conf = Join-Path $target 'AppData\Roaming\ProfileKitSmoke\conf.ini'
Assert-True (Test-Path $conf) 'Datei beim Zielbenutzer angekommen'
if (Test-Path $conf) {
    $content = Get-Content $conf -Raw -Encoding Unicode
    Assert-True ($content -like "*$target\Documents*") "Pfad umgeschrieben: $($content.Trim())"
}

reg.exe load HKU\pkverify (Join-Path $target 'NTUSER.DAT') | Out-Null
try {
    $last = (Get-ItemProperty 'Registry::HKEY_USERS\pkverify\Software\ProfileKitSmoke').LastDir
    $width = (Get-ItemProperty 'Registry::HKEY_USERS\pkverify\Software\ProfileKitSmoke\Sub').Width
    $clip = (Get-ItemProperty 'Registry::HKEY_USERS\pkverify\Software\Microsoft\Clipboard' -ErrorAction SilentlyContinue).EnableClipboardHistory
    Assert-True ($last -eq "$target\Downloads") "Registry-String umgeschrieben: $last"
    Assert-True ($width -eq 800) 'DWORD übertragen'
    Assert-True ($clip -eq 1) 'Zwischenablageverlauf übertragen'
}
finally {
    [GC]::Collect()
    reg.exe unload HKU\pkverify | Out-Null
}

# --- Default-Profil und aktueller Benutzer ----------------------------------------
Invoke-Pk @('apply', '--profile', $profileDir, '--default-user', '--only', 'windows.clipboard', '--no-backup') -Allowed 0,2
Invoke-Pk @('apply', '--profile', $profileDir, '--current-user', '--only', 'smoke.test', '--dry-run')

if ($failures -gt 0) { Write-Host "`n$failures Fehler" -ForegroundColor Red; exit 1 }
Write-Host "`nAlle Smoke-Tests bestanden." -ForegroundColor Green
