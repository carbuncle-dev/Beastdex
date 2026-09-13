@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

title Beastdex - Build

rem ============================================================
rem Configuration
rem ============================================================
set "PLUGIN_NAME=Beastdex"
set "CONFIG=Release"
if /I "%~1"=="debug" set "CONFIG=Debug"

rem Allow this file to live either beside Beastdex.csproj
rem or one directory above a Beastdex\ folder.
set "PROJECT=%~dp0%PLUGIN_NAME%.csproj"
if not exist "%PROJECT%" set "PROJECT=%~dp0%PLUGIN_NAME%\%PLUGIN_NAME%.csproj"

if not exist "%PROJECT%" (
    echo ERROR: Could not find %PLUGIN_NAME%.csproj.
    echo.
    echo Put build.bat either:
    echo   - beside %PLUGIN_NAME%.csproj, or
    echo   - one directory above the %PLUGIN_NAME% folder.
    echo.
    goto :fail
)

for %%I in ("%PROJECT%") do set "PROJECT_DIR=%%~dpI"

rem ============================================================
rem Find/install .NET 10 SDK
rem ============================================================
call :find_dotnet
call :check_dotnet10

if not defined DOTNET10_FOUND (
    echo .NET 10 SDK was not found.
    echo.

    where winget >nul 2>nul
    if errorlevel 1 (
        echo ERROR: WinGet was not found, so the .NET 10 SDK cannot be installed automatically.
        echo Install the Microsoft .NET 10 SDK manually and run this file again.
        echo.
        goto :fail
    )

    echo Installing Microsoft .NET 10 SDK with WinGet...
    echo Windows may ask for administrator approval.
    echo.

    winget install --exact --id Microsoft.DotNet.SDK.10 --source winget --accept-package-agreements --accept-source-agreements
    set "INSTALL_EXIT=!ERRORLEVEL!"
    if not "!INSTALL_EXIT!"=="0" (
        echo.
        echo ERROR: .NET 10 SDK installation failed with exit code !INSTALL_EXIT!.
        echo.
        goto :fail
    )

    rem The current cmd process may not receive PATH changes made by WinGet,
    rem so probe the normal installation path explicitly after installation.
    call :find_dotnet
    call :check_dotnet10

    if not defined DOTNET10_FOUND (
        echo.
        echo ERROR: .NET 10 was installed, but this process still cannot find it.
        echo Close this window and run build.bat again.
        echo.
        goto :fail
    )
)

rem ============================================================
rem Locate Dalamud development files when using the standard path
rem ============================================================
if not defined DALAMUD_HOME (
    set "DEFAULT_DALAMUD=%APPDATA%\XIVLauncher\addon\Hooks\dev"
    if exist "!DEFAULT_DALAMUD!\Dalamud.dll" (
        set "DALAMUD_HOME=!DEFAULT_DALAMUD!"
        echo Dalamud: !DALAMUD_HOME!
    ) else (
        echo NOTE: Standard Dalamud dev folder was not found at:
        echo   !DEFAULT_DALAMUD!
        echo The Dalamud SDK may still resolve it automatically.
        echo If your installation is elsewhere, set DALAMUD_HOME to the folder
        echo containing Dalamud.dll before running this script.
        echo.
    )
)

rem ============================================================
rem Regression checks (pure .NET, no game process required)
rem ============================================================
if exist "%PROJECT_DIR%Tests\Beastdex.Tests.csproj" (
    echo Running capture, startup, source identity, ranking, target marker and UI policy regression checks...
    "%DOTNET_EXE%" run --project "%PROJECT_DIR%Tests\Beastdex.Tests.csproj" --configuration "%CONFIG%" --no-launch-profile
    set "TEST_EXIT=!ERRORLEVEL!"
    if not "!TEST_EXIT!"=="0" (
        echo ERROR: Regression checks failed with exit code !TEST_EXIT!.
        goto :fail
    )
)

rem ============================================================
rem Clean and build
rem ============================================================
if exist "%PROJECT_DIR%bin" (
    echo Cleaning previous build output...
    rmdir /s /q "%PROJECT_DIR%bin"
    if exist "%PROJECT_DIR%bin" (
        echo ERROR: Old output could not be removed. Unload the dev plugin and retry.
        goto :fail
    )
)

echo.
echo ============================================================
echo Building Beastdex [%CONFIG% / x64]
echo Project: %PROJECT%
echo ============================================================
echo.

"%DOTNET_EXE%" build "%PROJECT%" -c "%CONFIG%" -p:Platform=x64 --nologo
set "BUILD_EXIT=!ERRORLEVEL!"

if not "!BUILD_EXIT!"=="0" (
    echo.
    echo ERROR: Build failed with exit code !BUILD_EXIT!.
    echo.
    goto :fail
)

rem ============================================================
rem Find the DLL produced by the Dalamud SDK.
rem Building a project directly can vary slightly in output layout,
rem so try the normal x64 path first and then search bin recursively.
rem ============================================================
set "DLL_PATH=%PROJECT_DIR%bin\x64\%CONFIG%\%PLUGIN_NAME%.dll"

if not exist "!DLL_PATH!" (
    set "DLL_PATH="
    for /r "%PROJECT_DIR%bin" %%F in (%PLUGIN_NAME%.dll) do (
        if exist "%%~fF" if not defined DLL_PATH set "DLL_PATH=%%~fF"
    )
)

if not defined DLL_PATH (
    echo.
    echo ERROR: dotnet reported success, but %PLUGIN_NAME%.dll could not be found under:
    echo   %PROJECT_DIR%bin
    echo.
    echo Files produced under bin:
    dir /s /b "%PROJECT_DIR%bin" 2>nul
    echo.
    goto :fail
)

rem ============================================================
rem Success
rem ============================================================
echo.
echo ============================================================
echo BUILD COMPLETE
echo ============================================================
echo DLL:
echo   !DLL_PATH!
echo.
echo First-time Dalamud setup:
echo   1. In FFXIV type /xlsettings
echo   2. Open Experimental -^> Dev Plugin Locations
echo   3. Add the DLL path shown above
echo   4. Open /xlplugins -^> Dev Tools -^> Installed Dev Plugins
echo   5. Enable Beastdex
echo   6. Type /bstgrind
echo.
echo For later builds, rebuild here and reload the dev plugin in Dalamud.
echo.
echo Tip: run "build.bat debug" for a Debug build.
echo ============================================================
echo.

pause
exit /b 0

rem ============================================================
rem Helpers
rem ============================================================
:find_dotnet
set "DOTNET_EXE="

if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
    goto :eof
)

for /f "delims=" %%D in ('where dotnet 2^>nul') do (
    if not defined DOTNET_EXE set "DOTNET_EXE=%%D"
)

goto :eof

:check_dotnet10
set "DOTNET10_FOUND="
if not defined DOTNET_EXE goto :eof

for /f "tokens=1 delims=." %%V in ('"%DOTNET_EXE%" --list-sdks 2^>nul') do (
    if "%%V"=="10" set "DOTNET10_FOUND=1"
)

goto :eof

:fail
echo Build did not complete.
echo.
pause
exit /b 1
