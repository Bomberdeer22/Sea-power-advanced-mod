<#
.SYNOPSIS
  Builds Sea Power Advanced and lays out an installable folder in dist\SeaPowerAdvanced.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Configuration Debug
  .\build.ps1 -Install -SeaPowerDir "D:\Steam\steamapps\common\Sea Power"
#>
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Install,
    [string]$SeaPowerDir = $env:SEAPOWER_DIR
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\SeaPowerAdvanced\SeaPowerAdvanced.csproj'

dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dist = Join-Path $root 'dist\SeaPowerAdvanced'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $root "src\SeaPowerAdvanced\bin\$Configuration\SeaPowerAdvanced.dll") $dist -Force
Copy-Item (Join-Path $root 'mod\_info.ini') $dist -Force
Write-Host "Package ready: $dist"

if ($Install) {
    if ([string]::IsNullOrWhiteSpace($SeaPowerDir)) {
        throw 'Set -SeaPowerDir (or SEAPOWER_DIR) to install into the game folder.'
    }
    $target = Join-Path $SeaPowerDir 'Sea Power_Data\StreamingAssets\SeaPowerAdvanced'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $dist 'SeaPowerAdvanced.dll') $target -Force
    Copy-Item (Join-Path $dist '_info.ini') $target -Force
    Write-Host "Installed to: $target"
}
