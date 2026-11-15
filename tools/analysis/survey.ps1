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
    [string]$Python = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$venvPython = Join-Path $here ".venv\Scripts\python.exe"
if (-not (Test-Path $venvPython)) {
    & (Join-Path $here "setup.ps1") -Python $Python
    if (-not (Test-Path $venvPython)) { throw "Setup did not produce .venv\Scripts\python.exe" }
}

$analyze = Join-Path $here "analyze.py"
$arguments = @($analyze)

if ($Selftest) {
    $arguments += @("selftest")
} else {
    $arguments += @("brief")
    if ($Dll) { $arguments = @("--dll", $Dll) + $arguments }
    if ($Out) { $arguments += @("-o", $Out) }
}

& $venvPython @arguments
exit $LASTEXITCODE
