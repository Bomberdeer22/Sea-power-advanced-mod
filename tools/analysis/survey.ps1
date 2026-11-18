<#
.SYNOPSIS
  Writes analysis-report.txt: what the installed Sea Power build can do.

.DESCRIPTION
  Runs the API survey against Seapower-Scripts.dll. Reads metadata and IL only: nothing is loaded,
  nothing is run, and the game does not need to be running.

  The output is the evidence used to write features against the real API. Send the file back.

  Blocked by the PowerShell execution policy? Use survey.cmd instead.
#>
[CmdletBinding()]
param(
    [string]$Dll = "",
    [string]$Out = "",
    [switch]$Selftest,
    [switch]$Readiness,
    [string]$Python = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$venvPython = Join-Path $here ".venv\Scripts\python.exe"
if (-not (Test-Path $venvPython)) {
    & (Join-Path $here "setup.ps1") -Python $Python
    if (-not (Test-Path $venvPython)) { throw "Setup did not produce .venv\Scripts\python.exe" }
}

# Order matters: python <script> [--dll <path>] <command> [-o <file>]. The script path comes
# first, and --dll belongs to the top-level parser, so it goes before the command name.
$arguments = @(Join-Path $here "analyze.py")

if ($Selftest) {
    $arguments += "selftest"
} elseif ($Readiness) {
    if ($Dll) { $arguments += @("--dll", $Dll) }
    $arguments += "readiness"
} else {
    if ($Dll) { $arguments += @("--dll", $Dll) }
    $arguments += "brief"
    if ($Out) { $arguments += @("-o", $Out) }
}

Write-Host "Running: $venvPython $($arguments -join ' ')"
try {
    & $venvPython @arguments
} catch {
    Write-Host ""
    Write-Host "The survey failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Send this message together with the console output and it can be fixed." -ForegroundColor Red
    exit 1
}
exit $LASTEXITCODE
