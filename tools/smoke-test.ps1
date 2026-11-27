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

# --- Lernmodus ------------------------------------------------------------------------
$session = Join-Path $Work 'learn-session.json'
$learned = Join-Path $Work 'learned.json'
Invoke-Pk @('learn', 'start', '--session', $session)
$learnDir = Join-Path $env:APPDATA 'ProfileKitLearn'
New-Item (Join-Path $learnDir 'profiles') -ItemType Directory -Force | Out-Null
Set-Content (Join-Path $learnDir 'settings.ini') 'theme=dark' -Encoding UTF8
Set-Content (Join-Path $learnDir 'profiles\default.json') '{}' -Encoding UTF8
New-Item 'HKCU:\Software\ProfileKitLearn\Window' -Force | Out-Null
Set-ItemProperty 'HKCU:\Software\ProfileKitLearn\Window' -Name Width -Value 1234 -Type DWord
Invoke-Pk @('learn', 'diff', '--session', $session)
Invoke-Pk @('learn', 'stop', '--session', $session, '--id', 'smoke.learned', '--name', 'Gelernt', '--winget', 'Test.Learned', '--out', $learned)
if (Test-Path $learned) {
    $rule = Get-Content $learned -Raw | ConvertFrom-Json
    Assert-True ($rule.files.path -contains '%APPDATA%\ProfileKitLearn') "Lernmodus: Ordner erkannt ($($rule.files.path -join ', '))"
    Assert-True ($rule.registry.key -contains 'HKCU\Software\ProfileKitLearn\Window') "Lernmodus: Registry erkannt ($($rule.registry.key -join ', '))"
    Assert-True ($rule.origin -eq 'LearnMode') 'Lernmodus: Herkunft gesetzt'
} else {
    Assert-True $false 'Lernmodus: Regel geschrieben'
}

# --- MCP-Server über stdio --------------------------------------------------------------
$psi = [Diagnostics.ProcessStartInfo]::new((Resolve-Path $Exe).Path)
$psi.ArgumentList.Add('mcp'); $psi.ArgumentList.Add('--rules'); $psi.ArgumentList.Add($rules)
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$mcp = [Diagnostics.Process]::Start($psi)
$requests = @(
    @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'smoke' } } },
    @{ jsonrpc = '2.0'; method = 'notifications/initialized' },
    @{ jsonrpc = '2.0'; id = 2; method = 'tools/list' },
    @{ jsonrpc = '2.0'; id = 3; method = 'tools/call'; params = @{ name = 'read_registry'; arguments = @{ key = 'HKCU\Software\ProfileKitSmoke'; recurse = $true } } },
    @{ jsonrpc = '2.0'; id = 4; method = 'tools/call'; params = @{ name = 'search_paths'; arguments = @{ query = 'ProfileKitSmoke' } } },
    @{ jsonrpc = '2.0'; id = 5; method = 'tools/call'; params = @{ name = 'read_file'; arguments = @{ path = 'C:\Windows\System32\drivers\etc\hosts' } } },
    @{ jsonrpc = '2.0'; id = 6; method = 'tools/call'; params = @{ name = 'propose_rule'; arguments = @{ rule = @{
        id = 'smoke.mcp'; displayName = 'Smoke MCP'; notes = 'via smoke test'
        files = @(@{ path = '%APPDATA%\ProfileKitSmoke' }); registry = @(@{ key = 'HKCU\Software\ProfileKitSmoke' }); excludes = @('*.log') } } } }
)
foreach ($r in $requests) { $mcp.StandardInput.WriteLine(($r | ConvertTo-Json -Compress -Depth 10)) }
$mcp.StandardInput.Close()
$out = $mcp.StandardOutput.ReadToEnd()
$mcp.WaitForExit(30000) | Out-Null
$responses = @{}
foreach ($line in ($out -split "`n" | Where-Object { $_.Trim() })) { $j = $line | ConvertFrom-Json; $responses[[int]$j.id] = $j }
Assert-True ($responses.Count -eq 6) "MCP: 6 Antworten auf 7 Nachrichten (Notification ohne Antwort) – erhalten: $($responses.Count)"
Assert-True ($responses[1].result.protocolVersion -eq '2025-06-18') 'MCP: initialize'
Assert-True ($responses[2].result.tools.Count -eq 7) "MCP: tools/list ($($responses[2].result.tools.Count) Werkzeuge)"
Assert-True ($responses[3].result.content[0].text -like '*LastDir*') 'MCP: read_registry liest HKCU'
Assert-True ($responses[4].result.content[0].text -like '*ProfileKitSmoke*') 'MCP: search_paths findet Ordner/Schlüssel'
Assert-True ($responses[5].result.isError -eq $true) 'MCP: Zugriff auf C:\Windows verweigert'
Assert-True ($responses[6].result.content[0].text -like '*"stored": true*') "MCP: propose_rule gespeichert"

