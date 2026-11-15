@echo off
REM Same as survey.ps1, for machines where the PowerShell execution policy blocks .ps1 files.
REM Usage:  survey.cmd            full API survey -> analysis-report.txt
REM         survey.cmd selftest   check the reader against a known assembly
REM         survey.cmd "C:\path\to\Seapower-Scripts.dll"
setlocal
set HERE=%~dp0
set PYTHON=%HERE%.venv\Scripts\python.exe

if not exist "%PYTHON%" (
    echo Creating the private Python environment...
    where py >nul 2>nul && (py -3 -m venv "%HERE%.venv") || (python -m venv "%HERE%.venv")
    if not exist "%PYTHON%" (
        echo Could not create a Python environment. Install Python 3 from python.org and retry.
        exit /b 1
    )
    "%PYTHON%" -m pip install --quiet -r "%HERE%requirements.txt" || exit /b 1
)

if /i "%1"=="selftest" (
    "%PYTHON%" "%HERE%analyze.py" selftest
    exit /b %ERRORLEVEL%
)

if not "%~1"=="" (
    "%PYTHON%" "%HERE%analyze.py" --dll "%~1" brief
) else (
    "%PYTHON%" "%HERE%analyze.py" brief
)
exit /b %ERRORLEVEL%
