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

# -Install needs the game folder. Take it from the same props file the build uses, so there is
# only one place to configure.
function Get-ConfiguredGameDir {
    foreach ($props in @('Directory.Build.local.props', 'Directory.Build.props')) {
        $path = Join-Path $root $props
        if (-not (Test-Path $path)) { continue }
        $match = [regex]::Match((Get-Content $path -Raw), '<SeaPowerDir>([^<]+)</SeaPowerDir>')
        if ($match.Success) { return $match.Groups[1].Value.Trim() }
    }
    return $null
}
$project = Join-Path $root 'src\SeaPowerAdvanced\SeaPowerAdvanced.csproj'

dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dist = Join-Path $root 'dist\SeaPowerAdvanced'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $root "src\SeaPowerAdvanced\bin\$Configuration\SeaPowerAdvanced.dll") $dist -Force
Copy-Item (Join-Path $root 'mod\_info.ini') $dist -Force
Write-Host "Package ready: $dist"

if ($Install) {
    if ([string]::IsNullOrWhiteSpace($SeaPowerDir)) { $SeaPowerDir = Get-ConfiguredGameDir }
    if ([string]::IsNullOrWhiteSpace($SeaPowerDir)) {
        throw 'Set SeaPowerDir in Directory.Build.local.props, or pass -SeaPowerDir.'
    }
    if (-not (Test-Path (Join-Path $SeaPowerDir 'Sea Power_Data\Managed'))) {
        throw "SeaPowerDir does not look like a Sea Power folder: $SeaPowerDir"
    }
    $target = Join-Path $SeaPowerDir 'Sea Power_Data\StreamingAssets\SeaPowerAdvanced'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $dist 'SeaPowerAdvanced.dll') $target -Force
    Copy-Item (Join-Path $dist '_info.ini') $target -Force
    Write-Host "Installed to: $target"
}
