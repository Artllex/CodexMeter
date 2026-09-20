param(
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Meter\CodexMeter.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid project version.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts\CodexMeter-$version-win-x64" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$zip = "$output.zip"
if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $zip)) { throw 'Output already exists. Choose a fresh directory.' }
& $Dotnet publish $project -c Release -r win-x64 --self-contained true -o $output -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$binary = Join-Path $output 'CodexMeter.dll'
if ((Get-Item -LiteralPath $binary).VersionInfo.FileVersion -ne [string]$projectXml.Project.PropertyGroup.FileVersion) { throw 'Unexpected binary version.' }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $output -Recurse
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip
Get-FileHash -LiteralPath $zip -Algorithm SHA256
