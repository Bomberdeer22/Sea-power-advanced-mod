@echo off
rem ===========================================================================
rem  Update an installed Sea Power Advanced - using what the changelog says
rem  changed between the version in the game folder and the version in here.
rem
rem  build.cmd assumes it is starting from nothing. This file assumes it is
rem  starting from *some* version already installed, works out which one from
rem  the game folder itself, and does only what that version still needs:
rem
rem    * 0.5.0  the DLL moved out of StreamingAssets. A copy left behind there
rem             is loaded twice, so the old folder is taken away.
rem    * 0.18.1 the jam ranges became 150 nm, 0.19.0 the dive became 10 nm.
rem             A settings file still holding the old figures is reported, and
rem             the mod moves them itself when the game next starts.
rem    * every release: the DLL and _info.ini are replaced, and the settings
rem             file is never touched, so your choices survive.
rem
rem  Then it runs build.cmd for the current source and verifies the result.
rem
rem  Double-click this file. It pauses at the end and writes every decision,
rem  every file change and the version it found to update.txt - so "what did it
rem  actually do" is answered by a file, not by scrolling back.
rem
rem  Usage:
rem     update.cmd             find the version, apply what is missing, rebuild
rem     update.cmd check       say what would be done, change nothing
rem     update.cmd nobuild     apply the file changes from dist\ (no compile)
rem     update.cmd force       rebuild and reinstall even if already current
rem
rem  Whatever is replaced is copied to uninstalled\ first, so going back is a
rem  copy, not a rebuild. Uses only cmd.exe: no PowerShell, no admin rights.
rem ===========================================================================
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "PROPS=%ROOT%\Directory.Build.local.props"
set "LOG=%ROOT%\update.txt"
set "KEEP=%ROOT%\uninstalled"
set "DIST=%ROOT%\dist\SeaPowerAdvanced"
set "CFGNAME=io.github.bomberdeer22.seapoweradvanced.cfg"

set "MODE=update"
set "NOBUILD="
set "FORCE="
:args
if "%~1"=="" goto args_done
if /i "%~1"=="check" set "MODE=check"
if /i "%~1"=="report" set "MODE=check"
if /i "%~1"=="nobuild" set "NOBUILD=1"
if /i "%~1"=="force" set "FORCE=1"
if /i "%~1"=="selftest" set "MODE=selftest"
shift
goto args

:args_done
> "%LOG%" echo ================================================================
>> "%LOG%" echo  Sea Power Advanced - update report
>> "%LOG%" echo  %DATE% %TIME%
>> "%LOG%" echo  folder: %ROOT%
>> "%LOG%" echo  mode  : %MODE%
if not defined NOBUILD goto args_log_nobuild
>> "%LOG%" echo  mode  : nobuild - using the package already in dist
:args_log_nobuild
if not defined FORCE goto args_log_force
>> "%LOG%" echo  mode  : force - reinstall even if already current
:args_log_force
>> "%LOG%" echo ================================================================

echo ============================================================
echo  Sea Power Advanced - update
echo ============================================================
echo  Folder : %ROOT%
echo  Action : %MODE%
echo  Result : %LOG%
echo.
echo  If this window ever closes by itself, open update.txt.
echo.

if /i "!MODE!"=="selftest" goto selftest

echo [1/5] Finding the game, and which version is in it...
call :find_game
if not defined GAME goto no_game
call :find_install
if not defined INSTALLED_AT goto not_installed

call :read_current
if not defined CURRENT goto no_current
call :read_installed
call :compare
if /i "!DECISION!"=="newer" goto installed_newer
if /i "!DECISION!"=="same" if not defined FORCE goto already_current

call :stamp
call :migrations
call :whats_new

if /i "!MODE!"=="check" goto check_only

call :game_running
if defined RUNNING goto game_busy

call :backup
call :apply
if errorlevel 1 goto apply_failed
call :verify
goto summary


rem ===========================================================================
rem  Where is the game
rem ===========================================================================
:find_game
rem  Three steps, the same ones build.cmd uses: the props file build.cmd wrote,
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
if defined GAME goto find_game_done

