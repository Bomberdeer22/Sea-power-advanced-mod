@echo off
REM ===========================================================================
REM  Sea Power Advanced - diagnostic collector
REM
REM  Writes ONE text file next to this script:
REM      SeaPowerAdvanced-diagnose.txt
REM  with everything needed to work out what is wrong.
REM
REM  Uses only cmd.exe: no PowerShell, no admin rights, no changes to your PC.
REM  The file is written as it goes, so it survives the window closing.
REM
REM  Double-click this file, then send the .txt it creates.
REM ===========================================================================
setlocal enabledelayedexpansion
cd /d "%~dp0"
set "HERE=%~dp0"
set "LOG=%HERE%SeaPowerAdvanced-diagnose.txt"

echo Collecting diagnostic information...
echo.

call :header
call :system
call :python
call :locate_game
call :game_checks
call :mod_files
call :game_logs
call :build_logs
call :analysis
call :footer

echo.
echo ============================================================
echo  Done. Send this one file:
echo    %LOG%
echo ============================================================
echo.
pause
exit /b 0


REM ===========================================================================
:header
> "%LOG%" echo ================================================================
>> "%LOG%" echo  Sea Power Advanced - diagnostic report
>> "%LOG%" echo  Generated %DATE% %TIME%
>> "%LOG%" echo ================================================================
>> "%LOG%" echo.
>> "%LOG%" echo Written for support. Contains local folder names only.
exit /b


:system
>> "%LOG%" echo.
>> "%LOG%" echo ----------------------------------------------------------------
>> "%LOG%" echo [ SYSTEM ]
>> "%LOG%" echo report path : %LOG%
>> "%LOG%" echo script path : %HERE%
>> "%LOG%" echo computer    : %COMPUTERNAME%
>> "%LOG%" echo user        : %USERNAME%
>> "%LOG%" echo arch        : %PROCESSOR_ARCHITECTURE%
ver >> "%LOG%"
echo  - system information
exit /b


:python
>> "%LOG%" echo.
>> "%LOG%" echo [ PYTHON ]
set "FOUND="
where py.exe >nul 2>nul
if not errorlevel 1 set "FOUND=1"
where python.exe >nul 2>nul
if not errorlevel 1 set "FOUND=1"
where python3.exe >nul 2>nul
if not errorlevel 1 set "FOUND=1"

if not defined FOUND goto :python_missing

>> "%LOG%" echo interpreter found on PATH
for /f "delims=" %%L in ('where py.exe 2^>nul') do >> "%LOG%" echo   py.exe     %%L
for /f "delims=" %%L in ('where python.exe 2^>nul') do >> "%LOG%" echo   python.exe %%L
py -3 --version >> "%LOG%" 2>&1
python --version >> "%LOG%" 2>&1

if not exist "%HERE%tools\analysis\.venv\Scripts\python.exe" goto :python_no_venv
>> "%LOG%" echo private environment: present
"%HERE%tools\analysis\.venv\Scripts\python.exe" --version >> "%LOG%" 2>&1
goto :python_done

:python_no_venv
>> "%LOG%" echo private environment: not created yet

:python_missing
if defined FOUND goto :python_done
>> "%LOG%" echo NOT FOUND on PATH.
>> "%LOG%" echo The analysis tools need Python 3 to run. Two options:
>> "%LOG%" echo   1. Install from https://www.python.org/downloads/ and tick
>> "%LOG%" echo      "Add python.exe to PATH" during setup.
>> "%LOG%" echo   2. Skip this: send Seapower-Scripts.dll instead.

:python_done
echo  - python environment
exit /b


:locate_game
>> "%LOG%" echo.
>> "%LOG%" echo [ GAME FOLDER ]
set "GAME="

if not exist "%HERE%Directory.Build.local.props" goto :game_no_props
for /f "tokens=2 delims=<>" %%A in ('findstr /i "<SeaPowerDir>" "%HERE%Directory.Build.local.props" 2^>nul') do set "GAME=%%A"
if not defined GAME goto :game_no_props
>> "%LOG%" echo source: Directory.Build.local.props

