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

# Finds the game folder without any configuration: Steam's registry entry first, then the usual
# default libraries. Returns $null when nothing plausible is installed.
function Find-SeaPowerDir {
    $candidates = New-Object System.Collections.Generic.List[string]
    try {
        $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath
        if (-not [string]::IsNullOrWhiteSpace($steam)) {
            $candidates.Add((Join-Path ($steam -replace '/', '\') 'steamapps\common\Sea Power'))
        }
    } catch { }
    foreach ($guess in @(
        'C:\Program Files (x86)\Steam\steamapps\common\Sea Power',
        'C:\Program Files\Steam\steamapps\common\Sea Power',
        'D:\Steam\steamapps\common\Sea Power',
        'D:\SteamLibrary\steamapps\common\Sea Power',
        'E:\SteamLibrary\steamapps\common\Sea Power')) {
        $candidates.Add($guess)
    }
    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate 'Sea Power_Data\Managed\Seapower-Scripts.dll')) { return $candidate }
    }
    return $null
}

# Writes the one file that tells MSBuild where the game and Anchor Chain are.
function Write-LocalProps([string]$gameDir) {
    $sa = Split-Path (Split-Path $gameDir -Parent) -Parent
    $anchorChain = Join-Path $sa 'workshop\content\1286220\3380210757'
    $lines = @(
        '<Project>',
        '  <PropertyGroup>',
        ('    <SeaPowerDir>' + $gameDir + '</SeaPowerDir>'),
        ('    <AnchorChainDir>' + $anchorChain + '</AnchorChainDir>'),
        '  </PropertyGroup>',
        '</Project>')
    Set-Content -Path (Join-Path $root 'Directory.Build.local.props') -Value $lines -Encoding UTF8
    Write-Host "Detected Sea Power:   $gameDir"
    Write-Host "Detected AnchorChain: $anchorChain"
    Write-Host "Wrote Directory.Build.local.props"
}

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

# First run on a new machine: work the paths out instead of demanding that they be typed in.
$propsPath = Join-Path $root 'Directory.Build.local.props'
if (-not (Test-Path $propsPath) -and [string]::IsNullOrWhiteSpace($env:SEAPOWER_DIR)) {
    $detected = Find-SeaPowerDir
    if ($null -ne $detected) { Write-LocalProps $detected }
    else { Write-Host 'Could not find Sea Power automatically; see Directory.Build.local.props.example.' }
}

dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'Build failed - the CS#### lines above are the cause.' -ForegroundColor Red
    Write-Host 'Send those lines on and it will be fixed.' -ForegroundColor Red
    exit 1
}

$dist = Join-Path $root 'dist\SeaPowerAdvanced'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $root "src\SeaPowerAdvanced\bin\$Configuration\SeaPowerAdvanced.dll") $dist -Force
Copy-Item (Join-Path $root 'mod\_info.ini') $dist -Force
Write-Host "Package ready: $dist"

if ($Install) {
    if ([string]::IsNullOrWhiteSpace($SeaPowerDir)) { $SeaPowerDir = Get-ConfiguredGameDir }
    if ([string]::IsNullOrWhiteSpace($SeaPowerDir)) {
        Write-Host 'Set SeaPowerDir in Directory.Build.local.props, or pass -SeaPowerDir.' -ForegroundColor Red
        exit 1
    }
    if (-not (Test-Path (Join-Path $SeaPowerDir 'Sea Power_Data\Managed'))) {
        Write-Host "SeaPowerDir does not look like a Sea Power folder: $SeaPowerDir" -ForegroundColor Red
        exit 1
    }
    # BepInEx plugin route: no mod-manager checkbox, no AnchorChain loader in between.
    $target = Join-Path $SeaPowerDir 'BepInEx\plugins\SeaPowerAdvanced'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $dist 'SeaPowerAdvanced.dll') $target -Force
    Copy-Item (Join-Path $dist '_info.ini') $target -Force
    Write-Host "Installed to: $target"

    # A copy in StreamingAssets would be loaded a second time (once by AnchorChain, once by
    # BepInEx). Point it out rather than deleting anything from the game folder unasked.
    $stale = Join-Path $SeaPowerDir 'Sea Power_Data\StreamingAssets\SeaPowerAdvanced'
    if (Test-Path $stale) {
        Write-Host ''
        Write-Host "NOTE: an older copy also exists at:" -ForegroundColor Yellow
        Write-Host "      $stale" -ForegroundColor Yellow
        Write-Host '      Delete that folder so the mod is not loaded twice.' -ForegroundColor Yellow
    }
}
