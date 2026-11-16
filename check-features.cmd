@echo off
REM ---------------------------------------------------------------------------
REM  Double-click this file.
REM
REM  It reads the game's own Seapower-Scripts.dll and writes
REM  feature-readiness.txt next to this file: which of the twelve planned
REM  features Sea Power can actually support, and which it cannot.
REM
REM  Nothing is loaded, run or changed. The game does not need to be open.
REM  No PowerShell is used. Everything printed is also saved to
REM  check-features-log.txt so a closing window cannot lose it.
REM
REM  Needs Python 3 once. If it is missing, this says so and tells you the
REM  alternative (send the DLL instead - the same report can be made from it).
REM ---------------------------------------------------------------------------
setlocal
set "HERE=%~dp0"
set "ANALYZE=%HERE%tools\analysis\analyze.py"
set "REQ=%HERE%tools\analysis\requirements.txt"
set "VENV=%HERE%tools\analysis\.venv"
set "PYTHON=%VENV%\Scripts\python.exe"
set "REPORT=%HERE%feature-readiness.txt"
set "LOG=%HERE%check-features-log.txt"

echo ==================================================================
echo  Sea Power Advanced - feature readiness check
echo ==================================================================
echo.

if not exist "%ANALYZE%" goto :no_tools
if exist "%PYTHON%" goto :ready

REM %BASEPY% is set and used on separate lines on purpose. Inside one ( ) block it would be
REM expanded before "set" had run, and the command would be dispatched with no program name.
echo First run: creating a private Python environment, about 10 seconds...
echo.
set "BASEPY="
where py >nul 2>nul && set "BASEPY=py -3"
if defined BASEPY goto :make_env
where python >nul 2>nul && set "BASEPY=python"
if defined BASEPY goto :make_env
where python3 >nul 2>nul && set "BASEPY=python3"
if not defined BASEPY goto :no_python

:make_env
%BASEPY% -m venv "%VENV%"
if not exist "%PYTHON%" goto :env_failed

"%PYTHON%" -m pip install --quiet -r "%REQ%"
if errorlevel 1 goto :deps_failed
echo Environment ready.
echo.
goto :ready

:ready
echo Reading the game's assembly and checking the planned features...
echo This takes about a minute. Nothing is being run or changed.
echo.
"%PYTHON%" "%ANALYZE%" readiness -o "%REPORT%" > "%LOG%" 2>&1
type "%LOG%"
if errorlevel 1 goto :failed
if not exist "%REPORT%" goto :failed

echo.
echo Collecting the exact member signatures behind the verdicts...
>> "%LOG%" echo.
>> "%LOG%" echo [member dump]
set "DUMP=%HERE%member-dump.txt"
> "%DUMP%" echo Sea Power Advanced - member dump
set "TYPES=SeaPower.ObjectBase SeaPower.Ship SeaPower.WeaponSystem SeapowerUI.ViewModels.ObjectBaseViewModel SensorData AircraftStates.MissileEvasion SeaPower.GameMain"
for %%T in (%TYPES%) do call :dump_type "%%T"
echo       wrote %DUMP%

echo.
echo ==================================================================
echo  Done. Send these two files back:
echo    %REPORT%
echo    %DUMP%
echo.
echo  The report says which features are supported; the dump has the
echo  exact field and method signatures needed to write them.
echo  Everything above was also saved to:
echo    %LOG%
echo ==================================================================
goto :done

:dump_type
>> "%DUMP%" echo.
>> "%DUMP%" echo ============================================================================
>> "%DUMP%" echo TYPE %~1
>> "%DUMP%" echo ============================================================================
"%PYTHON%" "%ANALYZE%" member %~1 >> "%DUMP%" 2>&1
if errorlevel 1 >> "%DUMP%" echo   ^(not found under that name - skipped^)
exit /b 0

:failed
echo.
echo ==================================================================
echo  The check did not finish.
echo.
echo  Send whichever of these exists - they say what went wrong:
echo    %LOG%
echo    %HERE%tools\analysis\analysis-error.txt
echo ==================================================================
goto :done

:no_python
echo ------------------------------------------------------------------
echo  Python 3 was not found on this PC, so the check cannot run here.
echo.
echo  Two ways forward:
echo.
echo   1. Install Python from https://www.python.org/downloads/ and tick
echo      "Add python.exe to PATH" during setup. Then double-click this
echo      file again.
echo.
echo   2. Skip it. Send this one file instead:
echo        ^<game^>\\Sea Power_Data\\Managed\\Seapower-Scripts.dll
echo      ^(<drive^>\\Program Files ^(x86^)\\Steam\\steamapps\\common\\Sea Power)
echo      The same report can be produced from that file alone.
echo ------------------------------------------------------------------
goto :done

:env_failed
echo ------------------------------------------------------------------
echo  Python was found, but it could not create its environment.
echo  Open a command prompt in this folder and run:
echo     %BASEPY% -m venv "%VENV%"
echo  to see the reason, then try again.
echo ------------------------------------------------------------------
goto :done

:deps_failed
echo ------------------------------------------------------------------
echo  The environment was created, but the two small libraries it needs
echo  could not be downloaded. Check that this PC is online, then run
echo  this file again.
echo ------------------------------------------------------------------
goto :done

:no_tools
echo ------------------------------------------------------------------
echo  tools\analysis\analyze.py is not next to this file.
echo.
echo  This file has to stay in the same folder as the rest of the mod,
echo  and be run from there. Nothing was changed.
echo ------------------------------------------------------------------

:done
echo.
pause
endlocal
