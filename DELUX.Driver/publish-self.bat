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
echo  DELUX.Driver 单文件自包含发布
echo  输出目录: %~dp0publish-self
echo ============================================
echo.

REM 单文件自包含发布：WPF 在 .NET 8 单文件模式下可正常加载原生 DLL。
REM 交叉编译需 EnableWindowsTargeting。
dotnet publish DELUX.Driver.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -o "publish-self"

if %errorlevel% equ 0 (
    echo.
    echo [完成] 已发布到: %~dp0publish-self\DELUX.Driver.exe
    echo.
    echo 若覆盖旧版本，请先关闭正在运行的 DELUX.Driver.exe（否则 exe 被进程锁定无法覆盖）。
) else (
    echo.
    echo [失败] 发布出错，请查看上面的输出。
)
pause