for %%L in (C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (
    call :try_lib "%%L:\Steam"
    call :try_lib "%%L:\SteamLibrary"
    call :try_lib "%%L:\Program Files (x86)\Steam"
    call :try_lib "%%L:\Program Files\Steam"
)

:find_game_done
>> "%LOG%" echo.
>> "%LOG%" echo game folder : !GAME!
call :say "game folder: !GAME!"
exit /b 0

rem  %1 = a Steam root (the folder that contains steamapps)
:try_lib
set "LIB=%~1"
if not exist "!LIB!\steamapps" exit /b 0
if not defined GAME if exist "!LIB!\steamapps\common\Sea Power\Sea Power_Data\Managed\Seapower-Scripts.dll" set "GAME=!LIB!\steamapps\common\Sea Power"
exit /b 0


rem ===========================================================================
rem  Which version is in the game folder
rem ===========================================================================
:find_install
set "PLUGIN=!GAME!\BepInEx\plugins\SeaPowerAdvanced"
set "LEGACY=!GAME!\Sea Power_Data\StreamingAssets\SeaPowerAdvanced"
set "CFG=!GAME!\BepInEx\config\!CFGNAME!"
set "INSTALLED_AT="
if exist "!PLUGIN!\SeaPowerAdvanced.dll" set "INSTALLED_AT=!PLUGIN!"
if not defined INSTALLED_AT if exist "!LEGACY!\SeaPowerAdvanced.dll" set "INSTALLED_AT=!LEGACY!"
exit /b 0


:read_current
rem  The version this folder builds. ModInfo.cs is the assembly's own answer;
rem  CHANGELOG.md's newest heading is the same answer written for humans.
set "CURRENT="
if not exist "%ROOT%\src\SeaPowerAdvanced\ModInfo.cs" goto read_current_changelog
for /f "tokens=2 delims==" %%v in ('findstr /c:"const string Version" "%ROOT%\src\SeaPowerAdvanced\ModInfo.cs" 2^>nul') do if not defined CURRENT set "CURRENT=%%v"
if not defined CURRENT goto read_current_changelog
set "CURRENT=!CURRENT:;=!"
set CURRENT=%CURRENT:"=%
if not defined CURRENT goto read_current_changelog
for /f "tokens=1" %%w in ("%CURRENT%") do set "CURRENT=%%w"
call :is_version "!CURRENT!"
if defined ISVER goto read_current_done

:read_current_changelog
set "CURRENT="
if not exist "%ROOT%\CHANGELOG.md" exit /b 0
for /f "tokens=2" %%v in ('findstr /r /c:"^## [0-9]" "%ROOT%\CHANGELOG.md" 2^>nul') do if not defined CURRENT set "CURRENT=%%v"
call :is_version "!CURRENT!"
if not defined ISVER set "CURRENT="

:read_current_done
>> "%LOG%" echo this folder : !CURRENT!
call :say "version in this folder: !CURRENT!"
exit /b 0


:read_installed
rem  Three places know the installed version, in order of how directly they know
rem  it. version.txt is written by build.cmd at install time; the mod's own log
rem  says which build actually started; BepInEx's log says what it loaded.
set "INSTALLED="
set "VSOURCE=none"
if exist "!INSTALLED_AT!\version.txt" goto read_installed_versiontxt
if exist "!INSTALLED_AT!\seapoweradvanced.log" goto read_installed_modlog
goto read_installed_bepinex

:read_installed_versiontxt
for /f "usebackq delims=" %%v in ("!INSTALLED_AT!\version.txt") do if not defined INSTALLED set "INSTALLED=%%v"
call :is_version "!INSTALLED!"
if not defined ISVER set "INSTALLED="
if defined INSTALLED set "VSOURCE=version.txt in the mod folder"
if defined INSTALLED goto read_installed_done

:read_installed_modlog
if not exist "!INSTALLED_AT!\seapoweradvanced.log" goto read_installed_bepinex
for /f "tokens=7" %%v in ('findstr /c:"Sea Power Advanced" "!INSTALLED_AT!\seapoweradvanced.log" 2^>nul') do set "INSTALLED=%%v"
call :is_version "!INSTALLED!"
if not defined ISVER set "INSTALLED="
if defined INSTALLED set "VSOURCE=the mod's own log, last time it started"
if defined INSTALLED goto read_installed_done

:read_installed_bepinex
if not exist "!GAME!\BepInEx\LogOutput.log" goto read_installed_done
for /f "tokens=8" %%v in ('findstr /c:"Loading [Sea Power Advanced" "!GAME!\BepInEx\LogOutput.log" 2^>nul') do set "INSTALLED=%%v"
set "INSTALLED=!INSTALLED:]=!"
call :is_version "!INSTALLED!"
if not defined ISVER set "INSTALLED="
if defined INSTALLED set "VSOURCE=BepInEx's log, last time it loaded the mod"

:read_installed_done
if not defined INSTALLED set "VSOURCE=no record found - treated as an old install"
>> "%LOG%" echo installed at : !INSTALLED_AT!
>> "%LOG%" echo installed ver: !INSTALLED!  (!VSOURCE!)
call :say "installed version: !INSTALLED!"
call :say "  from: !VSOURCE!"
exit /b 0


rem ===========================================================================
rem  Older, the same, or newer
rem ===========================================================================
:compare
set "DECISION="
if not defined INSTALLED goto compare_unknown
call :ver_lt "!INSTALLED!" "!CURRENT!"
if defined VLT goto compare_older
call :ver_lt "!CURRENT!" "!INSTALLED!"
if defined VLT goto compare_newer
goto compare_same

:compare_unknown
rem  A copy with no version record is either very old or was put there by hand.
rem  Every migration below looks for the files it would change and skips when
rem  they are not there, so checking all of them is safe.
set "INSTALLED=0.1.0"
set "DECISION=unknown"
>> "%LOG%" echo decision : unknown older install - every migration is checked
call :say "no version on record: checking every change since 0.1.0"
exit /b 0

:compare_older
set "DECISION=older"
>> "%LOG%" echo decision : older - !INSTALLED! to !CURRENT!
call :say "older than this folder: !INSTALLED! to !CURRENT!"
exit /b 0

:compare_same
set "DECISION=same"
>> "%LOG%" echo decision : already current
call :say "already at !CURRENT! in the game folder."
exit /b 0

:compare_newer
set "DECISION=newer"
>> "%LOG%" echo decision : the install is NEWER than this folder
exit /b 0


rem ===========================================================================
rem  The changes the changelog stands behind, oldest first
rem ===========================================================================
:migrations
>> "%LOG%" echo.
>> "%LOG%" echo [ changelog migrations, !INSTALLED! to !CURRENT! ]
echo.
echo [2/5] What this install still needs...
call :ver_lt "!INSTALLED!" "0.5.0"
if defined VLT call :mig_streamingassets
call :ver_lt "!INSTALLED!" "0.10.0"
if defined VLT call :note "0.10.0 - offensive ECM arrived. No files: it is all in the DLL."
call :ver_lt "!INSTALLED!" "0.15.1"
if defined VLT call :note "0.15.1 - civilian traffic was dropped. It never wrote a file: nothing to remove."
call :ver_lt "!INSTALLED!" "0.17.1"
if defined VLT call :note "0.17.1 - the ECM cones draw. No files: it is all in the DLL."
call :ver_lt "!INSTALLED!" "0.18.0"
if defined VLT call :note "0.18.0 - the missile shield was removed. It never wrote a file."
call :ver_lt "!INSTALLED!" "0.18.1"
if defined VLT call :mig_jamrange
call :ver_lt "!INSTALLED!" "0.18.3"
if defined VLT call :note "0.18.3 - a jammed missile is no longer set off, it is flown down."
call :ver_lt "!INSTALLED!" "0.19.0"
if defined VLT call :mig_dive
call :ver_lt "!INSTALLED!" "0.20.0"
if defined VLT call :note "0.20.0 - the dive is timed and its nose stops at 90 degrees. No files: all in the DLL."
call :ver_lt "!INSTALLED!" "0.21.0"
if defined VLT call :note "0.21.0 - the dive uses the game's own lock-loss state, never nulls a reference, and lets the game's water-dip path remove the missile. No files: all in the DLL."
call :ver_lt "!INSTALLED!" "0.22.0"
if defined VLT call :note "0.22.0 - the dive gains four limits (guidance channels, engagement interval, reaction delay, radar-seeker filter). They are new settings with defaults."
call :ver_lt "!INSTALLED!" "0.22.1"
if defined VLT call :mig_dive_limits
call :ver_lt "!INSTALLED!" "0.23.0"
if defined VLT call :note "0.23.0 - offensive ECM's sensor hold gains two limits: a task-force channel budget and a hold window with a recovery period. They are new settings with defaults."
call :ver_lt "!INSTALLED!" "0.24.0"
if defined VLT call :note "0.24.0 - Sensors is grouped into two submenus instead of twenty-odd entries, and the multiplayer client paths are declared and self-reporting. No files: all in the DLL."
call :ver_lt "!INSTALLED!" "0.24.1"
if defined VLT call :note "0.24.1 - build fix: the DLL released as 0.24.0 did not compile (a missing using in Multiplayer.cs). No files: all in the DLL."
call :ver_lt "!INSTALLED!" "0.24.2"
if defined VLT call :note "0.24.2 - the whole-task-force defensive ECM entry is a switch: click it again to stand the force down. No files: all in the DLL."
call :ver_lt "!INSTALLED!" "0.25.0"
if defined VLT call :note "0.25.0 - the force datalink: in a co-op session, contacts this force holds are handed to the other player's force. A new setting appears (CoopDatalink, on). No files: all in the DLL."
call :ver_lt "!INSTALLED!" "0.29.0"
if defined VLT call :note "0.29.0 - the LIDAR is gone. It could not be made to work reliably: a laser that only confirms contacts the force ALREADY holds is not a sensor, it is a harder way to read a picture you already have, and every version of it cost a build. Removed whole - the menu entries, the map overlay, the infrared view, the key and every one of its settings. Nothing else is affected. No files: all in the DLL. Any lidar.txt already in the mod folder is now just a leftover and can be deleted."
exit /b 0

:mig_dive_limits
set "_MIGRATED="
call :cfg_value EcmDiveChannels 2
if defined HASVAL call :say "0.22.1 APPLY  EcmDiveChannels is still 2; the mod moves it to 3 on the next start"
if defined HASVAL call :logline "APPLY 0.22.1 - EcmDiveChannels is still 2, moved to 3 at load"
if defined HASVAL set "_MIGRATED=1"
call :cfg_value EcmDiveIntervalSeconds 4
if defined HASVAL call :say "0.22.1 APPLY  EcmDiveIntervalSeconds is still 4; the mod moves it to 2 on the next start"
if defined HASVAL call :logline "APPLY 0.22.1 - EcmDiveIntervalSeconds is still 4, moved to 2 at load"
if defined HASVAL set "_MIGRATED=1"
call :cfg_value EcmDiveReactionSeconds 2
if defined HASVAL call :say "0.22.1 APPLY  EcmDiveReactionSeconds is still 2; the mod moves it to 1 on the next start"
if defined HASVAL call :logline "APPLY 0.22.1 - EcmDiveReactionSeconds is still 2, moved to 1 at load"
if defined HASVAL set "_MIGRATED=1"
if defined _MIGRATED exit /b 0
call :say "0.22.1 ok     dive limits need no change"
>> "%LOG%" echo   skip 0.22.1 - dive limits are not the old defaults
exit /b 0


:mig_streamingassets
if not exist "!LEGACY!" goto mig_streamingassets_clean
if /i "!MODE!"=="check" goto mig_streamingassets_would
>> "%LOG%" echo   APPLY  0.5.0 - removing the pre-0.5.0 copy: !LEGACY!
call :say "0.5.0  APPLY  old StreamingAssets copy: !LEGACY!"
call :say "       a copy there is loaded as well, so it is backed up and removed."
if not exist "!KEEP!" mkdir "!KEEP!" 2>nul
xcopy /e /i /y /q "!LEGACY!" "!KEEP!\legacy-StreamingAssets-!STAMP!" >> "%LOG%" 2>&1
rd /s /q "!LEGACY!" 2>nul
if exist "!LEGACY!" goto mig_streamingassets_left
call :say "       removed."
>> "%LOG%" echo   ok - legacy copy gone
exit /b 0

:mig_streamingassets_left
call :say "       COULD NOT REMOVE IT - close Sea Power and run uninstall.cmd"
>> "%LOG%" echo   LEFT BEHIND: !LEGACY!
exit /b 0

:mig_streamingassets_would
call :say "0.5.0  WOULD  remove the pre-0.5.0 copy: !LEGACY!"
>> "%LOG%" echo   WOULD REMOVE 0.5.0 - the legacy StreamingAssets copy
exit /b 0

:mig_streamingassets_clean
call :say "0.5.0  ok     no pre-0.5.0 copy in StreamingAssets"
>> "%LOG%" echo   skip 0.5.0 - no StreamingAssets copy
exit /b 0


:mig_jamrange
set "_MIGRATED="
call :cfg_value JamRangeNm 40
if defined HASVAL call :say "0.18.1 APPLY  JamRangeNm is still 40; the mod moves it to 150 on the next start"
if defined HASVAL call :logline "APPLY 0.18.1 - JamRangeNm is still 40, moved to 150 at load"
if defined HASVAL set "_MIGRATED=1"
call :cfg_value JamAirRangeNm 90
if defined HASVAL call :say "0.18.1 APPLY  JamAirRangeNm is still 90; the mod moves it to 150 on the next start"
if defined HASVAL call :logline "APPLY 0.18.1 - JamAirRangeNm is still 90, moved to 150 at load"
if defined HASVAL set "_MIGRATED=1"
if defined _MIGRATED exit /b 0
call :say "0.18.1 ok     jam ranges need no change"
>> "%LOG%" echo   skip 0.18.1 - jam ranges are not the old defaults
exit /b 0


:mig_dive
call :cfg_value EcmDiveNm 2
if defined HASVAL call :say "0.19.0 APPLY  EcmDiveNm is still 2; the mod moves it to 10 on the next start"
if defined HASVAL call :logline "APPLY 0.19.0 - EcmDiveNm is still 2, moved to 10 at load"
if defined HASVAL exit /b 0
call :cfg_value EcmDiveNm 0
if not defined HASVAL goto mig_dive_default
call :say "0.19.0 ok     EcmDiveNm = 0: the dive is switched off, left exactly as you set it"
>> "%LOG%" echo   skip 0.19.0 - EcmDiveNm is 0 by choice
exit /b 0
:mig_dive_default
call :say "0.19.0 ok     dive range needs no change"
>> "%LOG%" echo   skip 0.19.0 - EcmDiveNm is not the old two mile figure
exit /b 0


rem ===========================================================================
rem  What the player gains, straight from the changelog headings
rem ===========================================================================
:whats_new
>> "%LOG%" echo.
>> "%LOG%" echo [ what this update adds ]
echo.
echo [3/5] What you are gaining...
call :ver_lt "!INSTALLED!" "0.16.0"
if defined VLT call :say "0.16.0  offensive ECM through the game's own jam order, on unidentified contacts"
call :ver_lt "!INSTALLED!" "0.17.0"
if defined VLT call :say "0.17.0  the ECM cones are drawn on the map"
call :ver_lt "!INSTALLED!" "0.17.1"
if defined VLT call :say "0.17.1  the cones draw in the right place, from the game's own map grid"
call :ver_lt "!INSTALLED!" "0.18.1"
if defined VLT call :say "0.18.1  offensive ECM reaches 150 nm, ship to ship and ship to air"
call :ver_lt "!INSTALLED!" "0.19.0"
if defined VLT call :say "0.19.0  a jammed missile is flown into the sea from 10 nm"
call :ver_lt "!INSTALLED!" "0.20.0"
if defined VLT call :say "0.20.0  the dive is timed and visible, and the nose stops at 90 degrees"
call :ver_lt "!INSTALLED!" "0.21.0"
if defined VLT call :say "0.21.0  a jammed missile is flown into the water through the game's own flight model, and the game removes it there"
call :ver_lt "!INSTALLED!" "0.22.0"
if defined VLT call :say "0.22.0  the dive is limited like a real ship's: guidance channels, an engagement interval, a reaction delay, radar seekers only"
call :ver_lt "!INSTALLED!" "0.22.1"
if defined VLT call :say "0.22.1  the shipped setting is now 3 channels, 2 s between engagements, 1 s to react"
call :ver_lt "!INSTALLED!" "0.23.0"
if defined VLT call :say "0.23.0  offensive ECM holds a contact's sensors down only so long, only on so many contacts, and then they get them back"
call :ver_lt "!INSTALLED!" "0.24.0"
if defined VLT call :say "0.24.0  the Sensors menu is two submenus instead of a long list, and every multiplayer client path now states what it does and records whether it was tried"
call :ver_lt "!INSTALLED!" "0.24.1"
if defined VLT call :say "0.24.1  build fix - the 0.24.0 DLL did not compile; replace it with this one"
call :ver_lt "!INSTALLED!" "0.24.2"
if defined VLT call :say "0.24.2  defensive ECM for the whole task force is a switch now: click it again to stand the force down"
call :ver_lt "!INSTALLED!" "0.25.0"
if defined VLT call :say "0.25.0  new: the force datalink. In a co-op session, contacts your force holds are handed to the other player's force; datalink.txt says what was shared"
call :ver_lt "!INSTALLED!" "0.29.0"
if defined VLT call :say "0.29.0  the LIDAR has been removed entirely - the menu entries, the map overlay, the infrared view and all of its settings"
exit /b 0


rem ===========================================================================
rem  Doing it
rem ===========================================================================
:stamp
set "STAMP=%DATE%"
set "STAMP=!STAMP:/=-!"
set "STAMP=!STAMP:.=-!"
set "STAMP=!STAMP:,=-!"
set "STAMP=!STAMP: =_!"
set "CLOCK=%TIME%"
set "CLOCK=!CLOCK::=-!"
set "CLOCK=!CLOCK:.=-!"
set "CLOCK=!CLOCK: =0!"
set "STAMP=!STAMP!_!CLOCK!"
exit /b 0


:backup
rem  Before anything is replaced, what is about to be replaced is kept. An
rem  update that goes wrong is then a copy back, not a rebuild.
set "BACKUP=none - there was no earlier build"
if not exist "!INSTALLED_AT!\SeaPowerAdvanced.dll" goto backup_nothing

set "BACKUP=%KEEP%\before-!INSTALLED!_!STAMP!"
if not exist "!BACKUP!" mkdir "!BACKUP!" 2>nul
copy /y "!INSTALLED_AT!\SeaPowerAdvanced.dll" "!BACKUP!" >nul 2>&1
if exist "!INSTALLED_AT!\_info.ini" copy /y "!INSTALLED_AT!\_info.ini" "!BACKUP!" >nul 2>&1
if exist "!INSTALLED_AT!\version.txt" copy /y "!INSTALLED_AT!\version.txt" "!BACKUP!" >nul 2>&1
>> "%LOG%" echo backed up  : !BACKUP!
call :say "kept the old build in: !BACKUP!"
exit /b 0


:backup_nothing
>> "%LOG%" echo no earlier build to keep at !INSTALLED_AT!
exit /b 0

:apply
echo.
echo [4/5] Building and installing !CURRENT!...
>> "%LOG%" echo.
>> "%LOG%" echo [4/5] build and install
if defined NOBUILD goto apply_nobuild
if not exist "%ROOT%\build.cmd" goto apply_no_builder
>> "%LOG%" echo running: build.cmd install nopause
call "%ROOT%\build.cmd" install nopause
if errorlevel 1 goto apply_build_failed
exit /b 0

:apply_nobuild
rem  No compiler: copy the package build.cmd already made. Anything missing is
rem  named, rather than copied from somewhere else and hoped about.
>> "%LOG%" echo nobuild: copying from %DIST%
if not exist "%DIST%\SeaPowerAdvanced.dll" goto apply_no_package
if not exist "!PLUGIN!" mkdir "!PLUGIN!" 2>nul
copy /y "%DIST%\SeaPowerAdvanced.dll" "!PLUGIN!" >nul 2>&1
copy /y "%DIST%\_info.ini" "!PLUGIN!" >nul 2>&1
copy /y "%DIST%\version.txt" "!PLUGIN!" >nul 2>&1
if not exist "!PLUGIN!\SeaPowerAdvanced.dll" goto apply_copy_failed
>> "%LOG%" echo copied the dist package into !PLUGIN!
call :say "copied the package from dist\ into the game"
exit /b 0

:apply_no_package
call :say "nobuild was asked for, but dist\ has no SeaPowerAdvanced.dll."
call :say "Run build.cmd once without nobuild: it makes the package."
>> "%LOG%" echo ERROR nobuild: no package in dist
exit /b 1

:apply_no_builder
call :say "build.cmd is missing from this folder, so nothing can be built."
>> "%LOG%" echo ERROR build.cmd missing
exit /b 1

:apply_build_failed
call :say "the build failed - its own log says why."
>> "%LOG%" echo ERROR build.cmd returned a failure
exit /b 1

:apply_copy_failed
call :say "could not copy into the game folder. Close Sea Power, or run as administrator."
>> "%LOG%" echo ERROR copy into !PLUGIN! failed
exit /b 1


:verify
echo [5/5] Checking the result...
>> "%LOG%" echo.
>> "%LOG%" echo [5/5] verify
set "NOWVER="
if exist "!PLUGIN!\version.txt" for /f "usebackq delims=" %%v in ("!PLUGIN!\version.txt") do if not defined NOWVER set "NOWVER=%%v"
if not exist "!PLUGIN!\SeaPowerAdvanced.dll" goto verify_no_dll
if not defined NOWVER goto verify_no_version
call :say "installed now: !NOWVER! in !PLUGIN!"
>> "%LOG%" echo installed now: !NOWVER!
if /i "!NOWVER!"=="!CURRENT!" goto verify_ok
call :say "note: the package says !NOWVER!, this folder says !CURRENT!."
>> "%LOG%" echo note: version.txt !NOWVER! does not match source !CURRENT!
goto verify_done

:verify_ok
call :say "matches this folder's !CURRENT!."
>> "%LOG%" echo verified: !NOWVER! matches the source
goto verify_done

:verify_no_dll
set "VERIFY_FAIL=1"
call :say "PROBLEM: no SeaPowerAdvanced.dll in !PLUGIN! after the install."
>> "%LOG%" echo ERROR no dll after install in !PLUGIN!
goto verify_done

:verify_no_version
call :say "the DLL is there, but no version.txt - the build is older than this file."
>> "%LOG%" echo note: installed dll has no version.txt
:verify_done
exit /b 0


rem ===========================================================================
rem  Small helpers
rem ===========================================================================
:say
echo   %~1
>> "%LOG%" echo   %~1
exit /b 0

:logline
>> "%LOG%" echo   %~1
exit /b 0

:note
>> "%LOG%" echo   NOTE  %~1
call :say "       %~1"
exit /b 0

:is_version
rem  %1 = a candidate version. Sets ISVER=1 when it looks like 1.2.3.
set "ISVER="
if "%~1"=="" exit /b 0
echo %~1| findstr /r /c:"^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul
if not errorlevel 1 set "ISVER=1"
exit /b 0

:ver_lt
rem  Sets VLT=1 when %1 is older than %2. Three numeric parts, so that 0.9.0
rem  counts as older than 0.10.0 - which a plain string comparison gets wrong.
set "VLT="
set "_A=%~1"
set "_B=%~2"
if "!_A!"=="" exit /b 0
if "!_B!"=="" exit /b 0
call :ver3 _A
call :ver3 _B
for /f "tokens=1,2,3 delims=." %%a in ("!_A!") do (
    set "_A1=%%a"
    set "_A2=%%b"
    set "_A3=%%c"
)
for /f "tokens=1,2,3 delims=." %%a in ("!_B!") do (
    set "_B1=%%a"
    set "_B2=%%b"
    set "_B3=%%c"
)
if !_A1! lss !_B1! set "VLT=1"
if defined VLT exit /b 0
if !_A1! gtr !_B1! exit /b 0
if !_A2! lss !_B2! set "VLT=1"
if defined VLT exit /b 0
if !_A2! gtr !_B2! exit /b 0
if !_A3! lss !_B3! set "VLT=1"
exit /b 0

:ver3
rem  %1 = the name of a variable holding a version. Pads it to three parts.
set "_T=!%~1!"
if "!_T!"=="" exit /b 0
:ver3_loop
set "_N=0"
for %%p in (!_T:.= !) do set /a _N+=1
if !_N! geq 3 goto ver3_done
set "_T=!_T!.0"
goto ver3_loop
:ver3_done
set "%~1=!_T!"
exit /b 0

:cfg_value
rem  %1 = key, %2 = value. Sets HASVAL=1 when the settings file holds exactly
rem  that pair. Only setting lines count: BepInEx writes its descriptions with a
rem  leading ##, and those mention the old figures too.
set "HASVAL="
if not exist "!CFG!" exit /b 0
for /f "usebackq tokens=1,2 delims== " %%k in ("!CFG!") do if /i "%%k"=="%~1" if /i "%%l"=="%~2" set "HASVAL=1"
exit /b 0

:game_running
set "RUNNING="
tasklist /fi "imagename eq Sea Power.exe" 2>nul | findstr /i /c:"Sea Power.exe" >nul
if not errorlevel 1 set "RUNNING=Sea Power.exe"
tasklist /fi "imagename eq SeaPower.exe" 2>nul | findstr /i /c:"SeaPower.exe" >nul
if not errorlevel 1 set "RUNNING=SeaPower.exe"
exit /b 0


rem ===========================================================================
rem  The endings
rem ===========================================================================
:selftest
rem  Runs the two pieces of logic that can be silently wrong - which version is
rem  older, and whether a string is a version at all - against cases with known
rem  answers. No game, no build, nothing changed: it proves the update logic on
rem  this PC, which is the part a wrong answer would quietly ruin.
echo.
echo  Self-test: version order and version shape
>> "%LOG%" echo.
>> "%LOG%" echo [ selftest ]
set "FAILED=0"
call :expect_lt "0.9.0" "0.10.0" "older"
call :expect_lt "0.18.9" "0.18.10" "older"
call :expect_lt "0.18.3" "0.19.0" "older"
call :expect_lt "0.1.0" "0.5.0" "older"
call :expect_lt "0.19.0" "0.18.3" "not older"
call :expect_lt "0.5.0" "0.5.0" "not older"
call :expect_lt "0.5" "0.5.0" "not older"
call :expect_lt "10.0.0" "9.9.9" "not older"
call :expect_valid "0.19.0" "valid"
call :expect_valid "1.2" "not valid"
call :expect_valid "abc" "not valid"
if not "!FAILED!"=="0" goto selftest_bad
echo.
echo  PASS - both are right on this PC.
>> "%LOG%" echo selftest: PASS
goto finished

:selftest_bad
echo.
echo  FAIL - !FAILED! case(s) above came out wrong. Send update.txt.
>> "%LOG%" echo selftest: FAIL !FAILED! case(s)
goto finished_bad

:expect_lt
rem  %1 and %2 are versions, %3 is the answer this case should give.
call :ver_lt "%~1" "%~2"
set "_WANT="
if /i "%~3"=="older" set "_WANT=1"
set "_GOT=0"
if defined VLT set "_GOT=1"
if "!_GOT!"=="!_WANT!" goto expect_lt_ok
set /a FAILED+=1
echo    FAIL   %~1 older than %~2 should be "%~3"
>> "%LOG%" echo   FAIL %~1 older than %~2 should be %~3
exit /b 0
:expect_lt_ok
echo    pass   %~1 vs %~2 : "%~3"
>> "%LOG%" echo   pass %~1 vs %~2 : %~3
exit /b 0

:expect_valid
call :is_version "%~1"
set "_WANT="
if /i "%~2"=="valid" set "_WANT=1"
set "_GOT=0"
if defined ISVER set "_GOT=1"
if "!_GOT!"=="!_WANT!" goto expect_valid_ok
set /a FAILED+=1
echo    FAIL   "%~1" should be "%~2"
>> "%LOG%" echo   FAIL "%~1" should be %~2
exit /b 0
:expect_valid_ok
echo    pass   "%~1" : "%~2"
>> "%LOG%" echo   pass "%~1" : %~2
exit /b 0

:already_current
echo.
>> "%LOG%" echo.
>> "%LOG%" echo result: already current - nothing to do
echo  The game folder already has !CURRENT!, which is what this folder builds.
if exist "!INSTALLED_AT!\version.txt" goto already_current_done
echo.
echo  It was installed before version.txt existed, so its version came from a
echo  log. "update.cmd force" rebuilds it and leaves a version.txt behind.
>> "%LOG%" echo note: no version.txt in the install - a build from before it existed
:already_current_done
echo.
echo  Nothing to do. "update.cmd force" rebuilds and reinstalls anyway, and
echo  "update.cmd check" only reports.
goto finished

:check_only
echo ============================================================
echo  CHECK ONLY - nothing has been built or changed.
echo ============================================================
>> "%LOG%" echo.
>> "%LOG%" echo check only: nothing was built or changed
echo  Running update.cmd would take !INSTALLED! to !CURRENT!, as listed above.
echo.
call :say "check complete"
goto finished

:installed_newer
echo  The game folder has !INSTALLED!, which is newer than !CURRENT! here.
>> "%LOG%" echo.
>> "%LOG%" echo stopped: installed !INSTALLED! is newer than this folder's !CURRENT!
echo.
echo  This folder is an older copy of the project, so updating it would be a
echo  downgrade. Nothing was changed. Use the release folder that matches, or
echo  run build.cmd here on purpose if that is really what you want.
goto finished_bad

:not_installed
echo  No Sea Power Advanced is installed in:
echo    !PLUGIN!
>> "%LOG%" echo.
>> "%LOG%" echo stopped: nothing installed in !GAME!
echo.
echo  Nothing to update. Run build.cmd once - it builds *and* installs, and
echo  leaves a version.txt behind so this file can tell versions apart next time.
goto finished_bad

:no_current
echo  Could not read this folder's own version.
>> "%LOG%" echo.
>> "%LOG%" echo stopped: no version in ModInfo.cs or CHANGELOG.md
echo.
echo  Both src\SeaPowerAdvanced\ModInfo.cs and CHANGELOG.md are missing or
echo  unreadable, so there is no current version to update to.
goto finished_bad

:no_game
echo  Could not find Sea Power on this PC.
>> "%LOG%" echo.
>> "%LOG%" echo stopped: Sea Power folder not found
echo.
echo  Run build.cmd once - it searches for the game and writes down where it
echo  found it, and then this file finds it too.
goto finished_bad

:game_busy
echo  Sea Power is running (!RUNNING!).
>> "%LOG%" echo.
>> "%LOG%" echo stopped: Sea Power is running - the DLL cannot be replaced
echo.
echo  A loaded DLL cannot be overwritten. Close Sea Power, then run this again.
goto finished_bad

:apply_failed
echo.
echo  The update did not finish. The old build, if there was one, is in:
echo    !BACKUP!
>> "%LOG%" echo.
>> "%LOG%" echo result: FAILED - the old build is in !BACKUP!
echo.
echo  Send update.txt: it has the build's own output as well.
goto finished_bad

:summary
echo.
echo ============================================================
if /i "!DECISION!"=="same" echo  DONE - already at !CURRENT!, reinstalled.
if /i not "!DECISION!"=="same" echo  DONE - !INSTALLED! to !CURRENT!.
>> "%LOG%" echo.
>> "%LOG%" echo [ result ]
>> "%LOG%" echo from: !INSTALLED!  to: !CURRENT!
>> "%LOG%" echo old build kept in: !BACKUP!
>> "%LOG%" echo update.txt copied into the mod folder as well
echo.
echo  Start Sea Power. In BepInEx\LogOutput.log you should see:
echo    Loading [Sea Power Advanced !CURRENT!]
>> "%LOG%" echo next: start the game, look for "Loading [Sea Power Advanced !CURRENT!]"
echo.
echo  Settings were not touched: your jam ranges, dive range and menu choices
echo  carry over, and the mod moves any figure a release changed when it starts.
if not defined VERIFY_FAIL goto finished
>> "%LOG%" echo result: FAILED - the install did not put the DLL where it belongs
echo.
echo  But the DLL is not where it belongs - see [5/5] above and update.txt.
goto finished_bad


:copy_log
if not defined PLUGIN exit /b 0
if not exist "!PLUGIN!" exit /b 0
copy /y "%LOG%" "!PLUGIN!\update.txt" >nul 2>&1
exit /b 0

:finished
>> "%LOG%" echo.
>> "%LOG%" echo finished ok %DATE% %TIME%
if not defined PLUGIN goto finished_logged
>> "%LOG%" echo update.txt also copied to !PLUGIN!
:finished_logged
call :copy_log
echo  Report: %LOG%
echo ============================================================
echo.
echo Press any key to close this window.
pause >nul
endlocal
exit /b 0

:finished_bad
>> "%LOG%" echo.
>> "%LOG%" echo finished with a stop %DATE% %TIME%
call :copy_log
echo  Report: %LOG%
echo ============================================================
echo.
echo Press any key to close this window.
pause >nul
endlocal
exit /b 1
