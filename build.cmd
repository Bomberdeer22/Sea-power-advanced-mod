@echo off
rem ===========================================================================
rem  Build Sea Power Advanced - no PowerShell, no admin rights, no installer.
rem
rem  Double-click this file. It pauses at the end and writes everything to
rem  build-log.txt, so a window closing can never lose the error message.
rem
rem  Usage:
rem     build.cmd             build and install into the game  (default)
rem     build.cmd dist        build only, into dist\SeaPowerAdvanced
rem     build.cmd install     build and install into the game
rem
rem  Needs nothing pre-installed. It fetches, with curl and tar (both shipped
rem  with Windows 10/11, neither a script, so the PowerShell execution policy
rem  never comes into it):
rem
rem    .NET SDK                 -> .dotnet\    about 220 MB, once
rem    BepInEx + the Anchor     -> the game folder   about 650 KB, once
rem    Chain preloader
rem
rem  Anchor Chain's Workshop copy is NOT needed: the mod loads as a plain
rem  BepInEx plugin. If AnchorChain.dll happens to be installed, the Anchor
rem  Chain entry point is compiled in as well, for Workshop builds.
rem ===========================================================================
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "DOTNET_DIR=%ROOT%\.dotnet"
set "DOTNET_EXE=%DOTNET_DIR%\dotnet.exe"
set "PROJECT=%ROOT%\src\SeaPowerAdvanced\SeaPowerAdvanced.csproj"
set "DIST=%ROOT%\dist\SeaPowerAdvanced"
set "PROPS=%ROOT%\Directory.Build.local.props"
set "LOG=%ROOT%\build-log.txt"
set "BUILDLOG=%ROOT%\build-output.txt"

rem Default action when double-clicked: install. That is what people want.
set "ACTION=install"
if /i "%~1"=="dist" set "ACTION=dist"

> "%LOG%" echo Sea Power Advanced - build log
>> "%LOG%" echo started %DATE% %TIME%
>> "%LOG%" echo folder  %ROOT%
>> "%LOG%" echo action  %ACTION%
>> "%LOG%" echo.

echo ============================================================
echo  Sea Power Advanced - build
echo ============================================================
echo  Folder : %ROOT%
echo  Action : %ACTION%
echo  Log    : %LOG%
echo.
echo  If this window ever closes by itself, open build-log.txt and send it.
echo.

call :main
set "RC=%ERRORLEVEL%"

echo.
echo ============================================================
if "%RC%"=="0" echo  DONE - the build succeeded.
if not "%RC%"=="0" echo  STOPPED with an error. The lines above say what failed.
if not "%RC%"=="0" echo  Everything is saved in: %LOG%
echo ============================================================
>> "%LOG%" echo.
>> "%LOG%" echo finished with code %RC% at %DATE% %TIME%
echo.
echo Press any key to close this window.
pause >nul
endlocal
exit /b


rem ===========================================================================
rem  main
rem ===========================================================================
:main
call :step_sdk
if errorlevel 1 exit /b 1

call :step_paths
if errorlevel 1 exit /b 1

call :step_prereq
if errorlevel 1 exit /b 1

call :step_build
if errorlevel 1 exit /b 1

call :step_package
if errorlevel 1 exit /b 1

if /i not "%ACTION%"=="install" goto main_done
call :step_install
if errorlevel 1 exit /b 1

:main_done
exit /b 0


rem ===========================================================================
:step_sdk
echo [1/5] Looking for a .NET SDK...
>> "%LOG%" echo [1/5] sdk

if exist "%DOTNET_EXE%" (
    echo       using the portable copy in .dotnet
    >> "%LOG%" echo       portable %DOTNET_EXE%
    goto sdk_ok
)

where dotnet >nul 2>&1
if not errorlevel 1 (
    set "DOTNET_EXE=dotnet"
    echo       using the .NET SDK already installed on this PC
    >> "%LOG%" echo       found on PATH
    goto sdk_ok
)

