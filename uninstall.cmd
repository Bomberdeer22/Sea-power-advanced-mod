@echo off
rem ===========================================================================
rem  Uninstall Sea Power Advanced - the exact opposite of build.cmd.
rem
rem  build.cmd copies two files into the game and can install BepInEx. This
rem  finds those files again and takes them back out. Nothing else is touched:
rem  BepInEx itself, winhttp.dll, doorstop_config.ini and every other mod stay
rem  exactly as they are, because other mods need them.
rem
rem  Double-click this file. It pauses at the end and writes everything to
rem  uninstall-report.txt, so a window closing can never lose the answer.
rem
rem  Usage:
rem     uninstall.cmd             remove the mod       (default)
rem     uninstall.cmd check       say what would be removed, change nothing
rem     uninstall.cmd clean       remove the mod *and* its settings file
rem
rem  Nothing is destroyed outright. Whatever is found is copied to
rem  uninstalled\<date and time>\ first, so putting the mod back is a single
rem  copy back, and a settings file you spent an evening on is never lost.
rem
rem  Uses only cmd.exe: no PowerShell, no admin rights, no download.
rem ===========================================================================
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "PROPS=%ROOT%\Directory.Build.local.props"
set "LOG=%ROOT%\uninstall-report.txt"
set "KEEP=%ROOT%\uninstalled"
set "CFGNAME=io.github.bomberdeer22.seapoweradvanced.cfg"

rem check  = report only.  clean = also remove the settings file.
set "MODE=remove"
set "CLEAN="
:args
if "%~1"=="" goto args_done
if /i "%~1"=="check" set "MODE=check"
if /i "%~1"=="report" set "MODE=check"
if /i "%~1"=="remove" set "MODE=remove"
if /i "%~1"=="clean" set "CLEAN=1"
shift
goto args

:args_done
> "%LOG%" echo ================================================================
>> "%LOG%" echo  Sea Power Advanced - uninstall report
>> "%LOG%" echo  %DATE% %TIME%
>> "%LOG%" echo  mode: %MODE%
if defined CLEAN >> "%LOG%" echo  mode: clean - the settings file goes as well
>> "%LOG%" echo ================================================================
>> "%LOG%" echo.

echo ============================================================
echo  Sea Power Advanced - uninstall
echo ============================================================
echo  Folder : %ROOT%
echo  Action : %MODE%
if defined CLEAN echo  Also   : remove the settings file
echo  Result : %LOG%
echo.
echo  If this window ever closes by itself, open uninstall-report.txt.
echo.

call :find_game
if not defined GAME goto no_game
call :find_install
call :show_what
if /i "!MODE!"=="check" goto check_only

rem  Removing is the only thing that needs the game closed.
call :game_running
if defined RUNNING goto game_busy

call :stamp
call :remove_plugin
call :remove_legacy
call :settings_file
goto summary

rem ===========================================================================
:find_game
rem  The same three-step search build.cmd uses: the props file it wrote, then
rem  Steam's own registry entry, then the usual library names on every drive.
set "GAME="
if exist "%PROPS%" for /f "tokens=2 delims=<>" %%p in ('findstr /i "<SeaPowerDir>" "%PROPS%" 2^>nul') do if not defined GAME set "GAME=%%p"
if defined GAME if not exist "!GAME!\Sea Power_Data\Managed\Seapower-Scripts.dll" set "GAME="
if defined GAME goto find_game_done

for /f "tokens=2,*" %%a in ('reg query "HKCU\Software\Valve\Steam" /v SteamPath 2^>nul ^| findstr /i "SteamPath"') do if not defined GAME set "GAME=%%b"
if defined GAME set "GAME=!GAME:/=\!"
if defined GAME if not exist "!GAME!\steamapps\common\Sea Power" set "GAME="
if defined GAME set "GAME=!GAME!\steamapps\common\Sea Power"
if defined GAME if not exist "!GAME!\Sea Power_Data\Managed\Seapower-Scripts.dll" set "GAME="

