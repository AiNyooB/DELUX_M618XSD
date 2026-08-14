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

echo ============================================
echo  DELUX.Driver self-contained single-file publish
echo  Output: %~dp0publish-self
echo  Target machine needs NO .NET runtime (~150MB)
echo ============================================
echo.

REM Self-contained single-file: bundles runtime, no install needed on target.
REM Cross-compile needs EnableWindowsTargeting.
dotnet publish DELUX.Driver.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -o "publish-self"

if %errorlevel% equ 0 (
    echo.
    echo [DONE] Published to: %~dp0publish-self\DELUX.Driver.exe
    echo.
    echo If overwrite fails, close running DELUX.Driver.exe first.
) else (
    echo.
    echo [FAILED] Publish error, see output above.
)
pause