echo       none found - downloading a portable SDK (about 220 MB, one time).
echo       Nothing is installed system-wide; it stays inside this folder.
>> "%LOG%" echo       downloading portable sdk

where curl >nul 2>&1
if errorlevel 1 goto sdk_no_curl
where tar >nul 2>&1
if errorlevel 1 goto sdk_no_tar

if not exist "%DOTNET_DIR%" mkdir "%DOTNET_DIR%"
set "ZIP=%ROOT%\dotnet-sdk.zip"
set "GOT="

rem Three sources: the short link first, then a pinned build on both CDNs.
call :fetch_sdk "https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.zip"
if not defined GOT call :fetch_sdk "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.404/dotnet-sdk-8.0.404-win-x64.zip"
if not defined GOT call :fetch_sdk "https://dotnetcli.azureedge.net/dotnet/Sdk/8.0.404/dotnet-sdk-8.0.404-win-x64.zip"
if not defined GOT goto sdk_download_failed

echo       extracting...
>> "%LOG%" echo       extracting
tar -xf "%ZIP%" -C "%DOTNET_DIR%"
if errorlevel 1 goto sdk_extract_failed
del /q "%ZIP%" >nul 2>&1
if not exist "%DOTNET_EXE%" goto sdk_extract_failed

echo       portable SDK ready.
>> "%LOG%" echo       portable sdk ready
goto sdk_ok

:fetch_sdk
echo       fetching %~1
>> "%LOG%" echo       url %~1
curl -L --fail --retry 2 --ssl-no-revoke -o "%ZIP%" "%~1"
if errorlevel 1 exit /b 0
if not exist "%ZIP%" exit /b 0
set "GOT=1"
exit /b 0

:sdk_no_curl
echo       curl.exe is not available, and it is needed to download the SDK.
>> "%LOG%" echo       curl.exe missing
echo       Install the .NET 8 SDK from https://dotnet.microsoft.com/download
echo       and run this file again - it will find it.
exit /b 1

:sdk_no_tar
echo       tar.exe is not available, and it is needed to unpack the SDK.
>> "%LOG%" echo       tar.exe missing
echo       Install the .NET 8 SDK from https://dotnet.microsoft.com/download
echo       and run this file again - it will find it.
exit /b 1

:sdk_download_failed
echo       The download did not work.
>> "%LOG%" echo       download failed
echo.
echo       Either this PC cannot reach the internet, or a proxy is blocking it.
echo       The alternative is a normal install:
echo         https://dotnet.microsoft.com/download    (.NET 8 SDK)
echo       Then run build.cmd again - it finds the SDK automatically.
exit /b 1

:sdk_extract_failed
echo       The SDK downloaded but could not be unpacked.
>> "%LOG%" echo       extract failed
echo       Install the .NET 8 SDK from https://dotnet.microsoft.com/download
echo       and run build.cmd again.
exit /b 1

:sdk_ok
echo.
exit /b 0


rem ===========================================================================
:step_paths
echo [2/5] Finding Sea Power...
>> "%LOG%" echo [2/5] paths

set "GAME="
set "AC="

if not exist "%PROPS%" goto paths_search
for /f "tokens=2 delims=<>" %%p in ('findstr /i "<SeaPowerDir>" "%PROPS%" 2^>nul') do if not defined GAME set "GAME=%%p"
for /f "tokens=2 delims=<>" %%p in ('findstr /i "<AnchorChainDir>" "%PROPS%" 2^>nul') do if not defined AC set "AC=%%p"
>> "%LOG%" echo       from props: game=!GAME! ac=!AC!

rem A props file left over from an earlier attempt may point somewhere that no
rem longer exists. Drop any path that does not check out, then search afresh.
if defined GAME if not exist "!GAME!\Sea Power_Data\Managed\Seapower-Scripts.dll" set "GAME="
if defined AC if not exist "!AC!\AnchorChain.dll" set "AC="
if not defined GAME goto paths_search
if not defined AC goto paths_search
goto paths_check

