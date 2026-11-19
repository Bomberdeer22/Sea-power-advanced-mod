<#
.SYNOPSIS
  Builds the mod with a portable .NET SDK, so nothing has to be installed.

.DESCRIPTION
  The first run downloads the official .NET SDK ZIP into .dotnet\ inside this
  repo (about 200 MB, once) and unpacks it there. Nothing is installed
  system-wide: no admin rights, no registry changes, no PATH changes, and
  deleting .dotnet\ undoes it. No downloaded script is ever executed - the
  archive is Microsoft's own, over https, and build.cmd does the same thing
  with curl.

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

    # Download the SDK ZIP and unpack it - the same thing build.cmd does with curl and tar - rather
    # than fetching dotnet-install.ps1 and running it with -ExecutionPolicy Bypass. That pattern
    # (download a script, execute it, bypassing policy) is a dropper signature, and this repository
    # has no business looking like one: it is already reported as a virus by browsers for far less.
    # The archive is Microsoft's own, over https, and unpacked inside this folder; nothing is
    # installed system-wide.
    $version = if ($Channel -eq '8.0') { '8.0.404' } else { $Channel }
    $zip = Join-Path $dotnetDir 'dotnet-sdk.zip'

    $sources = @(
        "https://builds.dotnet.microsoft.com/dotnet/Sdk/$version/dotnet-sdk-$version-win-x64.zip",
        "https://dotnetcli.azureedge.net/dotnet/Sdk/$version/dotnet-sdk-$version-win-x64.zip"
    )

    $got = $false
    foreach ($url in $sources) {
        try {
            Write-Host "  $url"
            Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
            $got = $true
            break
        } catch {
            Write-Host "  (that one failed: $($_.Exception.Message))"
        }
    }

    if (-not $got) {
        throw "Could not download the .NET $Channel SDK. Install it from https://dotnet.microsoft.com/download, or run build.cmd, which does the same download with curl."
    }

    Write-Host '  unpacking...'
    Expand-Archive -Path $zip -DestinationPath $dotnetDir -Force
    Remove-Item $zip -Force -ErrorAction SilentlyContinue

    if (-not (Test-Path $dotnet)) { throw "The portable SDK did not appear in $dotnetDir." }
}

$env:PATH = "$dotnetDir;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# Hashtable splatting binds by name. An array splat would pass "-Configuration" as a positional
# VALUE, which is exactly what made the first version fail ValidateSet.
$parameters = @{ Configuration = $Configuration }
if ($Install) { $parameters['Install'] = $true }
if (-not [string]::IsNullOrWhiteSpace($SeaPowerDir)) { $parameters['SeaPowerDir'] = $SeaPowerDir }

& (Join-Path $root 'build.ps1') @parameters
exit $LASTEXITCODE
