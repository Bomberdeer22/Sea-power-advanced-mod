<#
.SYNOPSIS
  Builds the mod with a portable .NET SDK, so nothing has to be installed.

.DESCRIPTION
  The first run downloads the official .NET SDK into .dotnet\ inside this repo
  (about 200 MB, once). Nothing is installed system-wide: no admin rights, no
  registry changes, no PATH changes, and deleting .dotnet\ undoes it.

  The SDK is only a build tool. It uses the game's own DLLs for the references,
  so the mod is compiled against your exact Sea Power version.

.EXAMPLE
  .\tools\build-portable.ps1
  .\tools\build-portable.ps1 -Install
  .\tools\build-portable.ps1 -Configuration Debug
#>
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Install,
    [string]$SeaPowerDir = $env:SEAPOWER_DIR,
    [string]$Channel = '8.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnetDir = Join-Path $root '.dotnet'
$dotnet = Join-Path $dotnetDir 'dotnet.exe'

if (-not (Test-Path $dotnet)) {
    Write-Host "No portable SDK yet; fetching .NET $Channel into .dotnet\ (one time, no install)..."
    New-Item -ItemType Directory -Force -Path $dotnetDir | Out-Null
    $installer = Join-Path ([System.IO.Path]::GetTempPath()) 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer -InstallDir $dotnetDir -Channel $Channel -NoPath
    if (-not (Test-Path $dotnet)) { throw "The portable SDK did not appear in $dotnetDir." }
}

$env:PATH = "$dotnetDir;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$arguments = @('-Configuration', $Configuration)
if ($Install) { $arguments += '-Install' }
if (-not [string]::IsNullOrWhiteSpace($SeaPowerDir)) { $arguments += @('-SeaPowerDir', $SeaPowerDir) }

& (Join-Path $root 'build.ps1') @arguments