Invoke-Pk @('ai', 'proposals')
Invoke-Pk @('ai', 'proposals', '--show', 'smoke.mcp')
Invoke-Pk @('ai', 'accept', 'smoke.mcp')
Assert-True (Test-Path (Join-Path $env:LOCALAPPDATA 'Win-ProfileKit\rules\smoke.mcp.json')) 'KI-Vorschlag als Regel übernommen'
Invoke-Pk @('ai', 'accept', 'all')

# --- KI-Status, Anmeldeinformationsverwaltung, MCP-Konfiguration ------------------------
'sk-or-dummy-smoke' | & $Exe ai key set --stdin --lang de
Assert-True ($LASTEXITCODE -eq 0) 'API-Key in der Anmeldeinformationsverwaltung gespeichert'
$status = & $Exe ai status --lang de | Out-String
Write-Host $status
Assert-True ($status -match 'OpenRouter: Key ja') 'ai status erkennt den gespeicherten Key'
& $Exe ai key remove --lang de | Out-Null
Assert-True ((& $Exe ai status --lang de | Out-String) -match 'OpenRouter: Key nein') 'API-Key entfernt'
$config = & $Exe mcp config | Out-String
Assert-True ($config -match 'win-profilekit') 'mcp config gibt Client-Konfiguration aus'

# --- Stapelmodus (Planung ohne KI) ----------------------------------------------------
$wingetFile = Join-Path $Work 'winget-export.json'
@{ '$schema' = 'https://aka.ms/winget-packages.schema.2.0.json'; Sources = @(@{ Packages = @(
    @{ PackageIdentifier = '7zip.7zip' }, @{ PackageIdentifier = 'Contoso.PhotoTool' }, @{ PackageIdentifier = 'Microsoft.DotNet.DesktopRuntime.10' }) }) } |
    ConvertTo-Json -Depth 6 | Set-Content $wingetFile -Encoding UTF8
$planOut = & $Exe ai discover --from-winget $wingetFile --dry-run --rules $rules --lang de | Out-String
Write-Host $planOut
Assert-True ($LASTEXITCODE -eq 0) 'Stapelmodus: Trockenlauf'
Assert-True ($planOut -match 'KI-Suche\s+contoso\.phototool') 'Stapelmodus: Programm ohne Regel wird untersucht'
Assert-True ($planOut -match 'Regel vorhanden\s+7zip') 'Stapelmodus: 7-Zip hat eine Regel'
Assert-True ($planOut -match 'ohne Einstellungen') 'Stapelmodus: Runtime übersprungen'

