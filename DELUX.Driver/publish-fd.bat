@echo off
chcp 65001 >nul
cd /d "%~dp0"

REM ===== check dotnet =====
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] dotnet not found. Please install .NET SDK first.
    pause
    exit /b 1
)

REM ===== kill running instance if exists (avoid overwrite lock) =====
tasklist /fi "imagename eq DELUX.Driver.exe" | find /i "DELUX.Driver.exe" >nul 2>nul
if %errorlevel% equ 0 (
    echo [INFO] DELUX.Driver.exe is running, killing it before build...
    taskkill /f /im DELUX.Driver.exe >nul 2>nul
    timeout /t 1 >nul
) else (
    echo [INFO] No running DELUX.Driver.exe detected.
)

echo ============================================
echo  DELUX.Driver framework-dependent single-file publish
echo  Recommended: fastest startup (exe ~0.8MB)
echo  Output: %~dp0publish-self-fd
echo  Target machine needs .NET 10 Desktop Runtime (one-time install)
echo ============================================
echo.

REM Framework-dependent single-file: loads from installed .NET runtime,
REM no 150MB bundle scan, no native-lib extraction. Cold start much faster.
dotnet publish DELUX.Driver.csproj -c Release -r win-x64 --self-contained false -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -o "publish-self-fd"

if %errorlevel% equ 0 (
    echo.
    echo [DONE] Published to: %~dp0publish-self-fd\DELUX.Driver.exe
    echo Launching DELUX.Driver.exe...
    start "" "%~dp0publish-self-fd\DELUX.Driver.exe"
) else (
    echo.
    echo [FAILED] Publish error, see output above.
)
pause