:paths_search
rem Ask Steam where it is installed, then look through the plausible libraries.
set "STEAM="
for /f "tokens=2,*" %%a in ('reg query "HKCU\Software\Valve\Steam" /v SteamPath 2^>nul ^| findstr /i "SteamPath"') do set "STEAM=%%b"
if not defined STEAM goto paths_scan
set "STEAM=!STEAM:/=\!"
call :try_root "!STEAM!"
if defined GAME if defined AC goto paths_check

:paths_scan
rem Scan the usual library names on every drive letter. Fast, and it avoids
rem parsing Steam's libraryfolders.vdf, whose quoting is easy to get wrong.
rem Every candidate is tried, every time: the game and Anchor Chain can live on
rem different drives, and stopping at the first game folder found would miss it.
rem :try_root skips anything that does not exist and never overwrites a hit.
for %%L in (C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (
    call :try_root "%%L:\Steam"
    call :try_root "%%L:\SteamLibrary"
    call :try_root "%%L:\Program Files (x86)\Steam"
    call :try_root "%%L:\Program Files\Steam"
)

if not defined GAME goto paths_no_game
if defined AC goto paths_check

rem This used to stop the build and send you to the Steam Workshop. It no longer does: Anchor
rem Chain is an optional second loader, and the mod installs as a plain BepInEx plugin. The only
rem part that matters is the preloader, and the next step installs that itself.
echo       AnchorChain.dll not found - building the BepInEx-only version.
echo       That is fine; it is how the mod is installed.
>> "%LOG%" echo       game=!GAME! ac=MISSING - building BepInEx-only
goto paths_check

:paths_no_game
>> "%LOG%" echo       game not found
if not exist "%PROPS%" copy /y "%ROOT%\Directory.Build.local.props.example" "%PROPS%" >nul
echo       Could not find Sea Power automatically.
echo.
echo       Open this file in Notepad:
echo         %PROPS%
echo       Set the two paths to match your PC, save, and run this again.
echo       It has just been created for you. Example:
echo.
echo         ^<SeaPowerDir^>C:\Program Files ^(x86^)\Steam\steamapps\common\Sea Power^</SeaPowerDir^>
echo         ^<AnchorChainDir^>C:\Program Files ^(x86^)\Steam\steamapps\workshop\content\1286220\3380210757^</AnchorChainDir^>
exit /b 1

:paths_check
>> "%LOG%" echo       game       !GAME!
>> "%LOG%" echo       anchorchain !AC!
echo       game        !GAME!
if defined AC echo       AnchorChain !AC!
if not defined AC echo       AnchorChain not installed ^(optional^)

if not exist "!GAME!\Sea Power_Data\Managed\Seapower-Scripts.dll" goto paths_bad_game
if defined AC if not exist "!AC!\AnchorChain.dll" set "AC="

call :write_props
echo.
exit /b 0

:paths_bad_game
echo       That folder is not a Sea Power install - it has no
echo       "Sea Power_Data\Managed\Seapower-Scripts.dll".
>> "%LOG%" echo       bad game folder
exit /b 1

:paths_bad_ac
echo       There is no AnchorChain.dll in:
echo         !AC!
>> "%LOG%" echo       bad anchorchain folder
exit /b 1


rem --- %1 = a Steam root (the folder that contains "steamapps") ---------------
:try_root
set "LIB=%~1"
if not exist "!LIB!\steamapps" exit /b 0

if not defined GAME if exist "!LIB!\steamapps\common\Sea Power\Sea Power_Data\Managed\Seapower-Scripts.dll" (
    set "GAME=!LIB!\steamapps\common\Sea Power"
)

if not defined AC if exist "!LIB!\steamapps\workshop\content\1286220\3380210757\AnchorChain.dll" (
    set "AC=!LIB!\steamapps\workshop\content\1286220\3380210757"
)

if not defined AC (
    for /d %%W in ("!LIB!\steamapps\workshop\content\1286220\*") do (
        if not defined AC if exist "%%~W\AnchorChain.dll" set "AC=%%~W"
    )
)
exit /b 0

:write_props
> "%PROPS%" echo ^<?xml version="1.0" encoding="utf-8"?^>
>> "%PROPS%" echo ^<Project^>
>> "%PROPS%" echo   ^<PropertyGroup^>
>> "%PROPS%" echo     ^<SeaPowerDir^>!GAME!^</SeaPowerDir^>
if defined AC >> "%PROPS%" echo     ^<AnchorChainDir^>!AC!^</AnchorChainDir^>
>> "%PROPS%" echo   ^<^/PropertyGroup^>
>> "%PROPS%" echo ^<^/Project^>
>> "%LOG%" echo       wrote %PROPS%
exit /b 0


rem ===========================================================================
:step_prereq
echo [3/5] Checking BepInEx in the game folder...
>> "%LOG%" echo [3/5] prerequisites

rem The mod cannot load at all without BepInEx inside the game folder, and Sea Power only gets
rem BepInEx through Anchor Chain's preloader. Anchor Chain publishes both together as
rem ACPreloader.zip - a complete BepInEx install plus the preloader DLL - so the manual copy
rem that used to be INSTALL.md step 1 now happens here.
rem
rem "latest/download" always points at the newest release, so this does not rot. The pinned
rem V1.2.0 archive is the fallback if that redirect is blocked.
set "BEPCORE=!GAME!\BepInEx\core\BepInEx.dll"
set "DOORSTOP=!GAME!\winhttp.dll"
set "DOORSTOPCFG=!GAME!\doorstop_config.ini"

if not exist "!BEPCORE!" goto prereq_install
if not exist "!DOORSTOP!" goto prereq_install
if not exist "!DOORSTOPCFG!" goto prereq_install
echo       already installed - nothing to do.
>> "%LOG%" echo       BepInEx + preloader present
exit /b 0

:prereq_install
echo       not installed yet - fetching it into the game folder.
>> "%LOG%" echo       BepInEx/preloader missing, installing

where curl >nul 2>&1
if errorlevel 1 goto prereq_no_curl
where tar >nul 2>&1
if errorlevel 1 goto prereq_no_tar

set "ACZIP=%ROOT%\acpreloader.zip"
set "GOT="
call :fetch_preloader "https://github.com/SeaPower-Modders/AnchorChain/releases/latest/download/ACPreloader.zip"
if not defined GOT call :fetch_preloader "https://github.com/SeaPower-Modders/AnchorChain/releases/download/V1.2.0/ACPreloader.zip"
if not defined GOT goto prereq_download_failed

echo       extracting into the game folder...
>> "%LOG%" echo       extracting into !GAME!
tar -xf "%ACZIP%" -C "!GAME!"
if errorlevel 1 goto prereq_extract_failed
del /q "%ACZIP%" >nul 2>&1

if not exist "!BEPCORE!" goto prereq_extract_failed
if not exist "!DOORSTOP!" goto prereq_extract_failed
echo       BepInEx and the Anchor Chain preloader are now in place.
>> "%LOG%" echo       installed BepInEx + preloader
exit /b 0

:fetch_preloader
echo       fetching %~1
>> "%LOG%" echo       url %~1
curl -L --fail --retry 2 --ssl-no-revoke -o "%ACZIP%" "%~1"
if errorlevel 1 exit /b 0
if not exist "%ACZIP%" exit /b 0
set "GOT=1"
exit /b 0

:prereq_download_failed
echo       Could not download BepInEx.
>> "%LOG%" echo       prereq download failed
echo.
echo       Check that this PC is online and run build.cmd again.
echo       If it keeps failing, install Anchor Chain by hand: INSTALL.md step 1.
exit /b 1

:prereq_extract_failed
echo       The download arrived but could not be unpacked into:
echo         !GAME!
>> "%LOG%" echo       prereq extract failed
echo.
echo       Two usual causes:
echo         - Sea Power is running. Close it, then run this again.
echo         - Windows blocked writing to Program Files.
echo           Right-click build.cmd, choose "Run as administrator",
echo           then run it again.
exit /b 1

:prereq_no_curl
echo       curl.exe was not found. It ships with Windows 10 and 11.
>> "%LOG%" echo       no curl
echo       Install Anchor Chain by hand instead: INSTALL.md step 1.
exit /b 1

:prereq_no_tar
echo       tar.exe was not found. It ships with Windows 10 and 11.
>> "%LOG%" echo       no tar
echo       Install Anchor Chain by hand instead: INSTALL.md step 1.
exit /b 1


rem ===========================================================================
:step_build
echo [4/5] Building the mod...
>> "%LOG%" echo [4/5] build

rem Output goes to a file as well as the screen: MSBuild's error lines are the
rem most useful thing to send if this fails.
"%DOTNET_EXE%" build "%PROJECT%" -c Release -v minimal -nologo > "%BUILDLOG%" 2>&1
set "BUILD_RC=%ERRORLEVEL%"
type "%BUILDLOG%"
>> "%LOG%" echo       build exit code !BUILD_RC!

if not "!BUILD_RC!"=="0" goto build_bad
echo.
echo       build succeeded.
echo.
exit /b 0

:build_bad
echo.
echo       Build failed. The error is in the lines above, and also in:
echo         %BUILDLOG%
>> "%LOG%" echo       build failed, see %BUILDLOG%
exit /b 1


rem ===========================================================================
:step_package
echo [5/5] Packaging...
>> "%LOG%" echo [5/5] package

set "DLL=%ROOT%\src\SeaPowerAdvanced\bin\Release\SeaPowerAdvanced.dll"
if not exist "%DLL%" goto package_missing

if not exist "%DIST%" mkdir "%DIST%"
copy /y "%DLL%" "%DIST%" >nul
copy /y "%ROOT%\mod\_info.ini" "%DIST%" >nul
echo       package ready: %DIST%
>> "%LOG%" echo       package ready
exit /b 0

:package_missing
echo       The build reported success but produced no DLL at:
echo         %DLL%
>> "%LOG%" echo       dll missing after build
exit /b 1


rem ===========================================================================
:step_install
echo.
echo [install] Copying into the game...
>> "%LOG%" echo [install]

set "TARGET=%GAME%\BepInEx\plugins\SeaPowerAdvanced"

if not exist "%TARGET%" mkdir "%TARGET%" 2>nul
if not exist "%TARGET%" goto install_nofolder

copy /y "%DIST%\SeaPowerAdvanced.dll" "%TARGET%" >nul 2>&1
if not exist "%TARGET%\SeaPowerAdvanced.dll" goto install_denied
copy /y "%DIST%\_info.ini" "%TARGET%" >nul 2>&1
echo        copied to %TARGET%
>> "%LOG%" echo        copied to %TARGET%

set "STALE=%GAME%\Sea Power_Data\StreamingAssets\SeaPowerAdvanced"
if not exist "%STALE%\SeaPowerAdvanced.dll" goto install_done
echo.
echo        NOTE: an older copy is still here:
echo          %STALE%
echo        Delete that folder, or the mod loads twice.
>> "%LOG%" echo        stale copy present: %STALE%

:install_done
echo.
echo        Start Sea Power. There is nothing to tick - BepInEx loads it.
echo        Then right-click one of your ships and open:
echo            Orders - Weapon hatches
exit /b 0

:install_nofolder
echo       Could not create:
echo         %TARGET%
>> "%LOG%" echo       cannot create target folder
echo       BepInEx itself should have been installed by step [3/5]. If it
echo       was skipped, this file is missing:
echo         %GAME%\BepInEx\core\BepInEx.dll
exit /b 1

:install_denied
echo       Could not copy into the game folder - Windows blocked the write.
>> "%LOG%" echo       copy denied
echo.
echo       Right-click build.cmd and choose "Run as administrator", then run
echo       it again. The build itself worked, so the DLL is ready at:
echo         %DIST%\SeaPowerAdvanced.dll
echo       You can also copy that folder to:
echo         %TARGET%
exit /b 1