# --- Sammler (wie von einer Netzwerkfreigabe) und Abgleich ------------------------------
$share = Join-Path $Work 'share'
New-Item $share -ItemType Directory -Force | Out-Null
Invoke-Pk @('collect', '--out', $share, '--name', 'SMOKE_collect') -Allowed 0,2
$collected = Join-Path $share 'SMOKE_collect'
Assert-True (Test-Path (Join-Path $collected 'profile.json')) 'Sammler: Profil geschrieben'
Assert-True (Test-Path (Join-Path $collected 'inventory.json')) 'Sammler: Inventar geschrieben'
Assert-True (Test-Path (Join-Path $collected 'winget-export.json')) 'Sammler: winget-Export geschrieben'
if (Test-Path (Join-Path $collected 'profile.json')) {
    $m = Get-Content (Join-Path $collected 'profile.json') -Raw | ConvertFrom-Json
    Assert-True ($m.modules.id -contains 'windows.mouse') "Sammler: Windows-Module enthalten ($($m.modules.Count) Module: $($m.modules.id -join ', '))"
    $inv = Get-Content (Join-Path $collected 'inventory.json') -Raw | ConvertFrom-Json
    Assert-True ($inv.programs.Count -gt 5) "Sammler: $($inv.programs.Count) installierte Programme, $($inv.winget.Count) winget-Pakete"
}
Invoke-Pk @('compare', '--profile', $collected)
Invoke-Pk @('apply', '--profile', $collected, '--current-user', '--installed-only', '--dry-run')
& $Exe apply --profile $collected --current-user --installed-only --install --dry-run --lang de 2>&1 | Out-Null
Assert-True ($LASTEXITCODE -eq 3) '--installed-only und --install schließen sich aus'

# --- VHDX einer (simulierten) heruntergefahrenen VM ---------------------------------------
$vhdx = Join-Path $Work 'smoke-vm.vhdx'
$dp = Join-Path $Work 'diskpart.txt'
"create vdisk file=""$vhdx"" maximum=256 type=expandable`r`nselect vdisk file=""$vhdx""`r`nattach vdisk" | Set-Content $dp -Encoding ASCII
diskpart /s $dp | Out-Null
$disk = Get-DiskImage -ImagePath $vhdx | Get-Disk
Initialize-Disk -Number $disk.Number -PartitionStyle MBR
$part = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
Format-Volume -Partition $part -FileSystem NTFS -NewFileSystemLabel SMOKEVM -Confirm:$false | Out-Null
$vm = "$((Get-Partition -DiskNumber $disk.Number | Get-Volume | Where-Object DriveLetter | Select-Object -First 1).DriveLetter):\"
Write-Host "Test-VHDX eingebunden als $vm"

# Windows-Gerüst mit echtem SOFTWARE-Hive und NTUSER.DAT
New-Item (Join-Path $vm 'Windows\System32\config') -ItemType Directory -Force | Out-Null
reg add "HKLM\SOFTWARE\PK_FAKE_SW\Microsoft\Windows\CurrentVersion\Uninstall\SmokeApp" /v DisplayName /d "Smoke test App 1.0" /f | Out-Null
reg add "HKLM\SOFTWARE\PK_FAKE_SW\Microsoft\Windows NT\CurrentVersion\ProfileList\S-1-5-21-1-2-3-1001" /v ProfileImagePath /t REG_EXPAND_SZ /d "%SystemDrive%\Users\vmuser" /f | Out-Null
reg add "HKLM\SOFTWARE\PK_FAKE_SW\ProfileKitSmokeHKLM" /v Mode /d "aus-dem-image" /f | Out-Null
reg save "HKLM\SOFTWARE\PK_FAKE_SW" (Join-Path $vm 'Windows\System32\config\SOFTWARE') /y | Out-Null
reg delete "HKLM\SOFTWARE\PK_FAKE_SW" /f | Out-Null

$vmProfile = Join-Path $vm 'Users\vmuser'
New-Item (Join-Path $vmProfile 'AppData\Roaming\ProfileKitSmoke') -ItemType Directory -Force | Out-Null
Set-Content (Join-Path $vmProfile 'AppData\Roaming\ProfileKitSmoke\conf.ini') 'last=C:\Users\vmuser\Documents' -Encoding Unicode
reg add "HKCU\PK_FAKE_USER\Software\ProfileKitSmoke" /v LastDir /d "C:\Users\vmuser\Downloads" /f | Out-Null
reg save "HKCU\PK_FAKE_USER" (Join-Path $vmProfile 'NTUSER.DAT') /y | Out-Null
reg delete "HKCU\PK_FAKE_USER" /f | Out-Null
Dismount-DiskImage -ImagePath $vhdx | Out-Null

