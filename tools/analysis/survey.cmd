@echo off
REM ---------------------------------------------------------------------------
REM  Reads Sea Power's Seapower-Scripts.dll and writes the API reports.
REM  Nothing is loaded, run or changed; the game does not need to be open.
REM
REM    survey.cmd              full API survey   -> analysis-report.txt
REM    survey.cmd readiness    feature check     -> feature-readiness.txt
REM    survey.cmd selftest     verify the reader works
REM    survey.cmd "D:\path\to\Seapower-Scripts.dll"     (non-default game folder)
REM
REM  Safe to double-click. A private Python environment is created inside
REM  tools\analysis\.venv on first run; delete that folder to undo it.
REM ---------------------------------------------------------------------------
setlocal enabledelayedexpansion
set "HERE=%~dp0"
set "PYTHON=%HERE%.venv\Scripts\python.exe"
set "ANALYZE=%HERE%analyze.py"
set "RUNLOG=%HERE%run-log.txt"

if not exist "%PYTHON%" (
    echo First run: creating a private Python environment, about 10 seconds...
    echo.
    set "BASEPY="
    where py >nul 2>nul && set "BASEPY=py -3"
    if not defined BASEPY where python >nul 2>nul && set "BASEPY=python"
    if not defined BASEPY where python3 >nul 2>nul && set "BASEPY=python3"
    if not defined BASEPY goto :no_python

    %BASEPY% -m venv "%HERE%.venv"
    if not exist "%PYTHON%" (
        echo Could not create the Python environment using "%BASEPY%".
        goto :done
    )
    "%PYTHON%" -m pip install --quiet -r "%HERE%requirements.txt"
    if errorlevel 1 (
        echo Could not download the analysis dependencies. Check that this PC is online.
        goto :done
    )
    echo Environment ready.
    echo.
)

if /i "%~1"=="readiness" goto :readiness
if /i "%~1"=="selftest"  goto :selftest
if not "%~1"==""         goto :custom
goto :brief

:readiness
echo Checking which planned features this build can support...
"%PYTHON%" "%ANALYZE%" readiness > "%RUNLOG%" 2>&1
type "%RUNLOG%"
goto :result

:selftest
echo Verifying the reader against a known assembly...
"%PYTHON%" "%ANALYZE%" selftest > "%RUNLOG%" 2>&1
type "%RUNLOG%"
goto :result

:custom
echo Surveying "%~1" ...
"%PYTHON%" "%ANALYZE%" --dll "%~1" brief > "%RUNLOG%" 2>&1
type "%RUNLOG%"
goto :result

:brief
echo Surveying the installed game...
"%PYTHON%" "%ANALYZE%" brief > "%RUNLOG%" 2>&1
type "%RUNLOG%"
goto :result

:result
if errorlevel 1 (
    echo.
    echo ------------------------------------------------------------------
    echo  The analysis stopped with an error.
    echo  Copy everything shown above and send it over; it says what failed.
    echo ------------------------------------------------------------------
)
goto :done

:no_python
echo ------------------------------------------------------------------
echo  Python 3 was not found on this PC.
echo.
echo  Either:
echo    1. Install it from https://www.python.org/downloads/ and tick
echo       "Add python.exe to PATH" during setup, then run this again, OR
echo    2. Skip this and send the DLL over instead (see the chat).
echo ------------------------------------------------------------------

:done
echo.
echo Reports are written to:
echo   %HERE%analysis-report.txt    (full API survey)
echo   %HERE%feature-readiness.txt  (which features are buildable)
echo   %HERE%run-log.txt            (everything this run printed)

REM Pause so a double-click window does not vanish before it can be read.
set "INTERACTIVE="
echo "%cmdcmdline%" | find /i "%~nx0" >nul 2>nul && set "INTERACTIVE=1"
if defined INTERACTIVE pause
endlocal
