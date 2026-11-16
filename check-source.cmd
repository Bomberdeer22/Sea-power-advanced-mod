@echo off
rem ===========================================================================
rem  Check the C# source without building it.
rem
rem  Double-click this file. It pauses at the end and writes the result to
rem  check-source.txt, so nothing is lost if the window closes.
rem
rem  What it catches, in a few seconds rather than after a full compile:
rem
rem    * a symbol used in a file whose namespace is not imported   (CS0246)
rem    * a mod type referenced but no longer declared anywhere      (CS0246)
rem    * a UnitRecord member read by a feature but not declared     (CS1061)
rem
rem  It is a pre-flight check, not a compiler. Its map of the game's own
rem  namespaces is a list of names seen in the game's assemblies, so it can be
rem  wrong in both directions. When it disagrees with the compiler, the compiler
rem  is right.
rem
rem  Needs Python 3 (any recent version). Nothing is installed.
rem ===========================================================================
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "LOG=%ROOT%\check-source.txt"
set "SCRIPT=%ROOT%\tools\check-source.py"

set "PY="
py -3 --version >nul 2>&1
if not errorlevel 1 set "PY=py -3"
if not defined PY (
    python --version >nul 2>&1
    if not errorlevel 1 set "PY=python"
)

echo ============================================================
echo  Sea Power Advanced - source check
echo ============================================================
echo  Folder : %ROOT%
echo  Result : %LOG%
echo.

if not defined PY goto no_python
if not exist "%SCRIPT%" goto no_script

%PY% "%SCRIPT%" %*
set "RC=%ERRORLEVEL%"

echo.
echo ============================================================
if "%RC%"=="0" echo  PASS - nothing missing that this check can see.
if not "%RC%"=="0" echo  FINDINGS - read the lines above, then fix them.
echo ============================================================
echo.
echo Press any key to close this window.
pause >nul
endlocal
exit /b %RC%

:no_python
echo  Python was not found on this machine, so the check cannot run.
echo  The build itself does not need Python - build.cmd skips this step.
echo.
>> "%LOG%" echo Python not found - check skipped
echo Press any key to close this window.
pause >nul
endlocal
exit /b 1

:no_script
echo  tools\check-source.py is missing from this folder.
echo.
>> "%LOG%" echo tools\check-source.py missing - check skipped
echo Press any key to close this window.
pause >nul
endlocal
exit /b 1
