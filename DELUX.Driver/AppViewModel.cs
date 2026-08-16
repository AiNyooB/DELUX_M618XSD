using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
    private const int MaxAutoConnectAttempts = 3;
    private const int RetryDelayMs = 2000;
    private int _autoConnectAttempts;
    private bool _pendingRetry;

    /// <summary>产品外观：White / Black / Blue，默认白色（用户确认）。纯本地设置，不写设备。</summary>
    public enum ProductAppearance { White, Black, Blue }

    /// <summary>导航服务（由 MainWindow 在构造后注入）。</summary>
    public INavigationService? Navigation { get; set; }

    public AppViewModel(HidComm hid)
    {
        _hid = hid;
        ConnectCmd = new RelayCommand(_ => _ = ConnectAsync(), _ => !IsBusy);
        ReconnectCmd = new RelayCommand(_ => { _autoConnectAttempts = 0; _ = ConnectAsync(); });
        DisconnectCmd = new RelayCommand(_ => Disconnect(), _ => IsConnected && !IsBusy);
        SetAppearanceCmd = new RelayCommand(p => SetAppearance(p as string));
        SwitchLevelCmd = new RelayCommand(p => SwitchLevel(System.Convert.ToInt32(p)), _ => IsConnected && !IsBusy);
        SelectButtonCmd = new RelayCommand(p => SelectButton(System.Convert.ToInt32(p)));
        SetFuncCmd = new RelayCommand(p => SetButtonFunction(System.Convert.ToByte(p)));
        SetMacroCmd = new RelayCommand(p => SelectedMacroId = System.Convert.ToInt32(p));
        InitDpi();
        InitButtons();
        InitMacros();
        InitIconMarkers();

        // 周期检测官方 Mouse.exe 是否运行（AGENTS.md 前置条件：必须退出官方驱动）。
        _driverTimer = new System.Threading.Timer(_ => CheckOfficialDriver(), null, 0, 3000);
        // 自动连接超时计时器：超时未连上显示「重新识别」。
        _connectTimer = new System.Threading.Timer(_ => OnConnectTimeout(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

        // 加载产品外观本地设置（默认白色，用户确认）。
        LoadAppearance();
        // 加载主题：优先持久化的用户选择，无记录则跟随系统（AGENTS.md §3.5 默认跟随系统）。
        LoadTheme();
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

    /// <summary>主题模式：跟随设备（系统）/ 浅色 / 深色。</summary>
    public enum ThemeModeKind { System, Light, Dark }

    private ThemeModeKind _themeMode = ThemeModeKind.System;
    /// <summary>用户选择的主题模式；setter 解析为实际明暗并应用 + 持久化。</summary>
    public ThemeModeKind ThemeMode
    {
        get => _themeMode;
        set
        {
            if (!SetProperty(ref _themeMode, value)) return;
            ApplyThemeMode();
        }
    }

    private bool _isDark;
    /// <summary>当前实际明暗（由 ThemeMode 解析；MainWindow 据此同步 DWM 材质）。</summary>
    public bool IsDark
    {
        get => _isDark;
        set => SetProperty(ref _isDark, value);
    }

    /// <summary>解析当前 ThemeMode 为实际明暗，应用资源字典 + 通知 DWM + 持久化。</summary>
    private void ApplyThemeMode()
    {
        bool dark = ResolveDark(_themeMode);
        if (dark != _isDark)
        {
            _isDark = dark;
            App.ApplyTheme(dark);
            OnPropertyChanged(nameof(IsDark)); // 触发 MainWindow 更新 DWM 材质明暗
        }
        SaveTheme();
    }

    private static bool ResolveDark(ThemeModeKind mode) => mode switch
    {
        ThemeModeKind.Dark => true,
        ThemeModeKind.Light => false,
        _ => IsSystemDark(), // System：跟随设备/系统明暗
    };

    private static string ThemeConfigPath()
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DELUX.Driver", "theme");

    /// <summary>
    /// 启动期加载主题模式：优先持久化记录，无记录默认跟随设备（AGENTS.md §3.5 默认跟随系统）。
    /// 构造期无订阅者，直接赋字段避免多余通知（同 LoadAppearance 写 _appearance）。
    /// 实际资源字典由 App.OnStartup 据 IsDark 统一应用；兼容旧 light/dark 记录。
    /// </summary>
    private void LoadTheme()
    {
        ThemeModeKind mode;
        try
        {
            var path = ThemeConfigPath();
            mode = System.IO.File.Exists(path)
                ? System.IO.File.ReadAllText(path).Trim().ToLowerInvariant() switch
                {
                    "light" => ThemeModeKind.Light,
                    "dark" => ThemeModeKind.Dark,
                    _ => ThemeModeKind.System,
                }
                : ThemeModeKind.System;
        }
        catch
        {
            mode = ThemeModeKind.System;
        }
        _themeMode = mode;
        _isDark = ResolveDark(mode);
    }

    private void SaveTheme()
    {
        try
        {
            var path = ThemeConfigPath();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, _themeMode switch
            {
                ThemeModeKind.Light => "light",
                ThemeModeKind.Dark => "dark",
                _ => "system",
            });
        }
        catch { /* 写失败忽略，不阻断 UI */ }
    }

    /// <summary>读取系统明暗偏好（注册表 AppsUseLightTheme：0=深色）。读失败按浅色。</summary>
    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    #endregion

    #region DPI 配置（0x04，5 档卡片）

    /// <summary>
    /// UI 展示用 DPI 档位行。仅 5 档暴露（M618XSD 本机 5 档：800/1200/1600/2400/4000，
    /// 官方软件 6-8 档隐藏；协议槽位 6-8 固定 0，见 DpiConfig 注释）。
    /// </summary>
    public class DpiLevelItem : ObservableObject
    {
        public int Index { get; init; }
        public string Label => $"档位 {Index}";

        /// <summary>档位指示灯色（模拟硬件 LED，固定不随主题）：红/绿/蓝/紫/黄 = DPI 1..5，
        /// 与鼠标 DPI 键灯色一一对应（M618XSD驱动功能Wiki.md 3.2 节）。</summary>
        public Brush IndicatorBrush { get; init; } = Brushes.Gray;

        private string _value = "800";
        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value)) OnPropertyChanged(nameof(HasError));
            }
        }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        private bool _isRemembered;
        /// <summary>本次启动从本地记忆恢复的档位（设备不支持主动读档位，见 AGENTSK 2.4）；任何切档后清除。</summary>
        public bool IsRemembered
        {
            get => _isRemembered;
            set => SetProperty(ref _isRemembered, value);
        }

        /// <summary>数值校验失败（40-4800 且为 40 的倍数）；有错时卡片红框提示且不写入。</summary>
        public bool HasError => !TryParse(out _);

        public bool TryParse(out int v)
            => int.TryParse(_value.Trim(), out v) && v >= 40 && v <= 4800 && v % 40 == 0;

        /// <summary>
        /// 滑块绑定值（UI 辅助属性，不参与协议/校验逻辑）：
        /// get = 最近合法值（clamp 40-4800 + 取整 40 倍数，非法输入时仍可展示）；
        /// set = 取整后写回 Value（触发既有防抖保存）。
        /// </summary>
        public int SliderValue
        {
            get => int.TryParse(_value.Trim(), out int v) ? ClampToStep(v) : 40;
            set => Value = ClampToStep(value).ToString();
        }

        private static int ClampToStep(int v)
        {
            v = Math.Clamp(v, 40, 4800);
            return (int)(Math.Round(v / 40.0) * 40);
        }
    }

    private readonly ObservableCollection<DpiLevelItem> _dpiLevels = new();
    /// <summary>5 档卡片数据源（ItemsControl 绑定）。</summary>
    public ObservableCollection<DpiLevelItem> DpiLevels => _dpiLevels;

    /// <summary>协议层 DPI 配置（8 槽；槽位 6-8 固定 0，仅前 5 档参与 UI）。</summary>
    public DpiConfig Dpi { get; } = new DpiConfig();

    private int _activeLevel = 1;
    /// <summary>当前活跃档位（1..5）。硬件切档（DpiLevelChanged）与程序切档共用，驱动卡片高亮。</summary>
    public int ActiveLevel
    {
        get => _activeLevel;
        set
        {
            if (SetProperty(ref _activeLevel, value))
            {
                OnPropertyChanged(nameof(CurrentDpiText));
                for (int i = 0; i < _dpiLevels.Count; i++)
                {
                    _dpiLevels[i].IsActive = (i + 1 == value);
                    _dpiLevels[i].IsRemembered = false; // 任何切档（软件/硬件）后清除「上次使用」徽章
                }
            }
        }
    }

    /// <summary>底部常驻指示：当前档位 + 该档 DPI 值（非法值显示 0）。</summary>
    public string CurrentDpiText
    {
        get
        {
            int v = 0;
            if (_activeLevel >= 1 && _activeLevel <= _dpiLevels.Count)
                _dpiLevels[_activeLevel - 1].TryParse(out v);
            return $"当前档位：第 {_activeLevel} 档 · {v} DPI";
        }
    }

    /// <summary>「设为当前」切档命令（参数 = 档位号 1..5）。</summary>
    public RelayCommand SwitchLevelCmd { get; }

    // ---- 自动保存防抖（停止操作约 1.5s 写入；全站页面骨架，见 AGENTS.md 3.1）----
    private System.Threading.Timer? _saveDebounce;
    private const int AutoSaveDelayMs = 1500;

    private string _saveStatusText = "";
    /// <summary>保存状态：待保存… / 正在保存… / 已保存 ✓ / 数值无效，未保存。</summary>
    public string SaveStatusText
    {
        get => _saveStatusText;
        set => SetProperty(ref _saveStatusText, value);
    }

    private string? _toastMessage;
    /// <summary>轻量 Toast 文案（MainWindow 右下角浮层显示，2.5s 自动消失）。</summary>
    public string? ToastMessage
    {
        get => _toastMessage;
        set
        {
            if (SetProperty(ref _toastMessage, value)) OnPropertyChanged(nameof(ToastVisible));
        }
    }
    public bool ToastVisible => !string.IsNullOrEmpty(_toastMessage);

    private DispatcherTimer? _toastTimer;

    private void InitDpi()
    {
        // 官方软件默认 5 档（800/1200/1600/2400/4000），仅前 5 档启用；
        // 指示灯色序红/绿/蓝/紫/黄 = DPI 1..5（与鼠标 DPI 键灯色一致）。
        int[] defaults = { 800, 1200, 1600, 2400, 4000 };
        Color[] colors =
        {
            Color.FromRgb(0xE8, 0x11, 0x23), // 红
            Color.FromRgb(0x1A, 0x9E, 0x3E), // 绿
            Color.FromRgb(0x00, 0x67, 0xC0), // 蓝
            Color.FromRgb(0x8A, 0x4B, 0xD8), // 紫
            Color.FromRgb(0xE0, 0xA0, 0x00), // 黄
        };
        for (int i = 0; i < defaults.Length; i++)
        {
            var item = new DpiLevelItem
            {
                Index = i + 1,
                Value = defaults[i].ToString(),
                Enabled = true,
                IndicatorBrush = new SolidColorBrush(colors[i]),
            };
            item.PropertyChanged += OnDpiItemChanged;
            _dpiLevels.Add(item);
        }
        // 设备不支持主动读当前档位（AGENTSK 2.4），启动时从本地记忆恢复上次档位：
        // 有记忆 → 选中记忆档 + 显示「上次使用」徽章；无记忆 → 第一档。
        int last = LoadLastLevel();
        int startLevel = (last >= 1 && last <= 5) ? last : 1;
        _activeLevel = startLevel; // 直接赋字段不走 setter（避免误触发保存/清徽章）
        _dpiLevels[startLevel - 1].IsActive = true;
        if (last >= 1 && last <= 5)
            _dpiLevels[startLevel - 1].IsRemembered = true;

        _saveDebounce = new System.Threading.Timer(_ => Application.Current.Dispatcher.BeginInvoke(SaveDpi),
            null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        // 硬件切档上报同步（Input Report buf[3]=档位，AGENTSK 2.4）：只同步高亮，不写设备，并更新记忆。
        _hid.DpiLevelChanged = level => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (level >= 1 && level <= 5)
            {
                ActiveLevel = level;
                SaveLastLevel(level);
            }
        });
    }

    /// <summary>任一档数值/启用勾选变化 → 待保存 + 重置防抖计时。</summary>
    private void OnDpiItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(DpiLevelItem.Value) or nameof(DpiLevelItem.Enabled))) return;
        SaveStatusText = "待保存…";
        _saveDebounce?.Change(AutoSaveDelayMs, System.Threading.Timeout.Infinite);
    }

    /// <summary>防抖到期后的自动保存：校验 → 全量写 0x04（0x0C 唤醒 + 整报告，含校验和）。</summary>
    public void SaveDpi()
    {
        if (!IsConnected) { SaveStatusText = ""; return; }
        if (OfficialDriverRunning)
        {
            SaveStatusText = "";
            ShowToast("检测到官方驱动运行中，写入已拦截。请完全退出 Mouse.exe 后重试。");
            return;
        }
        for (int i = 0; i < _dpiLevels.Count; i++)
        {
            if (!_dpiLevels[i].TryParse(out _))
            {
                SaveStatusText = "数值无效，未保存";
                ShowToast($"第 {i + 1} 档数值无效：请输入 40-4800 且为 40 的倍数。");
                return;
            }
        }
        SaveStatusText = "正在保存…";
        SyncDpiConfig();
        if (_hid.Wake() && _hid.WriteFeature(Dpi.ToBytes()))
        {
            SaveStatusText = "已保存 ✓";
            ShowToast("已保存，鼠标 OLED 已切换");
        }
        else
        {
            SaveStatusText = "保存失败";
            ShowToast($"保存失败：{_hid.LastErrorMessage}。请确认已退出官方驱动后重试。");
        }
    }

    /// <summary>「设为当前」：先按当前编辑值全量写 0x04（含新档位），再同步高亮。
    /// ⚠️ 不用"只改 [24]"的轻量写法——未保存过编辑时本地无有效基准报告，
    /// 克隆空报告只写 [24] 会把 DPI 值槽清空（0x08 整表覆写同类事故，AGENTSK 2.3b）。</summary>
    public void SwitchLevel(int level)
    {
        if (level < 1 || level > 5 || !IsConnected) return;
        if (OfficialDriverRunning)
        {
            ShowToast("检测到官方驱动运行中，已取消切档。请完全退出 Mouse.exe 后重试。");
            return;
        }
        // 与 SaveDpi 相同的校验：任一档非法则不写（否则 SyncDpiConfig 会把非法档位当 0 写入设备）。
        for (int i = 0; i < _dpiLevels.Count; i++)
        {
            if (!_dpiLevels[i].TryParse(out _))
            {
                ShowToast($"第 {i + 1} 档数值无效：请输入 40-4800 且为 40 的倍数。");
                return;
            }
        }
        SyncDpiConfig();
        Dpi.ActiveLevel = (byte)level;
        if (_hid.Wake() && _hid.WriteFeature(Dpi.ToBytes()))
        {
            ActiveLevel = level;
            SaveLastLevel(level);
            SaveStatusText = "已保存 ✓";
            ShowToast($"已切换到第 {level} 档");
        }
        else
        {
            ShowToast($"切档失败：{_hid.LastErrorMessage}");
        }
    }

    /// <summary>把 5 档卡片当前编辑同步进协议层 DpiConfig（槽位 6-8 固定 0）。</summary>
    private void SyncDpiConfig()
    {
        byte bitmap = 0;
        for (int i = 0; i < 5 && i < _dpiLevels.Count; i++)
        {
            _dpiLevels[i].TryParse(out int v);
            Dpi.Levels[i] = v;
            if (_dpiLevels[i].Enabled) bitmap |= (byte)(1 << i);
        }
        for (int i = 5; i < 8; i++) Dpi.Levels[i] = 0; // 槽位 6/7/8 固定 0，本机不启用
        Dpi.EnabledBitmap = bitmap;
        if (_activeLevel >= 1 && _activeLevel <= 5) Dpi.ActiveLevel = (byte)_activeLevel;
    }

    private static string DpiLevelConfigPath()
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DELUX.Driver", "dpi-level.json");

    /// <summary>读取本地记忆的上次档位（1..5）；无记忆/损坏返回 -1。</summary>
    private int LoadLastLevel()
    {
        try
        {
            var path = DpiLevelConfigPath();
            if (System.IO.File.Exists(path) && int.TryParse(System.IO.File.ReadAllText(path).Trim(), out int v))
                return v;
        }
        catch { /* 读失败按无记忆处理 */ }
        return -1;
    }

    /// <summary>持久化上次档位（设备不支持主动读档位，靠本地记忆恢复，见 AGENTSK 2.4）。</summary>
    private void SaveLastLevel(int level)
    {
        try
        {
            var path = DpiLevelConfigPath();
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(path, level.ToString());
        }
        catch { /* 写失败不影响使用 */ }
    }

    public void ShowToast(string msg)
    {
        ToastMessage = msg;
        _toastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _toastTimer.Tick -= OnToastTick; // 先减后加，防重复叠加
        _toastTimer.Tick += OnToastTick;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnToastTick(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        ToastMessage = null;
    }

    #endregion

    #region 按键映射（0x08，整表覆写）

    /// <summary>UI 展示的物理按钮（10 个可编程键 → 协议 entry 索引）。</summary>
    public class ButtonItem : ObservableObject
    {
        public int Index { get; init; }
        public string Name { get; init; } = "";
        /// <summary>协议 entry 索引（0..17，见 HID协议逆向报告.md 3.5 UI→entry 映射）。</summary>
        public int EntryIndex { get; init; }
        /// <summary>关键键（左键/右键/中键）：修改需风险确认（原型 §2.4）。</summary>
        public bool IsPrimary { get; init; }
        /// <summary>标签在鼠标图（300×506 坐标系，已裁透明区）上的位置，Canvas.Left/Top 绑定。
        /// 支持在页面上直接拖动定位（调试用，拖动后写入 button-tags.json）。</summary>
        private double _tagX;
        public double TagX { get => _tagX; set => SetProperty(ref _tagX, value); }
        private double _tagY;
        public double TagY { get => _tagY; set => SetProperty(ref _tagY, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        private string _functionName = "";
        /// <summary>当前功能名（标签只显示这一行；按钮名通过 ToolTip 区分）。</summary>
        public string FunctionName { get => _functionName; set => SetProperty(ref _functionName, value); }
        /// <summary>匹配键，与 IconMarker.IconKey 对应，用于 LinkIconsToButtons 关联编号。</summary>
        public string IconKey { get; init; } = "";
        /// <summary>与该按钮对应的图标编号（1..10），显示在标签右上角，与图标上编号一致。</summary>
        public int IconIndex { get; set; }
    }

    /// <summary>鼠标图上的位置指示图标（线性：圆 = 按键位、箭头 = 滚轮方向）。
    /// 坐标在 300×506 鼠标图坐标系，固定默认值。</summary>
    public class IconMarker : ObservableObject
    {
        public enum MarkerKind { Circle, ArrowUp, ArrowDown, ArrowLeft, ArrowRight }
        public string Name { get; init; } = "";
        public MarkerKind Kind { get; init; }
        /// <summary>对应关系编号（1..10），与按键标签右上角的编号一致，建立一一对应。</summary>
        public int Index { get; set; }
        /// <summary>匹配键：把「中键」图标与「滚轮」按钮这类同物理键、异命名的项关联起来。</summary>
        public string IconKey { get; init; } = "";
        private double _x;
        public double X { get => _x; set => SetProperty(ref _x, value); }
        private double _y;
        public double Y { get => _y; set => SetProperty(ref _y, value); }
        /// <summary>箭头方向对应的线性 Path.Data（14×14 viewBox，无填充）。</summary>
        public string PathData => Kind switch
        {
            MarkerKind.ArrowUp    => "M 3 8 L 7 2 L 11 8 M 7 8 L 7 12",
            MarkerKind.ArrowDown  => "M 3 6 L 7 12 L 11 6 M 7 6 L 7 2",
            MarkerKind.ArrowLeft  => "M 8 3 L 2 7 L 8 11 M 8 7 L 12 7",
            MarkerKind.ArrowRight => "M 6 3 L 12 7 L 6 11 M 6 7 L 2 7",
            _ => string.Empty,
        };
    }

    /// <summary>已验证功能码表（12 个，HID协议逆向报告.md 3.5 节）；未逆向功能绝不进 UI。</summary>
    public static class ButtonFunc
    {
        public record Func(byte Code, string Name, string Group);
        public static readonly Func[] All =
        {
            new(0x01, "标准（默认）", "鼠标操作"),
            new(0x02, "左键", "鼠标操作"),
            new(0x03, "右键", "鼠标操作"),
            new(0x04, "中键", "鼠标操作"),
            new(0x05, "后退", "鼠标操作"),
            new(0x06, "前进", "鼠标操作"),
            new(0x09, "上滚", "鼠标操作"),
            new(0x0A, "下滚", "鼠标操作"),
            new(0x0B, "左滚", "鼠标操作"),
            new(0x0C, "右滚", "鼠标操作"),
            new(0x0D, "DPI 循环", "DPI 功能"),
            new(0x12, "宏", "宏"),
        };

        public static string NameOf(byte code)
            => System.Linq.Enumerable.FirstOrDefault(All, f => f.Code == code)?.Name ?? $"未知 (0x{code:X2})";
    }

    /// <summary>「鼠标操作」Tab 的功能选项（按选中按钮重建；宏单列 Tab，不在此列表）。</summary>
    public class FuncOption : ObservableObject
    {
        public byte Code { get; init; }
        public string Name { get; init; } = "";

        private bool _isChecked;
        public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
    }

    /// <summary>宏列表项（宏 Tab 单选；Phase 5 接入真实宏数据后动态变化，列表为空时宏 Tab 显示空态）。</summary>
    public class MacroItem : ObservableObject
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    public ObservableCollection<ButtonItem> Buttons { get; } = new();
    /// <summary>鼠标图上的 10 个位置指示图标（6 圆 + 4 箭头），见 IconMarker 类。</summary>
    public ObservableCollection<IconMarker> IconMarkers { get; } = new();
    /// <summary>本地维护的 18 项全表副本（0x08 整表覆写、不可读，见 AGENTSK 6 节）。</summary>
    public ButtonConfig BtnCfg { get; } = new();
    /// <summary>当前选中按钮的可选功能（含勾选状态，Group 分组）。</summary>
    public ObservableCollection<FuncOption> FuncOptions { get; } = new();

    private bool _isMacroSelected;
    /// <summary>当前选中功能为「宏」时切到宏 Tab。</summary>
    public bool IsMacroSelected
    {
        get => _isMacroSelected;
        set
        {
            if (SetProperty(ref _isMacroSelected, value))
                OnPropertyChanged(nameof(FuncTabIndex));
        }
    }
    /// <summary>右侧面板 Tab 索引：0=鼠标操作 1=宏。</summary>
    public int FuncTabIndex => _isMacroSelected ? 1 : 0;

    private ButtonItem? _selectedButton;
    /// <summary>当前选中的按钮（右侧功能面板数据源）。</summary>
    public ButtonItem? SelectedButton
    {
        get => _selectedButton;
        set
        {
            if (SetProperty(ref _selectedButton, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }
    public bool HasSelection => _selectedButton != null;

    /// <summary>宏列表（当前 1..6 占位；Phase 5 接入真实宏后动态变化）。</summary>
    public ObservableCollection<MacroItem> Macros { get; } = new();

    private void InitMacros()
    {
        for (int i = 1; i <= 6; i++)
            Macros.Add(new MacroItem { Id = i, Name = $"宏 {i}" });
        SyncMacroSelection();
    }

    /// <summary>同步宏列表勾选态（当前选中的宏 ID 勾选）。</summary>
    private void SyncMacroSelection()
    {
        foreach (var m in Macros) m.IsSelected = (m.Id == _selectedMacroId);
    }

    private int _selectedMacroId = 1;
    /// <summary>宏绑定二级选择：宏 ID（1..6）。</summary>
    public int SelectedMacroId
    {
        get => _selectedMacroId;
        set
        {
            if (SetProperty(ref _selectedMacroId, value))
            {
                SyncMacroSelection();
                // 若当前按钮已绑定宏，切 ID 立即改 entry[2] 并进入待保存
                var btn = SelectedButton;
                if (btn != null && BtnCfg.Entries[btn.EntryIndex][0] == ButtonConfig.FuncCode.Macro)
                    ApplyEntryChange(btn, ButtonConfig.FuncCode.Macro, (byte)value);
            }
        }
    }

    /// <summary>设置宏命令（参数 = 宏 ID）。</summary>
    public RelayCommand SetMacroCmd { get; }

    /// <summary>选中按钮命令（参数 = 按钮 Index）。</summary>
    public RelayCommand SelectButtonCmd { get; }
    /// <summary>设置选中按钮功能命令（参数 = 功能码）。</summary>
    public RelayCommand SetFuncCmd { get; }

    private System.Threading.Timer? _buttonSaveDebounce;

    private void InitButtons()
    {
        // 启动时先加载本地持久化的全表副本（上次保存的按键映射）；无保存则用出厂默认表。
        // 否则启动后 BtnCfg 为默认表，改任一键整表覆写会把设备上其他改过的键重置（AGENTSK 6 节）。
        LoadButtons();
        // 10 个可编程物理键 → 协议 entry 索引（HID协议逆向报告.md 3.5 节 UI→entry 映射）。
        // TagX/TagY 为标签在 660×660 鼠标图上的估算位置（侧视图：顶部=左右键/滚轮/DPI，侧边=前进后退/拇指滚轮）。
        var map = new (string Name, int Entry, bool Primary, string IconKey, double TagX, double TagY)[]
        {
            // 300×506 系初值（旧 660 系换算 + 按新图轮廓估；拖动标签可实时定位，最终以 button-tags.json 为准）
            ("左键",   0, true,  "左键", 50,  40),
            ("右键",   1, true,  "右键", 140, 40),
            ("滚轮",   4, true,  "中键", 90,  100),   // 中键点击（顶部中部）
            ("上滚",   16, false,"上滚", 90,  60),
            ("下滚",   17, false,"下滚", 90, 140),
            ("DPI 键", 5, false, "DPI", 140, 100),
            ("前进",   2, false, "前进", 60, 180),
            ("后退",   3, false, "后退",210,180),
            ("左滚",   14,false, "左滚", 60, 260),
            ("右滚",   15,false, "右滚",210,260),
        };
        for (int i = 0; i < map.Length; i++)
        {
            var entry = BtnCfg.Entries[map[i].Entry];
            Buttons.Add(new ButtonItem
            {
                Index = i,
                Name = map[i].Name,
                EntryIndex = map[i].Entry,
                IsPrimary = map[i].Primary,
                IconKey = map[i].IconKey,
                TagX = map[i].TagX,
                TagY = map[i].TagY,
                FunctionName = ButtonFunc.NameOf(entry[0]),
            });
        }
        _buttonSaveDebounce = new System.Threading.Timer(_ => Application.Current.Dispatcher.BeginInvoke(SaveButtons),
            null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        // 拖动定位期间：若有调试坐标文件则覆盖默认坐标（重启不丢已拖好的位置）
        LoadTagPositions();
    }

    /// <summary>标签坐标调试文件路径（临时：拖动标签定位按键后写此文件，确认后写死默认值并移除）。</summary>
    private static string TagPositionsPath()
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DELUX.Driver", "button-tags.json");

    /// <summary>拖动定位：把当前标签坐标写入本地文件（用户拖好后读此文件更新默认值）。</summary>
    public void SaveTagPositions()
    {
        try
        {
            var path = TagPositionsPath();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var dict = Buttons.ToDictionary(b => b.Name, b => new[] { b.TagX, b.TagY });
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(dict));
        }
        catch { /* 调试辅助，失败忽略 */ }
    }

    /// <summary>若存在调试坐标文件则加载覆盖默认坐标（拖动定位期间重启不丢位置）。</summary>
    private void LoadTagPositions()
    {
        try
        {
            var path = TagPositionsPath();
            if (!System.IO.File.Exists(path)) return;
            var dict = System.Text.Json.JsonSerializer
                .Deserialize<System.Collections.Generic.Dictionary<string, double[]>>(System.IO.File.ReadAllText(path));
            if (dict == null) return;
            foreach (var b in Buttons)
                if (dict.TryGetValue(b.Name, out var xy) && xy is { Length: 2 })
                {
                    b.TagX = xy[0];
                    b.TagY = xy[1];
                }
        }
        catch { /* 读失败用默认坐标 */ }
    }

    /// <summary>初始化 10 个位置指示图标（6 圆 = 按键位 + 4 箭头 = 滚轮方向）。
    /// 坐标已在 300×506 鼠标图上拖到按键物理中心，固定默认值。</summary>
    private void InitIconMarkers()
    {
        var map = new (string Name, IconMarker.MarkerKind Kind, string IconKey, double X, double Y)[]
        {
            ("左键",   IconMarker.MarkerKind.Circle,    "左键", 141.2, 111.4),
            ("右键",   IconMarker.MarkerKind.Circle,    "右键", 197.2, 116.3),
            ("中键",   IconMarker.MarkerKind.Circle,    "中键", 172.7,  73.9),
            ("前进",   IconMarker.MarkerKind.Circle,    "前进",  33.9, 109.3),
            ("后退",   IconMarker.MarkerKind.Circle,    "后退",  14.3, 165.9),
            ("DPI 键", IconMarker.MarkerKind.Circle,    "DPI",   72.9, 153.0),
            ("上滚",   IconMarker.MarkerKind.ArrowUp,   "上滚", 157.8,  40.2),
            ("下滚",   IconMarker.MarkerKind.ArrowDown, "下滚", 173.4, 113.9),
            ("左滚",   IconMarker.MarkerKind.ArrowLeft, "左滚",  38.8, 139.9),
            ("右滚",   IconMarker.MarkerKind.ArrowRight,"右滚",  61.6, 129.3),
        };
        for (int i = 0; i < map.Length; i++)
            IconMarkers.Add(new IconMarker { Name = map[i].Name, Kind = map[i].Kind, IconKey = map[i].IconKey, Index = i + 1, X = map[i].X, Y = map[i].Y });
        LinkIconsToButtons();
    }

    /// <summary>按 IconKey 把图标编号（1..10）回填到对应按键标签，建立图标↔标签一一对应。</summary>
    private void LinkIconsToButtons()
    {
        var iconByKey = IconMarkers.ToDictionary(m => m.IconKey, m => m);
        foreach (var b in Buttons)
            if (iconByKey.TryGetValue(b.IconKey, out var m))
                b.IconIndex = m.Index;
    }

    public void SelectButton(int index)
    {
        if (index < 0 || index >= Buttons.Count) return;
        // toggle：再次点击已选中的标签 → 取消选中，右侧面板消失
        if (SelectedButton != null && SelectedButton.Index == index)
        {
            for (int i = 0; i < Buttons.Count; i++) Buttons[i].IsSelected = false;
            SelectedButton = null;
            return;
        }
        for (int i = 0; i < Buttons.Count; i++) Buttons[i].IsSelected = (i == index);
        SelectedButton = Buttons[index];
        BuildFuncOptions(BtnCfg.Entries[Buttons[index].EntryIndex][0]);
        // 若该按钮已绑定宏，宏列表勾选同步到该按钮的宏 ID（否则保持上次选择）
        var entry = BtnCfg.Entries[Buttons[index].EntryIndex];
        if (entry[0] == ButtonConfig.FuncCode.Macro && entry[2] >= 1 && entry[2] <= 6)
            SelectedMacroId = entry[2];
    }

    /// <summary>重建右侧功能面板（「鼠标操作」Tab 选项 + 宏 Tab 选中态）。</summary>
    private void BuildFuncOptions(byte currentCode)
    {
        FuncOptions.Clear();
        foreach (var f in ButtonFunc.All)
        {
            if (f.Code == ButtonConfig.FuncCode.Macro) continue; // 宏在独立 Tab
            FuncOptions.Add(new FuncOption { Code = f.Code, Name = f.Name, IsChecked = f.Code == currentCode });
        }
        IsMacroSelected = currentCode == ButtonConfig.FuncCode.Macro;
    }

    /// <summary>设置选中按钮的功能（克隆全表 → 改目标 entry → 防抖整表写）。
    /// 关键键（左/右/中）改为非自身功能时弹风险确认（原型 §2.4）。</summary>
    public void SetButtonFunction(byte code)
    {
        var btn = SelectedButton;
        if (btn == null) return;
        if (!IsConnected) { ShowToast("请先连接鼠标"); return; }
        if (OfficialDriverRunning)
        {
            ShowToast("检测到官方驱动运行中，已取消修改。请完全退出 Mouse.exe 后重试。");
            return;
        }
        byte param = code == ButtonConfig.FuncCode.Macro ? (byte)_selectedMacroId : (byte)0;
        // 关键键改掉自身功能（如左键不再当左键）→ 风险确认，避免用户点掉后没法点回来。
        if (btn.IsPrimary && code != BtnCfg.Entries[btn.EntryIndex][0]
            && code != DefaultFuncFor(btn.EntryIndex))
        {
            var r = System.Windows.MessageBox.Show(
                $"将把「{btn.Name}」从「{btn.FunctionName}」改为「{ButtonFunc.NameOf(code)}」。\n\n" +
                "修改后该键将不再执行原功能，请确认（改错了可在 1.5 秒内点回原功能）。",
                "修改按键", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
            if (r != System.Windows.MessageBoxResult.OK) return;
        }
        ApplyEntryChange(btn, code, param);
        BuildFuncOptions(code);
    }

    /// <summary>entry 默认功能（用于风险确认判定：改回自身默认不算危险）。</summary>
    private static byte DefaultFuncFor(int entryIndex) => entryIndex switch
    {
        0 => ButtonConfig.FuncCode.Left,
        1 => ButtonConfig.FuncCode.Right,
        4 => ButtonConfig.FuncCode.Middle,
        _ => 0xFF,
    };

    private void ApplyEntryChange(ButtonItem btn, byte code, byte param)
    {
        BtnCfg.Entries[btn.EntryIndex] = new byte[] { code, 0x00, param };
        btn.FunctionName = ButtonFunc.NameOf(code);
        SaveStatusText = "待保存…";
        _buttonSaveDebounce?.Change(AutoSaveDelayMs, System.Threading.Timeout.Infinite);
    }

    /// <summary>防抖到期：整表覆写 0x08（0x0C 唤醒 + 59 字节报告，含校验和）。</summary>
    public void SaveButtons()
    {
        if (!IsConnected) { SaveStatusText = ""; return; }
        if (OfficialDriverRunning)
        {
            SaveStatusText = "";
            ShowToast("检测到官方驱动运行中，写入已拦截。请完全退出 Mouse.exe 后重试。");
            return;
        }
        SaveStatusText = "正在保存…";
        if (_hid.Wake() && _hid.WriteFeature(BtnCfg.ToBytes()))
        {
            SaveButtonsTable(); // 设备写成功后持久化全表副本（本地维护，见 AGENTSK 6 节）
            SaveStatusText = "已保存 ✓";
            ShowToast("已保存按键映射");
        }
        else
        {
            SaveStatusText = "保存失败";
            ShowToast($"保存失败：{_hid.LastErrorMessage}。请确认已退出官方驱动后重试。");
        }
    }

    private static string ButtonsConfigPath()
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DELUX.Driver", "buttons.json");

    /// <summary>启动时加载本地持久化的 18 项全表副本（0x08 不可读，须本地维护，见 AGENTSK 6 节）。</summary>
    private void LoadButtons()
    {
        try
        {
            var path = ButtonsConfigPath();
            if (!System.IO.File.Exists(path)) return;
            var arr = System.Text.Json.JsonSerializer.Deserialize<byte[][]>(System.IO.File.ReadAllText(path));
            if (arr != null && arr.Length == 18)
            {
                for (int i = 0; i < 18; i++)
                    if (arr[i] == null || arr[i].Length != 3) return; // 结构损坏，回退默认表
                BtnCfg.Entries = arr;
            }
        }
        catch { /* 损坏/读失败用默认表 */ }
    }

    /// <summary>设备写成功后持久化全表副本（JSON：18×3 字节数组）。</summary>
    private void SaveButtonsTable()
    {
        try
        {
            var path = ButtonsConfigPath();
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(BtnCfg.Entries));
        }
        catch { /* 写失败不影响本次使用 */ }
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
            {
                OnPropertyChanged(nameof(ShowDefaultTitle));
                OnPropertyChanged(nameof(IsDimmed));
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowDefaultTitle));
                OnPropertyChanged(nameof(IsDimmed));
            }
        }
    }

    /// <summary>默认空态标题「请连接设备」可见：未连接、未忙碌、无错误。</summary>
    public bool ShowDefaultTitle => !IsBusy && !IsConnected && !ConnectErrorVisible;

    /// <summary>显示「连接失败」标题（官方驱动冲突等无法重试的场景）。</summary>
    public bool ShowErrorTitle => ConnectErrorVisible && !ShowReconnect && !IsBusy && !IsConnected;

    /// <summary>连接失败时卡片背景变灰。</summary>
    public bool IsDimmed => !IsConnected && !IsBusy && (ShowReconnect || ConnectErrorVisible);

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
        public ICommand SetAppearanceCmd { get; }

    /// <summary>自动连接超时后显示「重新识别」按钮（用户可手动重试）。</summary>
    private bool _showReconnect;
    public bool ShowReconnect
    {
        get => _showReconnect;
        set
        {
            if (SetProperty(ref _showReconnect, value))
            {
                OnPropertyChanged(nameof(ShowErrorTitle));
                OnPropertyChanged(nameof(IsDimmed));
            }
        }
    }

    #region 连接失败原因（原型 §1.3：区分场景给可执行文案）

    private bool _connectErrorVisible;
    public bool ConnectErrorVisible
    {
        get => _connectErrorVisible;
        set
        {
            if (SetProperty(ref _connectErrorVisible, value))
            {
                OnPropertyChanged(nameof(ShowDefaultTitle));
                OnPropertyChanged(nameof(ShowErrorTitle));
                OnPropertyChanged(nameof(IsDimmed));
            }
        }
    }

    private string _connectErrorText = "";
    public string ConnectErrorText
    {
        get => _connectErrorText;
        set => SetProperty(ref _connectErrorText, value);
    }

    /// <summary>
    /// 根据 HID 返回的错误信息区分失败场景，给出可执行文案（原型 §1.3）。
    /// 协议术语不外显；统一动词「连接设备」。
    /// </summary>
    private void SetConnectErrorByReason(string? reason)
    {
        ConnectErrorVisible = true;
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
        Navigation?.Navigate("Connect");
        AppendLog("已断开连接。");
    }

    /// <summary>
    /// 软件启动后即自动尝试识别鼠标（无需用户点击）。连接中显示进度环；
    /// 若超过 ConnectTimeoutMs 仍未连上，停止并显示「重新识别」按钮。
    /// </summary>
    public void AutoConnect()
    {
        _autoConnectAttempts = 0;
        _ = ConnectAsync();
    }

    private async System.Threading.Tasks.Task ConnectAsync()
    {
        // 允许重试调用的 IsBusy 绕过（_pendingRetry 期间 IsBusy 保持 true）
        if (IsBusy && !_pendingRetry) return;
        _pendingRetry = false;
        IsBusy = true;
        ShowReconnect = false;
        ConnectErrorVisible = false;
        StatusColor = "#E0A000";
        StatusText = "正在识别鼠标…";
        ConnectButtonText = "正在识别…";
        AppendLog("正在识别鼠标…");

        // 启动超时计时：超过阈值仍未连上 → 提示重新识别。
        _connectTimer.Change(ConnectTimeoutMs, System.Threading.Timeout.Infinite);

        bool shouldRetry = false;

        try
        {
            // 重新连接前先停止上一次的监听线程（若有残留）。
            _hid.StopInputListener();

            // 前置条件（AGENTS.md）：发任何配置命令前必须退出官方 Mouse.exe。
            if (OfficialDriverRunning)
            {
                ConnectErrorVisible = true;
                ShowReconnect = true;
                ConnectErrorText = "请先完全退出 Mouse.exe 再重新打开本软件。";
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
                _autoConnectAttempts = 0;
                ConnectErrorVisible = false;
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
                _autoConnectAttempts++;
                if (_autoConnectAttempts < MaxAutoConnectAttempts)
                {
                    // 自动重试，不显示错误
                    shouldRetry = true;
                    StatusText = $"正在识别鼠标（第{_autoConnectAttempts + 1}/{MaxAutoConnectAttempts}次）";
                    AppendLog($"第{_autoConnectAttempts}次识别未成功，{RetryDelayMs / 1000}秒后重试…");
                }
                else
                {
                    // 3 次均失败，显示错误
                    StatusColor = "#888888";
                    StatusText = "未连接";
                    ConnectButtonText = "连接设备";
                    DeviceName = "未连接";
                    ShowReconnect = true;
                    ConnectErrorVisible = true;
                    ConnectErrorText = _hid.IsConnected
                        ? "已检测到接收器，但鼠标未开机或无响应，请打开鼠标后点击重新识别。"
                        : $"连接失败，请确认接收器已插入、官方驱动已关闭后重试。";
                    AppendLog(_hid.IsConnected
                        ? "接收器已枚举，但未收到鼠标 Input 上报：鼠标可能未开机。"
                        : $"连接失败：{_hid.LastErrorMessage}");
                }
            }
        }
        finally
        {
            _connectTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            if (shouldRetry)
            {
                // 重试期间保持 IsBusy=true，标题不闪回默认态
                _pendingRetry = true;
                _ = System.Threading.Tasks.Task.Delay(RetryDelayMs).ContinueWith(_ =>
                    Application.Current.Dispatcher.BeginInvoke(() => _ = ConnectAsync()));
            }
            else
            {
                IsBusy = false;
            }
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
        _saveDebounce?.Dispose();
        _buttonSaveDebounce?.Dispose();
        _toastTimer?.Stop();
    }

    #endregion
}
