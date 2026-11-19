<#
.SYNOPSIS
  Creates a private Python environment for the analysis tools.

.DESCRIPTION
  Installs dnfile and dncil into tools/analysis/.venv. Nothing is installed system-wide and no
  settings are changed; delete the .venv folder to undo it.

  Blocked by the PowerShell execution policy? Run survey.cmd instead: it sets the
  same environment up with cmd.exe, no script host involved.
#>
[CmdletBinding()]
param(
    [string]$Python = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

if (-not $Python) {
    $candidates = @(
        @{ Exe = "py";      Args = @("-3") },
        @{ Exe = "python";  Args = @() },
        @{ Exe = "python3"; Args = @() }
    )
    foreach ($candidate in $candidates) {
        if (Get-Command $candidate.Exe -ErrorAction SilentlyContinue) {
            $Python = $candidate.Exe
            $script:Prefix = $candidate.Args
            break
        }
    }
}

if (-not $Python) {
    throw "Python 3 not found. Install it from python.org (tick 'Add to PATH'), or pass -Python <path>."
}

$prefix = if ($script:Prefix) { $script:Prefix } else { @() }
$venvPython = Join-Path $here ".venv\Scripts\python.exe"

if (-not (Test-Path $venvPython)) {
    Write-Host "Creating tools\analysis\.venv with $Python ..."
    & $Python @prefix -m venv .venv
    if ($LASTEXITCODE -ne 0) { throw "Could not create the virtual environment." }
}

& $venvPython -m pip install --quiet --upgrade pip
& $venvPython -m pip install --quiet -r requirements.txt
if ($LASTEXITCODE -ne 0) { throw "Could not install the analysis dependencies." }

Write-Host ""
Write-Host "Ready. Run:"
Write-Host "  .\tools\analysis\survey.ps1            # full API survey"
Write-Host "  .\tools\analysis\survey.ps1 -Selftest  # check the reader"
