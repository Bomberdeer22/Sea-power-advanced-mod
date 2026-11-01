@echo off
rem ---------------------------------------------------------------------------
rem  Build Sea Power Advanced without running any PowerShell script file.
rem
rem  Windows blocks unsigned .ps1 files on many machines ("cannot be loaded ...
rem  is not digitally signed"). Batch files are not covered by that policy, so
rem  this route works when tools\build-portable.ps1 is refused.
rem
rem  Usage:
rem     build.cmd            build into dist\SeaPowerAdvanced
rem     build.cmd install    build, then copy into StreamingAssets
rem
rem  Needs nothing pre-installed: if there is no .NET SDK it fetches a portable
rem  one into .dotnet\ (about 200 MB, once, no installer, no admin rights), and
rem  it detects the game folder from your Steam install.
rem ---------------------------------------------------------------------------
setlocal

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "DOTNET_DIR=%ROOT%\.dotnet"
set "DOTNET_EXE=%DOTNET_DIR%\dotnet.exe"
set "PROJECT=%ROOT%\src\SeaPowerAdvanced\SeaPowerAdvanced.csproj"
set "DIST=%ROOT%\dist\SeaPowerAdvanced"
set "PROPS=%ROOT%\Directory.Build.local.props"

rem ---------------------------------------------------------------- 1. toolchain
if exist "%DOTNET_EXE%" goto have_dotnet

where dotnet >nul 2>&1
if not errorlevel 1 (
    set "DOTNET_EXE=dotnet"
    goto have_dotnet
)

echo No .NET SDK found. Downloading a portable one into .dotnet\ ...
echo This happens once and installs nothing system-wide.
if not exist "%DOTNET_DIR%" mkdir "%DOTNET_DIR%"

rem The installer is fetched as text and run as a script block, never as a file,
rem so the execution policy never sees it.
powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; try { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12 } catch {}; $text=(New-Object Net.WebClient).DownloadString('https://dot.net/v1/dotnet-install.ps1'); & ([scriptblock]::Create($text)) -InstallDir '%DOTNET_DIR%' -Channel 8.0 -NoPath"
if not exist "%DOTNET_EXE%" goto no_sdk

:have_dotnet
echo Using SDK: %DOTNET_EXE%

rem ------------------------------------------------------- 2. game folder paths
if exist "%PROPS%" goto have_config

rem Ask Steam where it lives, then check the default library.
set "STEAM="
for /f "tokens=2,*" %%a in ('reg query "HKCU\Software\Valve\Steam" /v SteamPath 2^>nul ^| findstr /i "SteamPath"') do set "STEAM=%%b"
if not defined STEAM goto default_config

set "STEAM=%STEAM:/=\%"
set "GAME=%STEAM%\steamapps\common\Sea Power"
set "AC=%STEAM%\steamapps\workshop\content\1286220\3380210757"
if not exist "%GAME%\Sea Power_Data\Managed" goto default_config
if not exist "%AC%\AnchorChain.dll" goto almost_config

> "%PROPS%" echo ^<Project^>
>>"%PROPS%" echo   ^<PropertyGroup^>
>>"%PROPS%" echo     ^<SeaPowerDir^>%GAME%^</SeaPowerDir^>
>>"%PROPS%" echo     ^<AnchorChainDir^>%AC%^</AnchorChainDir^>
>>"%PROPS%" echo   ^<^/PropertyGroup^>
>>"%PROPS%" echo ^<^/Project^>
echo Detected Sea Power:  %GAME%
echo Detected AnchorChain: %AC%
goto have_config

:almost_config
echo Found Sea Power at %GAME%
echo but not AnchorChain.dll in the Workshop folder.
goto default_config

:default_config
if not exist "%ROOT%\Directory.Build.local.props" (
    copy /y "%ROOT%\Directory.Build.local.props.example" "%PROPS%" >nul
    echo Created Directory.Build.local.props from the example.
)
echo If your Steam library is not in the default place, edit SeaPowerDir and
echo AnchorChainDir in Directory.Build.local.props, then run this again.

:have_config

rem ------------------------------------------------------------------- 3. build
echo.
echo Building...
"%DOTNET_EXE%" build "%PROJECT%" -c Release
if errorlevel 1 goto build_failed

rem ---------------------------------------------------------------- 4. package
if not exist "%DIST%" mkdir "%DIST%"
copy /y "%ROOT%\src\SeaPowerAdvanced\bin\Release\SeaPowerAdvanced.dll" "%DIST%" >nul
copy /y "%ROOT%\mod\_info.ini" "%DIST%" >nul
if not exist "%DIST%\SeaPowerAdvanced.dll" goto build_failed
echo Package ready: %DIST%

if /I not "%~1"=="install" goto done

rem ----------------------------------------------------------------- 5. install
set "GAME="
for /f "tokens=2 delims=<>" %%p in ('findstr /i "<SeaPowerDir>" "%PROPS%"') do if not defined GAME set "GAME=%%p"

if not defined GAME goto no_game_dir
if not exist "%GAME%\Sea Power_Data\Managed" goto bad_game_dir

set "TARGET=%GAME%\BepInEx\plugins\SeaPowerAdvanced"
if not exist "%TARGET%" mkdir "%TARGET%"
copy /y "%DIST%\SeaPowerAdvanced.dll" "%TARGET%" >nul
copy /y "%DIST%\_info.ini" "%TARGET%" >nul
if not exist "%TARGET%\SeaPowerAdvanced.dll" goto install_failed

echo Installed to: %TARGET%

set "STALE=%GAME%\Sea Power_Data\StreamingAssets\SeaPowerAdvanced"
if exist "%STALE%\SeaPowerAdvanced.dll" (
    echo.
    echo NOTE: an older copy also exists at:
    echo       %STALE%
    echo       Delete that folder so the mod is not loaded twice.
)
echo.
echo Now: start Sea Power. There is nothing to tick - BepInEx loads it directly.
echo Then right-click a ship - Orders - Weapon hatches.
goto done

rem ------------------------------------------------------------------ errors
:no_sdk
echo.
echo Could not download the .NET SDK automatically.
echo Install the .NET SDK 8 from https://dotnet.microsoft.com/download and run
echo build.cmd again - it will find it.
exit /b 1

:build_failed
echo.
echo Build failed. Copy the error lines above and send them on.
exit /b 1

:no_game_dir
echo.
echo Could not read SeaPowerDir from %PROPS%
echo Set it there, for example:
echo     ^<SeaPowerDir^>C:\Program Files (x86)\Steam\steamapps\common\Sea Power^</SeaPowerDir^>
exit /b 1

:bad_game_dir
echo.
echo SeaPowerDir does not look like a Sea Power folder:
echo     %GAME%
echo It must contain "Sea Power_Data\Managed".
exit /b 1

:install_failed
echo.
echo Could not copy into the game folder. If Sea Power is installed under
echo Program Files, close this window, right-click Command Prompt, choose
echo "Run as administrator", and run:  build.cmd install
exit /b 1

:done
if /I "%~1"=="install" goto :eof
echo.
echo To install into the game as well, run:  build.cmd install
endlocal
