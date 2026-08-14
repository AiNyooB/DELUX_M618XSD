## 排查：关闭窗口后进程仍然存在

### 根因分析

`OnClosed` 中调用了 `_vm.Dispose()` 销毁定时器，但 `System.Threading.Timer.Dispose()` **不等待正在执行的回调结束**。两个定时器回调都有风险：

1. **`CheckOfficialDriver`**（每 3 秒触发）：
   - `Process.GetProcesses()` 遍历所有进程，本身可能耗时
   - 第 163 行 `Application.Current.Dispatcher.Invoke()` — 若分发器已开始关闭，**同步 Invoke 会阻塞等待**，永远不会返回

2. **`OnConnectTimeout`** — 同样的 `Dispatcher.Invoke` 问题

即使定时器已 Dispose，如果回调正在执行中，它仍会阻塞在 `Dispatcher.Invoke` 上，导致进程不退出。

### 修复方案

**AppViewModel.cs** — 两处 `Dispatcher.Invoke` 改为 `Dispatcher.BeginInvoke`（异步投递，不阻塞）：
- 第 163 行 `CheckOfficialDriver` 中
- 第 472 行 `OnConnectTimeout` 中

**MainWindow.xaml.cs** — `OnClosed` 末尾添加 `Environment.Exit(0)` 作为安全网。

### 不涉及其他文件