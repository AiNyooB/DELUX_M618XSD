@echo off
chcp 65001 >nul
cd /d "%~dp0"

REM ===== 检查 dotnet =====
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [错误] 未找到 dotnet，请先安装 .NET 8 SDK。
    pause
    exit /b 1
)

echo ============================================
echo  MouseDriverClient 自包含单文件发布
echo  输出目录: %~dp0publish-self
echo ============================================
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "publish-self"

if %errorlevel% equ 0 (
    echo.
    echo [完成] 已发布到: %~dp0publish-self\MouseDriverClient.exe
) else (
    echo.
    echo [失败] 发布出错，请查看上面的输出。
)
pause