for %%L in (C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (
    call :try_lib "%%L:\Steam"
    call :try_lib "%%L:\SteamLibrary"
    call :try_lib "%%L:\Program Files (x86)\Steam"
    call :try_lib "%%L:\Program Files\Steam"
)

:find_game_done
>> "%LOG%" echo game folder : !GAME!
exit /b 0

rem  %1 = a Steam root (the folder that contains steamapps)
:try_lib
set "LIB=%~1"
if not exist "!LIB!\steamapps" exit /b 0
if not defined GAME if exist "!LIB!\steamapps\common\Sea Power\Sea Power_Data\Managed\Seapower-Scripts.dll" set "GAME=!LIB!\steamapps\common\Sea Power"
exit /b 0


:find_install
set "PLUGIN=!GAME!\BepInEx\plugins\SeaPowerAdvanced"
set "LEGACY=!GAME!\Sea Power_Data\StreamingAssets\SeaPowerAdvanced"
set "CFG=!GAME!\BepInEx\config\!CFGNAME!"
set "ANY="
if exist "!PLUGIN!" set "ANY=1"
if exist "!LEGACY!" set "ANY=1"
>> "%LOG%" echo plugin folder : !PLUGIN!
>> "%LOG%" echo legacy folder : !LEGACY!
>> "%LOG%" echo settings file : !CFG!
exit /b 0


:game_running
rem  A DLL that is loaded cannot be deleted, and a half-finished uninstall is
rem  worse than none. Keep it simple: refuse while the game is open.
set "RUNNING="
tasklist /fi "imagename eq Sea Power.exe" 2>nul | findstr /i /c:"Sea Power.exe" >nul
if not errorlevel 1 set "RUNNING=Sea Power.exe"
tasklist /fi "imagename eq SeaPower.exe" 2>nul | findstr /i /c:"SeaPower.exe" >nul
if not errorlevel 1 set "RUNNING=SeaPower.exe"
exit /b 0


:show_what
echo [1/3] What is there...
>> "%LOG%" echo.
>> "%LOG%" echo [1/3] what is there
if exist "!PLUGIN!" goto show_plugin
echo        no mod in BepInEx\plugins\SeaPowerAdvanced
>> "%LOG%" echo        plugin folder: absent
goto show_legacy

:show_plugin
echo        !PLUGIN!
>> "%LOG%" echo        plugin folder contents:
for /f "delims=" %%f in ('dir /b /a "!PLUGIN!" 2^>nul') do (
    echo           %%f
    >> "%LOG%" echo           %%f
)

:show_legacy
if not exist "!LEGACY!" goto show_settings
echo        and an old copy in StreamingAssets:
echo          !LEGACY!
>> "%LOG%" echo        legacy StreamingAssets copy present

:show_settings
if not exist "!CFG!" goto show_done
echo        settings: !CFG!
>> "%LOG%" echo        settings file present
:show_done
echo.
exit /b 0


:stamp
rem  A folder name made of the date and time: %DATE% and %TIME% carry slashes,
rem  colons and spaces on their own, and those are not allowed in a path.
set "STAMP=%DATE%"
set "STAMP=!STAMP:/=-!"
set "STAMP=!STAMP:.=-!"
set "STAMP=!STAMP:,=-!"
set "STAMP=!STAMP: =_!"
set "CLOCK=%TIME%"
set "CLOCK=!CLOCK::=-!"
set "CLOCK=!CLOCK:.=-!"
set "CLOCK=!CLOCK: =0!"
set "BACKUP=%KEEP%\!STAMP!_!CLOCK!"
>> "%LOG%" echo backup to    : !BACKUP!
exit /b 0


:remove_plugin
echo [2/3] Removing the mod from the game...
>> "%LOG%" echo.
>> "%LOG%" echo [2/3] remove
if not exist "!PLUGIN!" goto remove_plugin_none
call :backup "!PLUGIN!" "plugins-SeaPowerAdvanced"
rd /s /q "!PLUGIN!" 2>nul
if not exist "!PLUGIN!" goto remove_plugin_gone
echo        LEFT BEHIND - something in that folder is in use:
>> "%LOG%" echo        LEFT BEHIND: !PLUGIN!
for /f "delims=" %%f in ('dir /b /a "!PLUGIN!" 2^>nul') do (
    echo           %%f
    >> "%LOG%" echo           %%f
)
echo        Close Sea Power, then run this file again.
>> "%LOG%" echo        close the game and run again
goto remove_plugin_done

:remove_plugin_gone
echo        removed  !PLUGIN!
>> "%LOG%" echo        removed !PLUGIN!
goto remove_plugin_done

:remove_plugin_none
echo        nothing there - the BepInEx copy was already gone.
>> "%LOG%" echo        plugin folder absent, nothing to remove
:remove_plugin_done
exit /b 0


:remove_legacy
rem  Releases up to 0.4.0 put the DLL in StreamingAssets instead. A copy there
rem  is loaded by Anchor Chain as well, so leaving it means the mod comes back
rem  from the dead after a correct uninstall.
if not exist "!LEGACY!" exit /b 0
echo        also removing the pre-0.5.0 copy in StreamingAssets...
>> "%LOG%" echo        removing legacy copy !LEGACY!
call :backup "!LEGACY!" "StreamingAssets-SeaPowerAdvanced"
rd /s /q "!LEGACY!" 2>nul
if exist "!LEGACY!" (
    echo        LEFT BEHIND: !LEGACY!
    >> "%LOG%" echo        LEFT BEHIND: !LEGACY!
) else (
    echo        removed  !LEGACY!
    >> "%LOG%" echo        removed !LEGACY!
)
exit /b 0


:settings_file
echo [3/3] Settings...
>> "%LOG%" echo.
>> "%LOG%" echo [3/3] settings
if not exist "!CFG!" goto settings_none
if not defined CLEAN goto settings_keep
call :backup_file "!CFG!"
del /q "!CFG!" 2>nul
if exist "!CFG!" (
    echo        could not delete !CFG!
    >> "%LOG%" echo        could not delete the settings file
) else (
    echo        removed  settings file
    >> "%LOG%" echo        removed the settings file
)
goto settings_done

:settings_keep
echo        kept     settings file, so a reinstall remembers your choices
>> "%LOG%" echo        kept the settings file (run "uninstall.cmd clean" to remove it)
goto settings_done

:settings_none
echo        no settings file - the mod never wrote one.
>> "%LOG%" echo        no settings file
:settings_done
exit /b 0


rem  %1 = a folder to copy into the backup before it is deleted
rem  %2 = the name to keep it under: both live folders are called SeaPowerAdvanced
:backup
if not exist "%~1" exit /b 0
if not exist "!BACKUP!" mkdir "!BACKUP!" 2>nul
xcopy /e /i /y /q "%~1" "!BACKUP!\%~2" >> "%LOG%" 2>&1
if errorlevel 1 (
    echo        WARNING: could not copy "%~1" to the backup folder.
    >> "%LOG%" echo        WARNING: backup copy failed for %~1
) else (
    >> "%LOG%" echo        backed up %~1
)
exit /b 0

rem  %1 = a single file to copy into the backup
:backup_file
if not exist "%~1" exit /b 0
if not exist "!BACKUP!" mkdir "!BACKUP!" 2>nul
copy /y "%~1" "!BACKUP!\%~nx1" >nul 2>&1
exit /b 0


rem ===========================================================================
:check_only
echo ============================================================
echo  CHECK ONLY - nothing has been changed.
echo ============================================================
>> "%LOG%" echo.
>> "%LOG%" echo check only: nothing was changed
if not defined ANY echo        The mod is not installed in that game folder.
echo.
echo  Run uninstall.cmd on its own to actually remove it.
goto finished

:summary
echo.
echo ============================================================
if exist "!PLUGIN!" goto summary_left
if exist "!LEGACY!" goto summary_left
echo  DONE - the mod is out of the game.
>> "%LOG%" echo.
>> "%LOG%" echo result: removed
goto summary_notes

:summary_left
echo  NOT FULLY REMOVED - see "LEFT BEHIND" above.
>> "%LOG%" echo.
>> "%LOG%" echo result: incomplete - something was in use
goto summary_notes

:summary_notes
echo.
echo  Left alone on purpose:
echo    BepInEx, winhttp.dll and doorstop_config.ini - other mods need them.
>> "%LOG%" echo.
>> "%LOG%" echo        left in place: BepInEx, winhttp.dll, doorstop_config.ini
if exist "!BACKUP!" (
    echo  Everything removed was copied first to:
    echo    !BACKUP!
    >> "%LOG%" echo        backup: !BACKUP!
)
echo.
echo  Start Sea Power and the mod is gone.
goto finished


rem ===========================================================================
:no_game
echo  Could not find Sea Power on this PC.
>> "%LOG%" echo.
>> "%LOG%" echo Could not find Sea Power. Nothing was changed.
echo.
echo  If the mod is installed at all, it is in one of these folders:
echo    ^<game^>\BepInEx\plugins\SeaPowerAdvanced
echo    ^<game^>\Sea Power_Data\StreamingAssets\SeaPowerAdvanced
echo  Delete them by hand, and delete
echo    ^<game^>\BepInEx\config\%CFGNAME%
echo  if you also want the settings gone.
echo.
echo  Running build.cmd once would also write down where the game is, and
echo  then this file would find it.
goto finished_bad

:game_busy
echo  Sea Power is running (!RUNNING!).
>> "%LOG%" echo.
>> "%LOG%" echo Sea Power is running - nothing was changed.
echo.
echo  Its DLL cannot be deleted while the game holds it. Close Sea Power,
echo  then run this file again.
goto finished_bad


:finished
>> "%LOG%" echo.
>> "%LOG%" echo finished %DATE% %TIME%
echo  Report: %LOG%
echo ============================================================
echo.
echo Press any key to close this window.
pause >nul
endlocal
exit /b 0

:finished_bad
>> "%LOG%" echo.
>> "%LOG%" echo finished with nothing done %DATE% %TIME%
echo  Report: %LOG%
echo ============================================================
echo.
echo Press any key to close this window.
pause >nul
endlocal
exit /b 1
