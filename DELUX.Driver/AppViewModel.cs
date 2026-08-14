using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace DeluxDriver;

/// <summary>
/// 全局应用视图模型：连接状态、顶部状态条、导航状态、官方驱动检测、主题切换。
/// </summary>
public class AppViewModel : ObservableObject, IDisposable
{
    private readonly HidComm _hid;
    private readonly System.Threading.Timer _driverTimer;
    private readonly System.Threading.Timer _connectTimer;
    private const int ConnectTimeoutMs = 8000;

    /// <summary>产品外观：White / Black / Blue，默认白色（用户确认）。纯本地设置，不写设备。</summary>
    public enum ProductAppearance { White, Black, Blue }

    /// <summary>导航服务（由 MainWindow 在构造后注入）。</summary>
    public INavigationService? Navigation { get; set; }

    public AppViewModel(HidComm hid)
    {
        _hid = hid;
        ConnectCmd = new RelayCommand(_ => _ = ConnectAsync(), _ => !IsBusy);
        ReconnectCmd = new RelayCommand(_ => _ = ConnectAsync());
        DisconnectCmd = new RelayCommand(_ => Disconnect(), _ => IsConnected && !IsBusy);
        ThemeCmd = new RelayCommand(p => SetTheme(p as string));
        OpenTaskManagerCmd = new RelayCommand(_ => OpenTaskManager());
        SetAppearanceCmd = new RelayCommand(p => SetAppearance(p as string));

        // 周期检测官方 Mouse.exe 是否运行（AGENTS.md 前置条件：必须退出官方驱动）。
        _driverTimer = new System.Threading.Timer(_ => CheckOfficialDriver(), null, 0, 3000);
        // 自动连接超时计时器：超时未连上显示「重新识别」。
        _connectTimer = new System.Threading.Timer(_ => OnConnectTimeout(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

        // 加载产品外观本地设置（默认白色，用户确认）。
        LoadAppearance();
    }

    #region 产品外观（本地持久化，不写设备）

    /// <summary>当前产品外观；影响全站鼠标示意图配色（原型 §1.2 / §2.2 / §9.3）。</summary>
    private ProductAppearance _appearance = ProductAppearance.White;
    public ProductAppearance Appearance
    {
        get => _appearance;
        set
        {
            if (SetProperty(ref _appearance, value))
            {
                OnPropertyChanged(nameof(AppearanceImageUri));
                SaveAppearance();
            }
        }
    }

    /// <summary>当前外观对应的产品图资源路径（Pack URI，指向嵌入的 Assets 图片）。</summary>
    public string AppearanceImageUri
        => _appearance switch
        {
            ProductAppearance.Black => "pack://application:,,,/Assets/m618xsd_black_top.png",
            ProductAppearance.Blue => "pack://application:,,,/Assets/m618xsd_blue_top.png",
            _ => "pack://application:,,,/Assets/m618xsd_white_top.png",
        };

    private static string AppearanceConfigPath()
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DELUX.Driver", "appearance.json");

    private void LoadAppearance()
    {
        try
        {
            var path = AppearanceConfigPath();
            if (System.IO.File.Exists(path))
            {
                var txt = System.IO.File.ReadAllText(path).Trim().ToLowerInvariant();
                _appearance = txt switch
                {
                    "black" => ProductAppearance.Black,
                    "blue" => ProductAppearance.Blue,
                    _ => ProductAppearance.White,
                };
            }
        }
        catch { /* 读失败用默认白色 */ }
    }

    private void SaveAppearance()
    {
        try
        {
            var path = AppearanceConfigPath();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var val = _appearance switch
            {
                ProductAppearance.Black => "black",
                ProductAppearance.Blue => "blue",
                _ => "white",
            };
            System.IO.File.WriteAllText(path, val);
        }
        catch { /* 写失败忽略，不阻断 UI */ }
    }

    /// <summary>由 UI 调用：按字符串设置外观（white/black/blue），默认白色。</summary>
    private void SetAppearance(string? key)
    {
        var next = key?.ToLowerInvariant() switch
        {
            "black" => ProductAppearance.Black,
            "blue" => ProductAppearance.Blue,
            _ => ProductAppearance.White,
        };
        Appearance = next;
    }

    #endregion

    #region 官方驱动检测

    /// <summary>是否检测到官方 Mouse.exe 正在运行（用于顶部黄色警告条 + 拦截发送）。</summary>
    private bool _officialDriverRunning;
    public bool OfficialDriverRunning
    {
        get => _officialDriverRunning;
        set => SetProperty(ref _officialDriverRunning, value);
    }

    /// <summary>
    /// 会与本软件争用 HID 设备的官方驱动进程名（不含 .exe 后缀，ProcessName 本就不带扩展名）。
    /// 注意：绝不可包含 "Delux" / "DELUX.Driver" 等本软件自身进程名，否则会把自己误判为官方驱动。
    /// </summary>
    private static readonly string[] OfficialDriverProcessNames = { "Mouse" };

    private void CheckOfficialDriver()
    {
        bool running = false;
        int selfPid = Environment.ProcessId;
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                if (p.Id == selfPid) continue; // 排除自身进程，避免误报
                foreach (var name in OfficialDriverProcessNames)
                {
                    if (p.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        running = true;
                        break;
                    }
                }
                if (running) break;
            }
        }
        catch { /* 权限不足时忽略，不阻断 */ }

        if (running != OfficialDriverRunning)
            Application.Current.Dispatcher.BeginInvoke(() => OfficialDriverRunning = running);
    }

