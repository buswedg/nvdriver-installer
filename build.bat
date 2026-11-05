@echo off
SETLOCAL EnableDelayedExpansion

pushd "%~dp0"
set "PROJECT_ROOT=%CD%"

echo [*] NVDriver Installer Build System
echo [*] Checking .NET SDK...

where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [!] ERROR: dotnet CLI not found in PATH.
    popd
    exit /b 1
)

:: Handle clean option
if /i "%1"=="--clean" goto do_clean
if /i "%1"=="clean" goto do_clean
goto do_build

:do_clean
echo [*] Cleaning intermediate build artifacts and stale objects...
if exist "release\NVDriverInstaller.exe" del /f /q "release\NVDriverInstaller.exe" >nul 2>&1
dotnet clean src\NVDriverInstaller\NVDriverInstaller.csproj -c Release >nul 2>&1
if exist "src\NVDriverInstaller\bin" rmdir /s /q "src\NVDriverInstaller\bin" >nul 2>&1
if exist "src\NVDriverInstaller\obj" rmdir /s /q "src\NVDriverInstaller\obj" >nul 2>&1
echo [*] Clean complete!

:do_build
:: Release file locks if previously running
taskkill /f /im NVDriverInstaller.exe >nul 2>&1
:: Create output directory and logs
if not exist "release" mkdir "release"
if not exist "release\logs" mkdir "release\logs"

echo [*] Publishing NVDriver Installer (win-x64)...
dotnet publish src\NVDriverInstaller\NVDriverInstaller.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o release

if %ERRORLEVEL% NEQ 0 (
    echo [-] ERROR: Build failed.
    popd
    exit /b 1
)

:: Stage profiles in release\profiles
echo [*] Staging profiles...
if not exist "release\profiles" mkdir "release\profiles"
if not exist "release\profiles\packaging" mkdir "release\profiles\packaging"
if not exist "release\profiles\ddu" mkdir "release\profiles\ddu"

xcopy /y /e "profiles\packaging\*.toml" "release\profiles\packaging\" >nul 2>&1
xcopy /y /e "profiles\ddu\*.toml" "release\profiles\ddu\" >nul 2>&1

:: Stage tools directory
if not exist "release\tools" mkdir "release\tools"
if not exist "release\tools\ddu" mkdir "release\tools\ddu"
if exist "tools\ddu\Display Driver Uninstaller.exe" (
    xcopy /y /e "tools\ddu\*" "release\tools\ddu\" >nul 2>&1
)
if not exist "release\tools\7z" mkdir "release\tools\7z"
if exist "tools\7z\7z.exe" (
    xcopy /y /e "tools\7z\*" "release\tools\7z\" >nul 2>&1
)
:: Stage scripts directory
if not exist "release\scripts" mkdir "release\scripts"
if exist "scripts\resume.cmd" copy /y "scripts\resume.cmd" "release\scripts\" >nul 2>&1
if exist "scripts\resume-workflow.ps1" copy /y "scripts\resume-workflow.ps1" "release\scripts\" >nul 2>&1
if exist "release\resume.cmd" del /f /q "release\resume.cmd" >nul 2>&1
if exist "release\resume-workflow.ps1" del /f /q "release\resume-workflow.ps1" >nul 2>&1

:: Stage packages directory layout (raw and clean subfolders)
if not exist "release\packages" mkdir "release\packages"
if not exist "release\packages\raw" mkdir "release\packages\raw"
if not exist "release\packages\clean" mkdir "release\packages\clean"

echo [+] SUCCESS: Build and Packaging Complete!
echo [+] Binary and release artifacts located at: release\
popd
exit /b 0