:game_no_props
if defined GAME goto :game_have
if exist "C:\Program Files (x86)\Steam\steamapps\common\Sea Power\Sea Power.exe" set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Sea Power"
if defined GAME goto :game_have
if exist "C:\Program Files\Steam\steamapps\common\Sea Power\Sea Power.exe" set "GAME=C:\Program Files\Steam\steamapps\common\Sea Power"
if defined GAME goto :game_have
if exist "%~1" set "GAME=%~1"
if defined GAME goto :game_have

>> "%LOG%" echo NOT FOUND in the usual places.
>> "%LOG%" echo If the game lives elsewhere, run:
>> "%LOG%" echo    diagnose.cmd "D:\path\to\Sea Power"
>> "%LOG%" echo and send the new report.
set "GAME="
echo  - game folder NOT found
exit /b

:game_have
>> "%LOG%" echo path: !GAME!
echo  - game folder found
exit /b


:game_checks
if not defined GAME goto :checks_none

>> "%LOG%" echo.
>> "%LOG%" echo [ GAME FILES ]

if exist "!GAME!\Sea Power_Data\Managed\Seapower-Scripts.dll" >> "%LOG%" echo Seapower-Scripts.dll    : present
if not exist "!GAME!\Sea Power_Data\Managed\Seapower-Scripts.dll" >> "%LOG%" echo Seapower-Scripts.dll    : MISSING - wrong game folder?

if exist "!GAME!\BepInEx" >> "%LOG%" echo BepInEx                 : present
if not exist "!GAME!\BepInEx" >> "%LOG%" echo BepInEx                 : MISSING - BepInEx and Anchor Chain must be installed first

if exist "!GAME!\BepInEx\core\BepInEx.dll" >> "%LOG%" echo   core\BepInEx.dll      : present
if not exist "!GAME!\BepInEx\core\BepInEx.dll" >> "%LOG%" echo   core\BepInEx.dll      : MISSING

if exist "!GAME!\BepInEx\plugins\SeaPowerAdvanced" goto :plugin_present
>> "%LOG%" echo   plugins\SeaPowerAdvanced : MISSING - the mod DLL is not installed
goto :old_copy_check
:plugin_present
>> "%LOG%" echo   plugins\SeaPowerAdvanced : present
dir /b "!GAME!\BepInEx\plugins\SeaPowerAdvanced" >> "%LOG%" 2>nul

:old_copy_check
if not exist "!GAME!\Sea Power_Data\StreamingAssets\SeaPowerAdvanced" goto :old_copy_none
>> "%LOG%" echo   StreamingAssets copy   : PRESENT - remove this if the plugin copy is also present, or the mod loads twice
dir /b "!GAME!\Sea Power_Data\StreamingAssets\SeaPowerAdvanced" >> "%LOG%" 2>nul
goto :checks_done
:old_copy_none
>> "%LOG%" echo   StreamingAssets copy   : none - good
:checks_done
exit /b

:checks_none
>> "%LOG%" echo.
>> "%LOG%" echo [ GAME FILES ]
>> "%LOG%" echo skipped: game folder unknown
exit /b


:mod_files
>> "%LOG%" echo.
>> "%LOG%" echo [ THIS FOLDER ]
if exist "%HERE%dist" dir /b /s "%HERE%dist\*.dll" >> "%LOG%" 2>nul
if not exist "%HERE%dist" >> "%LOG%" echo no dist folder - the mod has not been built here
if not exist "%HERE%Directory.Build.local.props" goto :props_done
>> "%LOG%" echo.
>> "%LOG%" echo [ Directory.Build.local.props ]
type "%HERE%Directory.Build.local.props" >> "%LOG%" 2>nul
:props_done
echo  - build output
exit /b


:game_logs
>> "%LOG%" echo.
>> "%LOG%" echo [ GAME LOGS ]
if not defined GAME goto :logs_no_game

if not exist "!GAME!\BepInEx\LogOutput.log" goto :logs_no_beplog
>> "%LOG%" echo.
>> "%LOG%" echo --- BepInEx\LogOutput.log ---
type "!GAME!\BepInEx\LogOutput.log" >> "%LOG%" 2>nul
goto :logs_mod