    #endregion

    #region 主题切换

    private bool _isDark;
    public bool IsDark
    {
        get => _isDark;
        set => SetProperty(ref _isDark, value);
    }

    public ICommand ThemeCmd { get; }

    private void SetTheme(string? mode)
    {
        bool dark = string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase);
        if (dark == IsDark) return;
        IsDark = dark;
        App.ApplyTheme(IsDark);
    }

    /// <summary>一键打开任务管理器（原型 §1.3：官方驱动运行时给「打开任务管理器」引导）。</summary>
    private void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog($"打开任务管理器失败：{ex.Message}");
        }
    }

    #endregion

    #region 连接状态

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
                OnPropertyChanged(nameof(ShowDefaultTitle));
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(ShowDefaultTitle));
        }
    }

    /// <summary>默认空态标题「请连接设备」可见：未连接、未忙碌、无错误。</summary>
    public bool ShowDefaultTitle => !IsBusy && !IsConnected && !ConnectErrorVisible;

    /// <summary>状态条颜色：绿=已连接 / 灰=未连接 / 黄=连接中。</summary>
    private string _statusColor = "#888888";
    public string StatusColor
    {
        get => _statusColor;
        set => SetProperty(ref _statusColor, value);
    }

    private string _statusText = "未连接";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _connectButtonText = "连接设备";
    public string ConnectButtonText
    {
        get => _connectButtonText;
        set => SetProperty(ref _connectButtonText, value);
    }

    private string _deviceName = "未连接";
    public string DeviceName
    {
        get => _deviceName;
        set => SetProperty(ref _deviceName, value);
    }

    private string _batteryText = "—";
    public string BatteryText
    {
        get => _batteryText;
        set => SetProperty(ref _batteryText, value);
    }

    private string _logText = "";
    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public ICommand ConnectCmd { get; }
    public ICommand ReconnectCmd { get; }
    public ICommand DisconnectCmd { get; }
    public ICommand OpenTaskManagerCmd { get; }
    public ICommand SetAppearanceCmd { get; }

    /// <summary>自动连接超时后显示「重新识别」按钮（用户可手动重试）。</summary>
    private bool _showReconnect;
    public bool ShowReconnect
    {
        get => _showReconnect;
        set => SetProperty(ref _showReconnect, value);
    }

    #region 连接失败原因（原型 §1.3：区分场景给可执行文案）

    private bool _connectErrorVisible;
    public bool ConnectErrorVisible
    {
        get => _connectErrorVisible;
        set
        {
            if (SetProperty(ref _connectErrorVisible, value))
                OnPropertyChanged(nameof(ShowDefaultTitle));
        }
    }

    private string _connectErrorText = "";
    public string ConnectErrorText
    {
        get => _connectErrorText;
        set => SetProperty(ref _connectErrorText, value);
    }

    private bool _showOpenTaskManager;
    public bool ShowOpenTaskManager
    {
        get => _showOpenTaskManager;
        set => SetProperty(ref _showOpenTaskManager, value);
    }

    /// <summary>
    /// 根据 HID 返回的错误信息区分失败场景，给出可执行文案（原型 §1.3）。
    /// 协议术语不外显；统一动词「连接设备」。
    /// </summary>
    private void SetConnectErrorByReason(string? reason)
    {
        ConnectErrorVisible = true;
        ShowOpenTaskManager = false;
        ShowReconnect = true;

        var msg = (reason ?? "").ToLowerInvariant();
        if (msg.Contains("占用") || msg.Contains("access") || msg.Contains("used") || msg.Contains("being used"))
            ConnectErrorText = "鼠标正被其他程序占用，请关闭官方驱动后重试。";
        else if (msg.Contains("未找到") || msg.Contains("no device") || msg.Contains("not found") || msg.Contains("enumerate"))
            ConnectErrorText = "未找到鼠标：请插入 2.4G 接收器或数据线后重试。";
        else
            ConnectErrorText = $"连接失败，请确认接收器已插入、官方驱动已关闭后重试。";
    }

    private void AppendLog(string line)
    {
        LogText = $"[{DateTime.Now:HH:mm:ss}] {line}\n" + LogText;
    }

    private void Disconnect()
    {
        _hid.StopInputListener();
        _hid.Dispose();
        IsConnected = false;
        StatusColor = "#888888";
        StatusText = "未连接";
        ConnectButtonText = "连接设备";
        DeviceName = "未连接";
        BatteryText = "—";
        ConnectErrorVisible = false;
        ShowReconnect = false;
        ShowOpenTaskManager = false;
        Navigation?.Navigate("Connect");
        AppendLog("已断开连接。");
    }

    /// <summary>
    /// 软件启动后即自动尝试识别鼠标（无需用户点击）。连接中显示进度环；
    /// 若超过 ConnectTimeoutMs 仍未连上，停止并显示「重新识别」按钮。
    /// </summary>
    public void AutoConnect()
    {
        _ = ConnectAsync();
    }

    private async System.Threading.Tasks.Task ConnectAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ShowReconnect = false;
        ConnectErrorVisible = false;
        ShowOpenTaskManager = false;
        StatusColor = "#E0A000";
        StatusText = "正在识别鼠标…";
        ConnectButtonText = "正在识别…";
        AppendLog("正在识别鼠标…");

        // 启动超时计时：超过阈值仍未连上 → 提示重新识别。
        _connectTimer.Change(ConnectTimeoutMs, System.Threading.Timeout.Infinite);

        try
        {
            // 重新连接前先停止上一次的监听线程（若有残留）。
            _hid.StopInputListener();

            // 前置条件（AGENTS.md）：发任何配置命令前必须退出官方 Mouse.exe。
            if (OfficialDriverRunning)
            {
                ConnectErrorVisible = true;
                ShowOpenTaskManager = true;
                ShowReconnect = false;
                ConnectErrorText = "检测到官方驱动运行中，为避免冲突请先关闭后再连接。";
                AppendLog("已阻止连接：检测到官方驱动 Mouse.exe 正在运行，请先完全退出后再试。");
                return;
            }

            // HID 枚举/打开/打开数据接口均为同步 P/Invoke，整体丢到线程池避免阻塞 UI 线程。
            bool ok = await System.Threading.Tasks.Task.Run(() =>
            {
                if (!_hid.Connect()) return false;

                // Connect() 成功仅代表「系统枚举到接收器」，不代表鼠标真正开机在线。
                // 真正的在线判据：打开数据接口后能否收到鼠标的 Input Report（主动上报）。
                _hid.ResetInputSignal();
                bool dataOpened = _hid.OpenDataInterface();
                if (dataOpened)
                {
                    _hid.BatteryChanged = (chargeState, percent) =>
                    {
                        string state = chargeState switch
                        {
                            2 => "充电中 ",
                            3 => "已充满 ",
                            _ => "",
                        };
                        BatteryText = $"{state}{percent}%";
                    };
                    _hid.StartInputListener();
                }

                // 等待最多 4 秒看是否收到 Input 信号。
                bool alive = false;
                for (int i = 0; i < 40; i++)
                {
                    if (_hid.HasInputSignal) { alive = true; break; }
                    System.Threading.Thread.Sleep(100);
                }
                return alive;
            });

            if (ok)
            {
                IsConnected = true;
                ConnectErrorVisible = false;
                ShowOpenTaskManager = false;
                ShowReconnect = false;
                StatusColor = "#1A9E3E";
                StatusText = "已连接";
                ConnectButtonText = "断开连接";
                DeviceName = "DELUX M618XSD";
                AppendLog($"连接成功：{_hid.DevicePath}（已收到鼠标 Input 上报）");

                // 识别成功自动进入配置页面（原型 §1.3）。
                Navigation?.Navigate("Buttons");
            }
            else
            {
                // 接收器在但鼠标没开 / 未枚举到 → 视为未连上，提示重新识别。
                StatusColor = "#888888";
                StatusText = "未连接";
                ConnectButtonText = "连接设备";
                DeviceName = "未连接";
                ShowReconnect = true;
                ConnectErrorVisible = true;
                ShowOpenTaskManager = false;
                ConnectErrorText = _hid.IsConnected
                    ? "已检测到接收器，但鼠标未开机或无响应，请打开鼠标后点击重新识别。"
                    : $"连接失败，请确认接收器已插入、官方驱动已关闭后重试。";
                AppendLog(_hid.IsConnected
                    ? "接收器已枚举，但未收到鼠标 Input 上报：鼠标可能未开机。"
                    : $"连接失败：{_hid.LastErrorMessage}");
            }
        }
        finally
        {
            // 无论成功失败都取消超时计时，并复位忙状态。
            _connectTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            IsBusy = false;
        }
    }

    /// <summary>连接超时回调：未连上 → 停止进度环提示，显示「重新识别」。</summary>
    private void OnConnectTimeout()
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            // 已连上或正在重试则忽略。
            if (IsConnected || IsBusy) return;
            ShowReconnect = true;
            ConnectErrorVisible = true;
            ShowOpenTaskManager = false;
            ConnectErrorText = "未能自动识别到鼠标，请确认接收器已插入或数据线已连接后点击重新识别。";
            StatusColor = "#888888";
            StatusText = "未识别到鼠标";
            ConnectButtonText = "连接设备";
        });
    }

    #endregion

    #endregion

    #region 导航状态

    private string _currentPageKey = "Connect";
    public string CurrentPageKey
    {
        get => _currentPageKey;
        set => SetProperty(ref _currentPageKey, value);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _driverTimer.Dispose();
        _connectTimer.Dispose();
    }

    #endregion
}