Invoke-Pk @('vhdx', 'users', '--vhdx', $vhdx)
$vmShare = Join-Path $Work 'vmshare'
Invoke-Pk @('collect', '--vhdx', $vhdx, '--user', 'vmuser', '--out', $vmShare, '--only', 'smoke.test') -Allowed 0,2
$vmProfileOut = Join-Path $vmShare 'smoke-vm_vmuser'
Assert-True (-not (Get-DiskImage -ImagePath $vhdx).Attached) 'VHDX: nach dem Sammeln wieder getrennt'
if (Test-Path (Join-Path $vmProfileOut 'profile.json')) {
    $vmManifest = Get-Content (Join-Path $vmProfileOut 'profile.json') -Raw | ConvertFrom-Json
    Assert-True ($vmManifest.source.profilePath -eq 'C:\Users\vmuser') "VHDX: Originalpfad im Manifest ($($vmManifest.source.profilePath))"
    Assert-True ($vmManifest.source.machine -eq 'smoke-vm') "VHDX: Rechnername aus dem Dateinamen ($($vmManifest.source.machine))"
    $vmReg = Get-Content (Join-Path $vmProfileOut 'modules\smoke.test\registry.json') -Raw
    Assert-True ($vmReg -like '*C:\\Users\\vmuser\\Downloads*') 'VHDX: HKCU aus dem NTUSER.DAT des Images'
    Assert-True ($vmReg -like '*aus-dem-image*') 'VHDX: HKLM aus dem SOFTWARE-Hive des Images'
    Assert-True (Test-Path (Join-Path $vmProfileOut 'modules\smoke.test\files\APPDATA\ProfileKitSmoke\conf.ini')) 'VHDX: Datei aus dem Image erfasst'
    $vmInv = Get-Content (Join-Path $vmProfileOut 'inventory.json') -Raw | ConvertFrom-Json
    Assert-True ($vmInv.programs.name -contains 'Smoke test App 1.0') 'VHDX: Inventar aus dem Image'

    Invoke-Pk @('apply', '--profile', $vmProfileOut, '--user', $user) -Allowed 0,2
    $rewritten = Get-Content (Join-Path $target 'AppData\Roaming\ProfileKitSmoke\conf.ini') -Raw -Encoding Unicode
    Assert-True ($rewritten -like "*$target\Documents*") "VHDX: Pfad beim Anwenden umgeschrieben ($($rewritten.Trim()))"
} else {
    Assert-True $false 'VHDX: Profil geschrieben'
}

# --- Konto ohne Profil (wie aus der autounattend.xml) ------------------------------------
net user pktest2 'Pk!Smoke-2026' /add | Out-Null
Invoke-Pk @('apply', '--profile', $profileDir, '--user', 'pktest2', '--only', 'smoke.test') -Allowed 0,2
$p2 = (Get-CimInstance Win32_UserProfile | Where-Object { $_.LocalPath -like '*\pktest2' }).LocalPath
Assert-True ([bool]$p2) "Profil für Konto ohne Anmeldung erzeugt ($p2)"
if ($p2) { Assert-True (Test-Path (Join-Path $p2 'AppData\Roaming\ProfileKitSmoke\conf.ini')) 'Einstellungen beim Konto ohne Anmeldung angekommen' }

if ($failures -gt 0) { Write-Host "`n$failures Fehler" -ForegroundColor Red; exit 1 }
Write-Host "`nAlle Smoke-Tests bestanden." -ForegroundColor Green
# Ausdrücklich 0: GitHub Actions beendet pwsh-Schritte sonst mit dem letzten $LASTEXITCODE
# (der Test "--installed-only und --install" erwartet absichtlich Exitcode 3).
exit 0
