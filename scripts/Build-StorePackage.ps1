param(
    [string]$MeterDirectory,
    [string]$ProviderDirectory,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $MeterDirectory) { $MeterDirectory = Join-Path $root 'artifacts\meter-store' }
if (-not $ProviderDirectory) { $ProviderDirectory = Join-Path $root 'src\Widget\x64\Release' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\store' }

$layout = Join-Path $OutputDirectory 'layout'
[xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'src\WidgetPackage\AppxManifest.xml') -Raw
$packageVersion = [string]$manifest.Package.Identity.Version
if ($packageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid package version in AppxManifest.xml.' }
$package = Join-Path $OutputDirectory "CodexMeterWidget_$packageVersion`_x64.msix"
if (Test-Path $OutputDirectory) { Remove-Item -LiteralPath $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $layout,(Join-Path $layout 'Meter'),(Join-Path $layout 'SampleWidgetProviderApp') -Force | Out-Null

Copy-Item -Path (Join-Path $root 'src\WidgetPackage\*') -Destination $layout -Recurse
Copy-Item -Path (Join-Path $MeterDirectory '*') -Destination (Join-Path $layout 'Meter') -Recurse
foreach ($name in @(
    'SampleWidgetProviderApp.exe',
    'SampleWidgetProviderApp.pri',
    'Microsoft.WindowsAppRuntime.Bootstrap.dll',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.Core.winmd',
    'WebView2Loader.dll',
    'Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll'
)) {
    Copy-Item -LiteralPath (Join-Path $ProviderDirectory $name) -Destination (Join-Path $layout 'SampleWidgetProviderApp')
}

$makeAppx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter makeappx.exe -Recurse |
    Where-Object FullName -Match '\\x64\\makeappx\.exe$' |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $makeAppx) { throw 'MakeAppx.exe was not found in the Windows SDK.' }
& $makeAppx pack /d $layout /p $package /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }
Get-FileHash -LiteralPath $package -Algorithm SHA256 | Format-List
