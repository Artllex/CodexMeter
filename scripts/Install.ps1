param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Version.Build -lt 22621) { throw 'Windows 11 22H2 or newer is required.' }
if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
$framework = Get-AppxPackage -Name Microsoft.WindowsAppRuntime.2 | Where-Object { [version]$_.Version -ge [version]'2.4.0.0' }
if (-not $framework) { throw 'Microsoft.WindowsAppRuntime.2 version 2.4.0.0 or newer is required.' }
$dev = Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if ($dev -ne 1) { throw 'This unsigned preview requires Windows Developer Mode. Enable it in Settings; this installer does not change it.' }
if (Get-AppxPackage -Name Artllex.CodexMeterWidget) { throw 'An existing widget is installed. Remove that package before installing this preview.' }
if (Get-Process CodexMeter -ErrorAction SilentlyContinue) { throw 'Exit Codex Meter using its tray menu before installing.' }
$target = Join-Path $env:LOCALAPPDATA 'Programs\CodexMeterWidget-1.0.1-beta.1'
if (Test-Path $target) { throw "Destination already exists: $target" }
if ($CheckOnly) { Write-Output 'Prerequisites passed. No files changed.'; return }
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Meter'),(Join-Path $PSScriptRoot 'Widget') -Destination $target -Recurse
Add-AppxPackage -Register (Join-Path $target 'Widget\AppxManifest.xml')
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Meter.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = Join-Path $target 'Meter\CodexMeter.exe'
$link.WorkingDirectory = Join-Path $target 'Meter'
$link.Save()
Start-Process -FilePath (Join-Path $target 'Meter\CodexMeter.exe') -WorkingDirectory (Join-Path $target 'Meter') -WindowStyle Hidden
Write-Output 'Installed. Open Win+W and add Codex Meter. Keep the tray collector running. No automatic startup was added.'