:logs_no_beplog
>> "%LOG%" echo BepInEx\LogOutput.log : not found - BepInEx has never run, or it is elsewhere

:logs_mod
if not exist "!GAME!\BepInEx\plugins\SeaPowerAdvanced\seapoweradvanced.log" goto :logs_no_modlog
>> "%LOG%" echo.
>> "%LOG%" echo --- seapoweradvanced.log ---
type "!GAME!\BepInEx\plugins\SeaPowerAdvanced\seapoweradvanced.log" >> "%LOG%" 2>nul
goto :logs_done

:logs_no_modlog
>> "%LOG%" echo seapoweradvanced.log : not found - the mod has never started
goto :logs_done

:logs_no_game
>> "%LOG%" echo skipped: game folder unknown
:logs_done
echo  - game logs
exit /b


:build_logs
>> "%LOG%" echo.
>> "%LOG%" echo [ BUILD LOGS ]
if not exist "%HERE%build-log.txt" goto :build_no_log
>> "%LOG%" echo --- build-log.txt ---
type "%HERE%build-log.txt" >> "%LOG%" 2>nul
goto :build_output
:build_no_log
>> "%LOG%" echo build-log.txt : not found - build.cmd has not been run from this folder
:build_output
if not exist "%HERE%build-output.txt" goto :build_done
>> "%LOG%" echo.
>> "%LOG%" echo --- build-output.txt ---
type "%HERE%build-output.txt" >> "%LOG%" 2>nul
:build_done
echo  - build logs
exit /b


:analysis
>> "%LOG%" echo.
>> "%LOG%" echo [ ANALYSIS TOOLS ]
set "APY="
if exist "%HERE%tools\analysis\.venv\Scripts\python.exe" set "APY=%HERE%tools\analysis\.venv\Scripts\python.exe"
if defined APY goto :analysis_run
where py.exe >nul 2>nul
if not errorlevel 1 set "APY=py -3"
if defined APY goto :analysis_run
where python.exe >nul 2>nul
if not errorlevel 1 set "APY=python"
if defined APY goto :analysis_run
>> "%LOG%" echo skipped: Python not available
echo  - analysis skipped, no Python
exit /b

:analysis_run
>> "%LOG%" echo --- selftest ---
%APY% "%HERE%tools\analysis\analyze.py" selftest >> "%LOG%" 2>&1
>> "%LOG%" echo exit code %ERRORLEVEL%

>> "%LOG%" echo.
>> "%LOG%" echo --- readiness ---
set "DLL="
if not defined GAME goto :analysis_no_dll
if not exist "!GAME!\Sea Power_Data\Managed\Seapower-Scripts.dll" goto :analysis_no_dll
set "DLL=!GAME!\Sea Power_Data\Managed\Seapower-Scripts.dll"
:analysis_no_dll
if defined DLL goto :analysis_with_dll
%APY% "%HERE%tools\analysis\analyze.py" readiness --fast >> "%LOG%" 2>&1
goto :analysis_collect
:analysis_with_dll
%APY% "%HERE%tools\analysis\analyze.py" --dll "%DLL%" readiness --fast >> "%LOG%" 2>&1
:analysis_collect
>> "%LOG%" echo exit code %ERRORLEVEL%

if not exist "%HERE%tools\analysis\analysis-error.txt" goto :no_error_file
>> "%LOG%" echo.
>> "%LOG%" echo --- analysis-error.txt ---
type "%HERE%tools\analysis\analysis-error.txt" >> "%LOG%" 2>nul
:no_error_file
if not exist "%HERE%tools\analysis\feature-readiness.txt" goto :analysis_done
>> "%LOG%" echo.
>> "%LOG%" echo --- feature-readiness.txt ---
type "%HERE%tools\analysis\feature-readiness.txt" >> "%LOG%" 2>nul
:analysis_done
echo  - analysis run
exit /b


:footer
>> "%LOG%" echo.
>> "%LOG%" echo ================================================================
>> "%LOG%" echo  End of report.
>> "%LOG%" echo ================================================================
exit /b
