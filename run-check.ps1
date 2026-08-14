# 一键验证 DELUX.Driver 是否能正常启动并显示窗口。
# 用法：在 PowerShell 里执行 .\run-check.ps1，或直接右键"使用 PowerShell 运行"。
$ErrorActionPreference = 'Continue'
$dir = "D:\DELUX_M618XSD\DELUX.Driver\publish-self"
$exe = Join-Path $dir "DELUX.Driver.exe"

Write-Host "=== 清理旧日志 ==="
Remove-Item -Force (Join-Path $dir "crash.log"), (Join-Path $dir "startup.log") -ErrorAction SilentlyContinue

Write-Host "=== 启动 DELUX.Driver.exe（等待 6 秒）==="
$proc = Start-Process -FilePath $exe -PassThru -WorkingDirectory $dir
Start-Sleep -Seconds 6

$alive = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
if ($alive) {
    Write-Host "[OK] 进程仍在运行 (PID $($proc.Id))，窗口应已显示。请查看屏幕。" -ForegroundColor Green
    Write-Host "（如需关闭，结束 DELUX.Driver 进程即可。）"
} else {
    Write-Host "[FAIL] 进程已退出，窗口未显示。" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== crash.log ==="
if (Test-Path (Join-Path $dir "crash.log")) { Get-Content (Join-Path $dir "crash.log") } else { Write-Host "(无)" }
Write-Host "=== startup.log ==="
if (Test-Path (Join-Path $dir "startup.log")) { Get-Content (Join-Path $dir "startup.log") } else { Write-Host "(无)" }

Write-Host ""
Write-Host "按任意键退出..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
