param(
    [Parameter(Mandatory=$true)][string]$ProviderDirectory,
    [string]$MeterDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $MeterDirectory) { $MeterDirectory = Join-Path $root 'artifacts\meter-store' }
$output = Join-Path $root 'artifacts\CodexMeterWidget-1.0.1-beta.1-win-x64'
if (Test-Path $output) { throw 'Package directory already exists; use a fresh output directory.' }
New-Item -ItemType Directory -Path "$output\Meter\assets","$output\Widget\SampleWidgetProviderApp" -Force | Out-Null
Copy-Item -Path (Join-Path $MeterDirectory '*') -Destination "$output\Meter" -Recurse
foreach ($name in @('SampleWidgetProviderApp.exe','SampleWidgetProviderApp.pri','Microsoft.WindowsAppRuntime.Bootstrap.dll','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Core.winmd','WebView2Loader.dll','Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll')) {
    Copy-Item -LiteralPath (Join-Path $ProviderDirectory $name) -Destination "$output\Widget\SampleWidgetProviderApp"
}
Get-ChildItem (Join-Path $root 'src\WidgetPackage') | Copy-Item -Destination "$output\Widget" -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'README.md'),(Join-Path $root 'docs'),(Join-Path $root 'licenses') -Destination $output -Recurse
Get-ChildItem $output -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($output.Length + 1).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
} | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Compress-Archive -Path "$output\*" -DestinationPath "$output.zip"
Get-FileHash "$output.zip" | Format-List
