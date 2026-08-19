using System;
using System.Collections.Generic;
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
        HoverButtonCmd = new RelayCommand(p => SetButtonHover(System.Convert.ToInt32(p), true));
        UnhoverButtonCmd = new RelayCommand(p => SetButtonHover(System.Convert.ToInt32(p), false));
        SetMacroCmd = new RelayCommand(p => SelectMacroBinding(System.Convert.ToInt32(p)));
        NewMacroCmd = new RelayCommand(_ => NewMacro());
        SelectMacroCmd = new RelayCommand(p => SelectMacro(System.Convert.ToInt32(p)));
        CopyMacroCmd = new RelayCommand(_ => DuplicateMacro());
        DeleteMacroCmd = new RelayCommand(p => DeleteMacro(p is int id ? id : 0));
        SaveMacroCmd = new RelayCommand(_ => SaveMacro());
        CloseMacroCmd = new RelayCommand(_ => CloseMacro());
        ImportMacroCmd = new RelayCommand(_ => ImportMacro());
        ExportMacroCmd = new RelayCommand(_ => ExportMacro());
        RecordToggleCmd = new RelayCommand(_ => ToggleRecord());
        InsertKeyCmd = new RelayCommand(_ => InsertKey());
        InsertMouseCmd = new RelayCommand(_ => InsertMouse());
        ClearActionsCmd = new RelayCommand(_ => ClearActions());
        // 左键改键风险确认（仅左键点击标签即弹，未选功能前先提示；见 SelectButton / ConfirmLeftBtnChange）。
        LeftBtnConfirmOkCmd = new RelayCommand(_ => ConfirmLeftBtnChange());
        LeftBtnConfirmCancelCmd = new RelayCommand(_ => CancelLeftBtnChange());
        _recorder.KeyEvent += OnKeyEvent;
        _recorder.MouseEvent += OnMouseEvent;
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
            RefreshDpiStageBrushes();           // 指示灯色取自主题字典，需随之刷新
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

        /// <summary>档位指示灯色（模拟硬件 LED）：红/绿/蓝/紫/黄 = DPI 1..5，
        /// 与鼠标 DPI 键灯色一一对应（M618XSD驱动功能Wiki.md 3.2 节）；
        /// 颜色定义在浅/深主题字典，主题切换时由 RefreshDpiStageBrushes 刷新（需可写）。</summary>
        public Brush IndicatorBrush { get; set; } = Brushes.Gray;

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
        // 指示灯色序红/绿/蓝/紫/黄 = DPI 1..5（与鼠标 DPI 键灯色一致），
        // 颜色取自主题字典 DpiStageNIndicatorBrush，随浅/深主题切换自动刷新。
        int[] defaults = { 800, 1200, 1600, 2400, 4000 };
        for (int i = 0; i < defaults.Length; i++)
        {
            var item = new DpiLevelItem
            {
                Index = i + 1,
                Value = defaults[i].ToString(),
                Enabled = true,
                IndicatorBrush = GetDpiStageBrush(i + 1),
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

    /// <summary>从主题字典取第 n 档（1..5）指示灯画刷；缺失时回退灰色，保证主题切换不抛异常。</summary>
    private static Brush GetDpiStageBrush(int n)
    {
        var key = $"DpiStage{n}IndicatorBrush";
        return Application.Current.TryFindResource(key) is Brush b ? b : Brushes.Gray;
    }

    /// <summary>主题切换后刷新各档指示灯画刷（颜色定义在浅/深主题字典中，随主题变化）。</summary>
    private void RefreshDpiStageBrushes()
    {
        foreach (var lvl in _dpiLevels)
            lvl.IndicatorBrush = GetDpiStageBrush(lvl.Index);
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
        if (level == _activeLevel) return; // 已是当前档：不写设备（同类 no-op，见 ApplyEntryChange 守卫）
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
        private bool _isSelected;
        /// <summary>对应按键标签被选中时联动高亮（图 ↔ 标签 ↔ 面板 三向呼应）。</summary>
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
        private bool _isHovered;
        /// <summary>对应按键标签被鼠标悬停时联动高亮（hover 态；选中态 IsSelected 优先级更高，见 ADR buttons-hover-link）。</summary>
        public bool IsHovered { get => _isHovered; set => SetProperty(ref _isHovered, value); }
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
            new(0x12, "快捷指令", "快捷指令"),
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

    // ============================ 宏管理（快捷指令页，Phase 5） ============================
    // 设备宏槽位固定 1..6（0x08 按键映射 entry[2] 与 0x09 宏头 buf[2] 用同一 ID，见 HID协议逆向报告.md 3.6）。
    // 上位机按「可命名的宏列表」管理，保存时映射到设备槽位；名称↔槽位映射随 macros.json 本地持久化。
    // 宏动作码仅键盘已验证（鼠标动作码未逆向 → UI 置灰「等待协议补齐」，绝不盲发，AGENTSK 6 节）。

    private const int MaxMacroSlots = 6;
    /// <summary>单个宏动作步数上限（0x09 数据区 [30..128] 共 49 对，见 Models.cs BuildCommandBuffer）。</summary>
    private const int MaxMacroActions = 49;

    /// <summary>宏列表项（改键页宏 Tab 与快捷指令页左侧列表共用；Id=设备槽位，0=未分配）。</summary>
    public class MacroItem : ObservableObject
    {
        /// <summary>设备槽位 1..6；0 = 尚未分配（新建宏首次保存时映射空闲槽）。</summary>
        public int Id { get; set; }

        public MacroConfig Config { get; init; } = new();

        /// <summary>宏名称（编辑即重命名，INPC 驱动列表/编辑器头部同步）。</summary>
        public string Name
        {
            get => Config.Name;
            set
            {
                if (Config.Name == value) return;
                Config.Name = value;
                OnPropertyChanged();
            }
        }

        public bool IsEmpty => Config.Actions.Count == 0;
        public int StepCount => Config.Actions.Count;

        /// <summary>列表副行摘要：步骤数 + 播放方式。</summary>
        public string Summary => Config.Actions.Count == 0 ? "无动作" : $"{Config.Actions.Count} 步 · {MethodName(Config.Method)}";

        /// <summary>播放方式中文名（0x00=循环次数 0x01=任意键停止 0x02=按住循环，HID协议逆向报告.md 3.6）。</summary>
        public static string MethodName(int method) => method switch
        {
            0x00 => "循环次数",
            0x01 => "任意键停止",
            0x02 => "按住循环",
            _ => $"未知({method})",
        };

        /// <summary>是否已被某个按键绑定（VM 注入的检查委托；绑定变化时 NotifyBoundChanged 刷新）。</summary>
        public Func<bool>? BoundCheck { get; set; }
        public bool IsBound => BoundCheck?.Invoke() ?? false;
        public void NotifyBoundChanged() => OnPropertyChanged(nameof(IsBound));

        /// <summary>快捷指令页左侧列表的选中态（正在编辑哪个宏）。</summary>
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        /// <summary>改键页宏 Tab 的绑定选中态（当前按钮绑定的宏 ID）——与编辑选中态分离，避免两处互斥打架。</summary>
        private bool _isBindingSelected;
        public bool IsBindingSelected { get => _isBindingSelected; set => SetProperty(ref _isBindingSelected, value); }

        public void NotifySummaryChanged()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(StepCount));
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>宏编辑器动作行（按键 | 按下/释放 | 设备实际延迟ms）。</summary>
    public class MacroActionItem : ObservableObject
    {
        public MacroAction Action { get; init; } = new();

        /// <summary>延迟被修改（VM 订阅此事件以标记「未保存」——延迟框直改共享 Action，绕过了 MarkMacroDirty）。</summary>
        public event Action? DelayChanged;

        public string KeyName => InputRecorder.KeyNameOf(Action.Code);

        /// <summary>更新按键码并刷新显示名（供 UI 编辑键值回写）。</summary>
        public void SetCode(byte newCode)
        {
            if (Action.Code == newCode) return;
            Action.Code = newCode;
            OnPropertyChanged(nameof(KeyName));
        }

        public bool Press
        {
            get => Action.Press;
            set
            {
                if (Action.Press == value) return;
                Action.Press = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PressText));
            }
        }
        public string PressText => Press ? "按下" : "释放";

        /// <summary>设备实际生效延迟（ms）：0=无延迟，否则 10..635 按 5ms 取整
        /// （编码 byte=round(ms/5) 1..127，与设备解码 max(10, byte×5) 互逆，见 Models.cs）。</summary>
        public int DelayMs
        {
            get => Action.DelayMs;
            set
            {
                int v = NormalizeDelay(value);
                if (Action.DelayMs == v) return;
                Action.DelayMs = v;
                OnPropertyChanged();
                DelayChanged?.Invoke();
            }
        }

        /// <summary>延迟规范化：0 保持 0；否则按 5 取整后 clamp 到 10..635。</summary>
        public static int NormalizeDelay(int ms)
        {
            if (ms <= 0) return 0;
            int r = (int)(Math.Round(ms / 5.0, MidpointRounding.AwayFromZero) * 5);
            return Math.Clamp(r, 10, 635);
        }

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

    /// <summary>当前选中按钮生效的「鼠标操作」功能码，供 ListView（SelectedValue）高亮当前项。
    /// 宏功能在独立 Tab，不计入此值（ListView 仅承载鼠标操作选项）。</summary>
    public byte? SelectedButtonFuncCode
    {
        get
        {
            var btn = SelectedButton;
            if (btn == null) return null;
            var entry = BtnCfg.Entries[btn.EntryIndex];
            byte code = entry[0];
            return code == ButtonConfig.FuncCode.Macro ? (byte?)null : code;
        }
    }

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

    /// <summary>宏列表（设备槽位 1..6；新建宏保存时分配空闲槽）。</summary>
    public ObservableCollection<MacroItem> Macros { get; } = new();

    /// <summary>是否还能新建宏（设备最多 6 槽）。</summary>
    public bool CanAddMacro => Macros.Count < MaxMacroSlots;
    public bool MacrosFullVisible => Macros.Count >= MaxMacroSlots;

    /// <summary>宏数据本地持久化路径（名称↔槽位映射，跨重启不丢，见 AGENTS.md 宏节）。</summary>
    private static string MacrosConfigPath()
        => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DELUX.Driver", "macros.json");

    private class MacroItemDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Method { get; set; }
        public int LoopCount { get; set; } = 1;
        public List<MacroActionDto>? Actions { get; set; }
    }
    private class MacroActionDto
    {
        public int Code { get; set; }
        public bool Press { get; set; }
        public int DelayMs { get; set; }
    }

    private void InitMacros()
    {
        LoadMacros();
    }

    /// <summary>启动时从本地加载宏列表（损坏/读失败 → 空列表，走原型空状态「暂无宏配置」）。</summary>
    private void LoadMacros()
    {
        try
        {
            var path = MacrosConfigPath();
            if (!System.IO.File.Exists(path)) return;
            var arr = System.Text.Json.JsonSerializer.Deserialize<List<MacroItemDto>>(System.IO.File.ReadAllText(path));
            if (arr == null) return;
            foreach (var d in arr)
            {
                var cfg = new MacroConfig
                {
                    Id = d.Id,
                    Name = d.Name ?? "",
                    Method = d.Method is >= 0 and <= 2 ? d.Method : 0,
                    LoopCount = Math.Clamp(d.LoopCount, 1, 255),
                };
                if (d.Actions != null)
                    foreach (var a in d.Actions)
                        cfg.Actions.Add(new MacroAction { Code = (byte)Math.Clamp(a.Code, 0, 255), Press = a.Press, DelayMs = MacroActionItem.NormalizeDelay(a.DelayMs) });
                AddMacroItem(cfg, d.Id);
            }
        }
        catch { /* 损坏/读失败 → 空列表 */ }
        NotifyMacroListChanged();
    }

    /// <summary>持久化宏列表到本地（名称/播放方式/循环次数/动作全量）。</summary>
    private void PersistMacros()
    {
        try
        {
            var path = MacrosConfigPath();
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            var dtos = Macros.Select(m => new MacroItemDto
            {
                Id = m.Id,
                Name = m.Name,
                Method = m.Config.Method,
                LoopCount = m.Config.LoopCount,
                Actions = m.Config.Actions.Select(a => new MacroActionDto { Code = a.Code, Press = a.Press, DelayMs = a.DelayMs }).ToList(),
            }).ToList();
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(dtos));
        }
        catch { /* 写失败不影响本次使用 */ }
    }

    /// <summary>加入列表：槽位非法/冲突则自动取空闲槽；返回新建的项。</summary>
    private MacroItem AddMacroItem(MacroConfig cfg, int slot)
    {
        if (slot < 1 || slot > MaxMacroSlots || Macros.Any(m => m.Id == slot))
            slot = NextFreeSlot();
        var item = new MacroItem { Id = slot, Config = cfg };
        item.BoundCheck = () => IsMacroBound(item.Id);
        if (string.IsNullOrWhiteSpace(item.Name))
            item.Name = $"MX{NextMacroIndex()}";
        Macros.Add(item);
        NotifyMacroListChanged();
        return item;
    }

    /// <summary>取第一个空闲槽位（1..6），全满返回 0。</summary>
    private int NextFreeSlot()
    {
        for (int s = 1; s <= MaxMacroSlots; s++)
            if (!Macros.Any(m => m.Id == s)) return s;
        return 0;
    }

    /// <summary>取下一个宏默认名序号：扫描已存宏名中的 "MX{n}"，返回最大 n+1（无则 1）。</summary>
    private int NextMacroIndex()
    {
        int max = 0;
        foreach (var m in Macros)
        {
            var mt = System.Text.RegularExpressions.Regex.Match(m.Name ?? "", @"^MX(\d+)$");
            if (mt.Success && int.TryParse(mt.Groups[1].Value, out int n))
                max = Math.Max(max, n);
        }
        return max + 1;
    }

    private void NotifyMacroListChanged()
    {
        OnPropertyChanged(nameof(CanAddMacro));
        OnPropertyChanged(nameof(MacrosFullVisible));
        SyncMacroSelection();
    }

    /// <summary>宏是否被任一按键绑定（0x08 全表 entry[0]==Macro && entry[2]==id）。</summary>
    private bool IsMacroBound(int id)
    {
        if (id < 1 || id > MaxMacroSlots) return false;
        return BtnCfg.Entries.Any(e => e[0] == ButtonConfig.FuncCode.Macro && e[2] == id);
    }

    /// <summary>按键绑定变化后刷新各宏的 IsBound 与未绑定警告。</summary>
    private void NotifyMacroBoundChanged()
    {
        foreach (var m in Macros) m.NotifyBoundChanged();
    }

    /// <summary>同步改键页宏 Tab 的绑定勾选态（当前按钮绑定的宏 ID）。</summary>
    private void SyncMacroSelection()
    {
        foreach (var m in Macros) m.IsBindingSelected = (m.Id == _selectedMacroId);
    }

    private int _selectedMacroId;
    /// <summary>宏绑定二级选择：宏 ID（1..6，0=未选）。点击任一宏 ID 即把当前按钮绑定为「宏 + 该 ID」
    /// （绑定/换 ID 均走 ApplyEntryChange，无变化不触发保存）。此属性只承载「勾选态 + 绑定动作」：
    /// 选中未绑定宏的按钮或按钮功能改为非宏时必须先清空（见 ClearMacroSelection），否则宏 Tab 会残留
    /// 上次选中的宏 ID——用户看到「MX1 已被选中」的假象，且再点它因值未变而绑定不生效（点了没反应）。</summary>
    public int SelectedMacroId
    {
        get => _selectedMacroId;
        set
        {
            if (SetProperty(ref _selectedMacroId, value))
            {
                SyncMacroSelection();
                var btn = SelectedButton;
                if (btn != null && ApplyEntryChange(btn, ButtonConfig.FuncCode.Macro, (byte)value))
                {
                    BuildFuncOptions(ButtonConfig.FuncCode.Macro); // 刷新右栏 Tab（切到宏 Tab）
                    NotifyMacroBoundChanged(); // 绑定变化 → 宏页未绑定警告同步
                }
            }
        }
    }

    /// <summary>宏 Tab 点击宏 ID 的入口：前置校验与 SetButtonFunction 一致（未连接 / 官方驱动运行中给
    /// Toast），避免「点了没反应」（AGENTS.md §3.3）；校验失败时重推勾选态，纠正 RadioButton 点击后
    /// 本地残留的勾选（OneWay 绑定不回写源，不重推会停在「已勾选但未绑定」的假象）。</summary>
    public void SelectMacroBinding(int id)
    {
        var btn = SelectedButton;
        if (btn == null) return;
        if (!IsConnected) { ReassertMacroChecks(); ShowToast("请先连接鼠标"); return; }
        if (OfficialDriverRunning)
        {
            ReassertMacroChecks();
            ShowToast("检测到官方驱动运行中，已取消修改。请完全退出 Mouse.exe 后重试。");
            return;
        }
        SelectedMacroId = id;
    }

    /// <summary>清空宏勾选态（仅改状态，不触发绑定/保存）。选中未绑定宏的按钮、或按钮功能改为非宏时
    /// 调用，避免宏 Tab 残留上次选中的宏 ID 造成「还没绑定却已勾选」的假象。</summary>
    private void ClearMacroSelection()
    {
        if (_selectedMacroId == 0) return;
        _selectedMacroId = 0;
        SyncMacroSelection();
    }

    /// <summary>强制重推宏 Tab 勾选态：先置反再置回（两次变更通知），让 OneWay 绑定覆盖
    /// RadioButton 组互斥/用户点击造成的目标侧残留（仅校验失败路径用于状态恢复）。</summary>
    private void ReassertMacroChecks()
    {
        foreach (var m in Macros)
        {
            m.IsBindingSelected = !m.IsBindingSelected;
            m.IsBindingSelected = !m.IsBindingSelected;
        }
    }

    /// <summary>新建宏：进入草稿编辑态（未加入列表，保存后才落库显示），避免未保存就污染列表。</summary>
    public void NewMacro()
    {
        if (Macros.Count >= MaxMacroSlots)
        {
            ShowToast($"设备最多支持 {MaxMacroSlots} 个快捷指令，请先删除一个");
            return;
        }
        // 草稿：Id=0（未分配槽），不入 Macros 集合；保存时由 SaveMacros 分配槽并落库
        var draft = new MacroItem { Id = 0, Config = new MacroConfig { Name = $"MX{NextMacroIndex()}", LoopCount = 1 } };
        draft.BoundCheck = () => IsMacroBound(draft.Id);
        // 统一走 EditingMacro setter：它会一并通知 EditingMacroName / HasEditingMacro /
        // EditingMethod / EditingLoopCount / IsLoopCountEnabled 并 RefreshEditingActions，
        // 确保名称框等编辑器字段立刻拿到草稿值（直接赋值字段会漏通知 EditingMacroName）
        foreach (var x in Macros) x.IsSelected = false;
        EditingMacro = draft;
        IsRecording = false;
        // 新草稿是干净状态：清掉可能残留的「未保存」标记（否则上次关闭编辑时未清除的
        // 残留标记会让未做任何修改的新草稿在关闭时误弹二次确认，历史缺陷）
        SaveStatusText = "";
        ShowToast("已新建快捷指令，点「开始录制」录制按键吧");
        // 不在新建时 ScheduleMacroSave —— 草稿未改动不应自动写盘（AGENTS.md 3.1：修改后才自动保存）
    }

    /// <summary>选中宏进入编辑（停止录制；加载动作序列到编辑器）。</summary>
    public void SelectMacro(int id)
    {
        var m = Macros.FirstOrDefault(x => x.Id == id);
        if (m == null) return;
        foreach (var x in Macros) x.IsSelected = (x == m);
        EditingMacro = m;
    }

    /// <summary>退出编辑视图、返回列表视图（清空当前编辑宏与动作序列）。
    /// 同时清掉「未保存」标记：关闭即退出编辑上下文，标记不得残留到下一次新建/编辑
    /// （否则未做修改的新草稿在关闭时也会误弹二次确认，历史缺陷）。</summary>
    public void CloseMacro()
    {
        EditingMacro = null;
        EditingActions.Clear();
        SelectedActionIndex = -1;
        SaveStatusText = "";
    }

    /// <summary>复制当前宏（名称 +「副本」，新槽位，进入编辑态）。</summary>
    public void DuplicateMacro()
    {
        var src = EditingMacro;
        if (src == null) return;
        if (Macros.Count >= MaxMacroSlots)
        {
            ShowToast($"设备最多支持 {MaxMacroSlots} 个快捷指令，请先删除一个");
            return;
        }
        var cfg = new MacroConfig { Name = src.Name + " 副本", Method = src.Config.Method, LoopCount = src.Config.LoopCount };
        foreach (var a in src.Config.Actions)
            cfg.Actions.Add(new MacroAction { Code = a.Code, Press = a.Press, DelayMs = a.DelayMs });
        var item = AddMacroItem(cfg, 0);
        SelectMacro(item.Id);
        ScheduleMacroSave();
        ShowToast("已复制快捷指令");
    }

    /// <summary>两步确认的武装目标宏 Id；-1 = 未武装。按目标记（武装 A 后再点 B 不会误删 B）。</summary>
    private int _deleteArmedId = -1;
    public bool DeleteArmed => _deleteArmedId >= 0;
    public string MacroDeleteButtonText => DeleteArmed ? "确认删除？" : "删除快捷指令";

    /// <summary>删除宏（两次点击确认；已绑定按键不静默改写，仅提示指向空槽）。
    /// 不传参时删当前编辑项；hover 删除传 Id。**第一击只武装、不进入编辑态**——
    /// 否则选中即禁用左栏（HasEditingMacro），第二击点不到按钮，删除死锁。</summary>
    public void DeleteMacro(int id = 0)
    {
        var m = id != 0 ? Macros.FirstOrDefault(x => x.Id == id) : EditingMacro;
        if (m == null) return;
        if (_deleteArmedId != m.Id)
        {
            _deleteArmedId = m.Id;
            OnPropertyChanged(nameof(DeleteArmed));
            ArmResetTimer();
            ShowToast("再次点击确认删除");
            return;
        }
        _deleteArmedId = -1;
        OnPropertyChanged(nameof(DeleteArmed));
        int bound = BtnCfg.Entries.Count(e => e[0] == ButtonConfig.FuncCode.Macro && e[2] == m.Id);
        string name = m.Name;
        Macros.Remove(m);
        if (EditingMacro == m)
        {
            EditingMacro = null;
            EditingActions.Clear();
        }
        NotifyMacroListChanged();
        PersistMacros();
        SaveStatusText = "";
        ShowToast(bound > 0
            ? $"已删除「{name}」，仍有 {bound} 个按键指向它（将无动作，可到改键设置改绑）"
            : $"已删除「{name}」");
        if (Macros.Count > 0 && EditingMacro == null) SelectMacro(Macros[0].Id);
    }

    /// <summary>导出当前宏为 JSON（本软件自用格式）。</summary>
    public void ExportMacro()
    {
        var m = EditingMacro;
        if (m == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "快捷指令文件 (*.json)|*.json",
            FileName = $"{SanitizeFileName(m.Name)}.json",
        };
        if (dlg.ShowDialog() != true) return;
        var dto = new MacroItemDto
        {
            Name = m.Name,
            Method = m.Config.Method,
            LoopCount = m.Config.LoopCount,
            Actions = m.Config.Actions.Select(a => new MacroActionDto { Code = a.Code, Press = a.Press, DelayMs = a.DelayMs }).ToList(),
        };
        System.IO.File.WriteAllText(dlg.FileName, System.Text.Json.JsonSerializer.Serialize(dto));
        ShowToast("已导出快捷指令");
    }

    /// <summary>导入宏 JSON：结构/范围校验，非法文件拒绝写入并提示（计划文档验收项）。</summary>
    public void ImportMacro()
    {
        if (Macros.Count >= MaxMacroSlots)
        {
            ShowToast($"设备最多支持 {MaxMacroSlots} 个快捷指令，请先删除一个");
            return;
        }
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "快捷指令文件 (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<MacroItemDto>(System.IO.File.ReadAllText(dlg.FileName));
            if (dto == null || dto.Actions == null) throw new FormatException();
            var rawName = string.IsNullOrWhiteSpace(dto.Name) ? "导入的快捷指令" : dto.Name.Trim();
            if (rawName.Length > 20) rawName = rawName[..20]; // 与编辑器名称框 MaxLength=20 一致
            var cfg = new MacroConfig
            {
                Name = rawName,
                Method = dto.Method is >= 0 and <= 2 ? dto.Method : 0,
                LoopCount = Math.Clamp(dto.LoopCount, 1, 255),
            };
            foreach (var a in dto.Actions)
            {
                if (a.Code is < 0 or > 255 || a.DelayMs < 0 || (a.DelayMs > 0 && (a.DelayMs < 5 || a.DelayMs > 635)))
                    throw new FormatException();
                cfg.Actions.Add(new MacroAction { Code = (byte)a.Code, Press = a.Press, DelayMs = MacroActionItem.NormalizeDelay(a.DelayMs) });
            }
            // 131B 数据区上限 49 对：超限截断并提示（与录制/插入的封顶一致）
            bool truncated = cfg.Actions.Count > MaxMacroActions;
            if (truncated) cfg.Actions = cfg.Actions.Take(MaxMacroActions).ToList();
            var item = AddMacroItem(cfg, 0);
            SelectMacro(item.Id);
            ScheduleMacroSave();
            ShowToast(truncated ? $"已导入（动作超过 {MaxMacroActions} 步，已保留前 {MaxMacroActions} 步）" : "已导入快捷指令");
        }
        catch
        {
            ShowToast("文件无法识别，请检查是否本软件导出"); // 非法文件不写入
        }
    }

    private static string SanitizeFileName(string s)
        => string.Concat(s.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    // ---- 编辑器状态 ----

    private MacroItem? _editingMacro;
    /// <summary>进入编辑态时的配置快照（「丢弃」回滚目标 = 上次保存状态；保存成功后刷新）。</summary>
    private MacroConfig? _editingSnapshot;

    /// <summary>当前编辑的宏（快捷指令页右侧编辑器数据源）。</summary>
    public MacroItem? EditingMacro
    {
        get => _editingMacro;
        private set
        {
            if (SetProperty(ref _editingMacro, value))
            {
                // 进入编辑态时快照当前（已保存）状态——编辑器全程直改共享对象
                // （Name/Config/动作序列引用），丢弃时必须能回滚到这里（历史缺陷：丢弃不真正回滚）
                _editingSnapshot = value == null ? null : CloneConfig(value.Config);
                OnPropertyChanged(nameof(HasEditingMacro));
                OnPropertyChanged(nameof(EditingMacroName));
                OnPropertyChanged(nameof(EditingMethod));
                OnPropertyChanged(nameof(EditingLoopCount));
                OnPropertyChanged(nameof(IsLoopCountEnabled));
                RefreshEditingActions();
            }
        }
    }
    public bool HasEditingMacro => _editingMacro != null;

    /// <summary>编辑器动作行集合（保存/持久化前由 MarkMacroDirty 写回 Config）。</summary>
    public ObservableCollection<MacroActionItem> EditingActions { get; } = new();

    /// <summary>宏名称编辑框（输入即重命名 + 待保存）。</summary>
    public string EditingMacroName
    {
        get => _editingMacro?.Name ?? "";
        set
        {
            if (_editingMacro == null || _editingMacro.Name == value) return;
            _editingMacro.Name = value;
            OnPropertyChanged();
            MarkMacroDirty();
        }
    }

    /// <summary>播放方式（0x00=循环次数 0x01=任意键停止 0x02=按住循环）。</summary>
    public int EditingMethod
    {
        get => _editingMacro?.Config.Method ?? 0;
        set
        {
            if (_editingMacro == null || _editingMacro.Config.Method == value) return;
            _editingMacro.Config.Method = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLoopCountEnabled));
            _editingMacro.NotifySummaryChanged();
            MarkMacroDirty();
        }
    }

    /// <summary>循环次数（仅「循环次数」方式生效，1..255）。</summary>
    public int EditingLoopCount
    {
        get => _editingMacro?.Config.LoopCount ?? 1;
        set
        {
            if (_editingMacro == null) return;
            int v = Math.Clamp(value, 1, 255);
            if (_editingMacro.Config.LoopCount == v) return;
            _editingMacro.Config.LoopCount = v;
            OnPropertyChanged();
            MarkMacroDirty();
        }
    }

    public bool IsLoopCountEnabled => _editingMacro != null && _editingMacro.Config.Method == 0x00;

    /// <summary>把编辑器行写回宏配置并启动防抖保存（动作/延迟/方法/次数任一变更都走这里）。</summary>
    private void MarkMacroDirty()
    {
        if (_editingMacro == null) return;
        _editingMacro.Config.Actions = EditingActions.Select(a => a.Action).ToList();
        _editingMacro.NotifySummaryChanged();
        ScheduleMacroSave();
    }

    private void RefreshEditingActions()
    {
        EditingActions.Clear();
        if (_editingMacro != null)
            foreach (var a in _editingMacro.Config.Actions)
            {
                var item = new MacroActionItem { Action = a };
                // 延迟框直改共享 Action 对象（见 MacroActionItem.DelayMs），必须经事件标记「未保存」
                item.DelayChanged += () => MarkMacroDirty();
                EditingActions.Add(item);
            }
        SelectedActionIndex = -1;
    }

    // ---- 动作序列编辑 ----

    private int _selectedActionIndex = -1;
    public int SelectedActionIndex
    {
        get => _selectedActionIndex;
        set
        {
            if (SetProperty(ref _selectedActionIndex, value))
            {
                for (int i = 0; i < EditingActions.Count; i++) EditingActions[i].IsSelected = (i == value);
                OnPropertyChanged(nameof(HasSelectedAction));
            }
        }
    }
    public bool HasSelectedAction => _selectedActionIndex >= 0 && _selectedActionIndex < EditingActions.Count;

    public void SelectAction(int index) => SelectedActionIndex = index;

    /// <summary>步数上限提示（仅一次，避免录制中重复弹）。低于上限时自动复位。</summary>
    private void NotifyMacroCap()
    {
        if (EditingActions.Count >= MaxMacroActions)
        {
            if (!_macroCapToastShown)
            {
                _macroCapToastShown = true;
                ShowToast($"快捷指令最多支持 {MaxMacroActions} 步动作");
            }
        }
        else
        {
            _macroCapToastShown = false;
        }
    }

    private bool _macroCapToastShown;

    private void AddAction(MacroAction action)
    {
        if (_editingMacro == null) return;
        // 按下动作受 49 步上限约束；「抬起」始终允许追加（防停止录制时补录的抬起被挡，导致修饰键卡死）
        if (action.Press && EditingActions.Count >= MaxMacroActions)
        {
            NotifyMacroCap();
            return;
        }
        EditingActions.Add(new MacroActionItem { Action = action });
        SelectedActionIndex = EditingActions.Count - 1;
        MarkMacroDirty();
    }

    /// <summary>切换选中行「按下/释放」。</summary>
    public void TogglePress(int index)
    {
        if (index < 0 || index >= EditingActions.Count) return;
        EditingActions[index].Press = !EditingActions[index].Press;
        MarkMacroDirty();
    }

    /// <summary>拖拽重排：把 from 位置动作移到 to 位置（to 为目标插入下标，已做边界归一化）。</summary>
    public void MoveAction(int from, int to)
    {
        if (from < 0 || from >= EditingActions.Count) return;
        to = Math.Max(0, Math.Min(to, EditingActions.Count - 1));
        if (to == from) return;
        EditingActions.Move(from, to);
        SelectedActionIndex = to;
        MarkMacroDirty();
    }

    public void DeleteAction(int index)
    {
        if (index < 0 || index >= EditingActions.Count) return;
        EditingActions.RemoveAt(index);
        SelectedActionIndex = -1;
        MarkMacroDirty();
    }

    private bool _clearArmed;
    public bool ClearArmed { get => _clearArmed; set { if (SetProperty(ref _clearArmed, value)) OnPropertyChanged(nameof(ClearButtonText)); } }
    public string ClearButtonText => ClearArmed ? "确认清空？" : "重置";

    /// <summary>清空动作序列（两次点击确认，防误清）。</summary>
    public void ClearActions()
    {
        if (EditingActions.Count == 0) return;
        if (!ClearArmed)
        {
            ClearArmed = true;
            ArmResetTimer();
            ShowToast("再次点击确认清空动作序列");
            return;
        }
        ClearArmed = false;
        EditingActions.Clear();
        SelectedActionIndex = -1;
        MarkMacroDirty();
        ShowToast("已清空动作序列");
    }

    private DispatcherTimer? _armResetTimer;
    /// <summary>两步确认的自动解除（2.5s 未点第二次 → 回到未武装态；一次性，触发后自停）。</summary>
    private void ArmResetTimer()
    {
        _armResetTimer?.Stop();
        _armResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _armResetTimer.Tick += (_, _) =>
        {
            _armResetTimer?.Stop(); // 一次性解除，避免每 2.5s 空转
            _deleteArmedId = -1;
            OnPropertyChanged(nameof(DeleteArmed));
            ClearArmed = false;
        };
        _armResetTimer.Start();
    }

    // ---- 实时录制（低级键盘钩子，仅录制期间安装；鼠标动作码未逆向 → 不提供录制鼠标，见 AGENTSK 6 节） ----

    private readonly InputRecorder _recorder = new();
    private DateTime _lastKeyTime;
    /// <summary>录制期间按下的键（未抬起）；停止录制时补录为「抬起」，防播放时修饰键/按键卡死。</summary>
    private readonly HashSet<byte> _recordingDownKeys = new();

    private bool _isRecording;
    public bool IsRecording { get => _isRecording; set { if (SetProperty(ref _isRecording, value)) OnPropertyChanged(nameof(RecordButtonText)); } }
    public string RecordButtonText => IsRecording ? "停止录制" : "开始录制";

    private bool _capturingKey;
    public bool IsCapturingKey { get => _capturingKey; set { if (SetProperty(ref _capturingKey, value)) OnPropertyChanged(nameof(InsertKeyButtonText)); } }
    public string InsertKeyButtonText => IsCapturingKey ? "请按下键盘按键…（Esc 取消）" : "插入键盘按键";

    private bool _capturingMouse;
    public bool IsCapturingMouse { get => _capturingMouse; set { if (SetProperty(ref _capturingMouse, value)) OnPropertyChanged(nameof(InsertMouseButtonText)); } }
    public string InsertMouseButtonText => IsCapturingMouse ? "请按下鼠标按键…（Esc 取消）" : "插入鼠标按键";

    /// <summary>录制开关（录制中再次点击 = 停止）。</summary>
    public void ToggleRecord()
    {
        if (IsRecording) { StopRecord(); return; }
        StartRecord();
    }

    private void StartRecord()
    {
        if (_editingMacro == null) return;
        // 录制是纯本地键盘捕获，不依赖设备连接（宏保存亦不写设备）
        if (OfficialDriverRunning) { ShowToast("检测到官方驱动运行中。请完全退出 Mouse.exe 后重试。"); return; }
        IsCapturingKey = false;  // 取消残留的「插入按键」捕获态
        IsCapturingMouse = false; // 取消残留的「插入鼠标按键」捕获态
        _lastKeyTime = default;
        _recordingDownKeys.Clear();
        _recorder.Install();
        IsRecording = true;
        ShowToast("录制中…");
    }

    private void StopRecord()
    {
        _recorder.Uninstall();
        IsRecording = false;
        // 补录仍按住的键为「抬起」：停止瞬间的物理抬起已收不到（钩子已卸载），否则宏播放时按键卡死
        foreach (var code in _recordingDownKeys)
            AddAction(new MacroAction { Code = code, Press = false });
        _recordingDownKeys.Clear();
        if (_editingMacro != null) MarkMacroDirty();
    }

    /// <summary>插入键盘按键：进入「请按键…」捕获态，下一个键盘按键（不含 Esc）插入为「按下」动作。</summary>
    public void InsertKey()
    {
        if (_editingMacro == null) return;
        if (IsRecording)
        {
            ShowToast("录制中无法插入按键，请先停止录制");
            return;
        }
        IsCapturingMouse = false;
        IsCapturingKey = true;
        _recorder.Install();
    }

    /// <summary>插入鼠标按键：进入「请按键…」捕获态，下一个鼠标按键（不含 Esc）插入为「按下」动作。</summary>
    public void InsertMouse()
    {
        if (_editingMacro == null) return;
        if (IsRecording)
        {
            ShowToast("录制中无法插入鼠标按键，请先停止录制");
            return;
        }
        IsCapturingKey = false;
        IsCapturingMouse = true;
        _recorder.Install();
    }

    /// <summary>离开快捷指令页时兜底卸载钩子（防导航后常驻）。</summary>
    public void CancelCapture()
    {
        _recorder.Uninstall();
        IsRecording = false;
        IsCapturingKey = false;
        IsCapturingMouse = false;
        _recordingDownKeys.Clear();
    }

    private void OnKeyEvent(byte hid, bool down)
    {
        // 任一「插入」捕获态下 Esc（HID 41）都表示取消；录制态不再用 Esc 结束，Esc 作为普通按键入列
        if (hid == 41 && (IsCapturingKey || IsCapturingMouse))
        {
            IsCapturingKey = false;
            IsCapturingMouse = false;
            _recorder.Uninstall();
            return;
        }
        if (IsCapturingKey && down)
        {
            IsCapturingKey = false;
            _recorder.Uninstall();
            AddAction(new MacroAction { Code = hid, Press = true });
            return;
        }
        // 鼠标捕获态下忽略键盘按键（Esc 已在上面处理），继续等待鼠标点击
        if (IsCapturingMouse && !IsRecording) return;
        if (!IsRecording && !IsCapturingKey) return;
        // 录制：按下/抬起都入列；相邻事件间隔 → 前一个动作的延迟（设备实际，10..635ms 按 5 取整）
        // 达步数上限后，下一个「按下」自动停止录制（防无限追加）；抬起仍正常入列
        if (down && EditingActions.Count >= MaxMacroActions)
        {
            StopRecord();
            NotifyMacroCap();
            return;
        }
        if (down) _recordingDownKeys.Add(hid);
        else _recordingDownKeys.Remove(hid);
        var now = DateTime.Now;
        int delay = _lastKeyTime == default
            ? 0
            : MacroActionItem.NormalizeDelay((int)(now - _lastKeyTime).TotalMilliseconds);
        AddAction(new MacroAction { Code = hid, Press = down, DelayMs = delay });
        _lastKeyTime = now;
    }

    private void OnMouseEvent(byte code, bool down)
    {
        // 鼠标捕获态：下一个鼠标按键（不含 Esc，Esc 已在 OnKeyEvent 处理）插入为「按下」动作
        if (IsCapturingMouse && down)
        {
            IsCapturingMouse = false;
            _recorder.Uninstall();
            AddAction(new MacroAction { Code = code, Press = true });
            return;
        }
        // 键盘捕获态下忽略鼠标按键，继续等待键盘
        if (IsCapturingKey && !IsRecording) return;
        if (!IsRecording && !IsCapturingMouse) return;
        // 录制：鼠标按下/抬起都入列（与键盘同一套延迟与步数上限逻辑）
        if (down && EditingActions.Count >= MaxMacroActions)
        {
            StopRecord();
            NotifyMacroCap();
            return;
        }
        if (down) _recordingDownKeys.Add(code);
        else _recordingDownKeys.Remove(code);
        var now = DateTime.Now;
        int delay = _lastKeyTime == default
            ? 0
            : MacroActionItem.NormalizeDelay((int)(now - _lastKeyTime).TotalMilliseconds);
        AddAction(new MacroAction { Code = code, Press = down, DelayMs = delay });
        _lastKeyTime = now;
    }

    // ---- 宏保存：仅本地持久化，不写设备 ----
    // 官方驱动同样不支持独立宏写入（无 0x09 直写入口）；宏经「改键设置」把按键绑定为宏
    // （0x08 entry[0]=0x12、entry[2]=槽位）后由设备按绑定关系生效。故宏页绝不 WriteFeature
    // 写设备、也不要求连接设备；「保存」= 落本地 macros.json + 分配槽位。

    private void ScheduleMacroSave()
    {
        // 标记「未保存」：由用户点「保存」按钮落本地；关闭编辑器时据此二次确认（防丢输入）。
        if (_editingMacro == null) return;
        SaveStatusText = "未保存";
    }

    /// <summary>手动保存入口（「保存」按钮调用）：仅本地持久化宏数据（不写设备）。
    /// 宏生效依赖改键页将按键绑定为宏（0x08 引用槽位），本方法不触碰设备。</summary>
    public void SaveMacro() => SaveMacros();

    /// <summary>深拷贝宏配置（快照/回滚用：动作对象全部分离，后续编辑不污染快照）。</summary>
    private static MacroConfig CloneConfig(MacroConfig src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        Method = src.Method,
        LoopCount = src.LoopCount,
        Actions = src.Actions.Select(a => new MacroAction { Code = a.Code, Press = a.Press, DelayMs = a.DelayMs }).ToList(),
    };

    /// <summary>丢弃未保存修改：把正在编辑的宏回滚到上次保存的状态（进入编辑时的快照，保存后为最近一次保存）。
    /// 已保存宏：改名/播放方式/循环次数/动作序列（含延迟）全部回滚；新建草稿：直接随关闭丢弃（不落列表）。
    /// 由关闭编辑器选「丢弃」或切页确认选「丢弃」调用。</summary>
    public void DiscardMacro()
    {
        if (_editingMacro == null || _editingSnapshot == null) return;
        var m = _editingMacro;
        m.Name = _editingSnapshot.Name; // 写回 Config.Name 并通知列表刷新
        m.Config.Method = _editingSnapshot.Method;
        m.Config.LoopCount = _editingSnapshot.LoopCount;
        m.Config.Actions = _editingSnapshot.Actions
            .Select(a => new MacroAction { Code = a.Code, Press = a.Press, DelayMs = a.DelayMs }).ToList();
        m.NotifySummaryChanged();
        ShowToast("已丢弃未保存的修改");
    }

    /// <summary>本地持久化：本地数据库保存宏内容（动作/延迟/循环），分配槽号。不写设备。</summary>
    private void SaveMacros()
    {
        var m = _editingMacro;
        if (m == null) return;

        m.Config.Actions = EditingActions.Select(a => a.Action).ToList();

        // 草稿（新建未保存）首次保存：分配空闲槽（设备上限 6）并纳入列表集合，列表才显示
        if (!Macros.Any(x => x == m))
        {
            if (m.Id == 0) m.Id = NextFreeSlot();
            if (m.Id == 0)
            {
                SaveStatusText = "保存失败";
                ShowToast("设备最多支持 6 个快捷指令，请先删除一个");
                return;
            }
            m.Config.Id = m.Id;
            m.BoundCheck = () => IsMacroBound(m.Id);
            Macros.Add(m);
            NotifyMacroListChanged();
        }

        PersistMacros();
        SaveStatusText = "";   // 清空未保存标记（反馈用 Toast，不常驻"正在保存"）
        _editingSnapshot = CloneConfig(m.Config); // 保存成功 → 丢弃回滚目标刷新为本次保存的状态
        ShowToast("已保存到本机，在「按键设置」绑定到按键后即可生效");
    }

    /// <summary>设置宏命令（参数 = 宏 ID）。</summary>
    public RelayCommand SetMacroCmd { get; }
    public RelayCommand NewMacroCmd { get; }
    public RelayCommand SelectMacroCmd { get; }
    public RelayCommand CopyMacroCmd { get; }
    public RelayCommand DeleteMacroCmd { get; }
    public RelayCommand SaveMacroCmd { get; }
    /// <summary>退出宏编辑视图（返回列表视图）。</summary>
    public RelayCommand CloseMacroCmd { get; }
    public RelayCommand ImportMacroCmd { get; }
    public RelayCommand ExportMacroCmd { get; }
    public RelayCommand RecordToggleCmd { get; }
    public RelayCommand InsertKeyCmd { get; }
    public RelayCommand InsertMouseCmd { get; }
    public RelayCommand ClearActionsCmd { get; }

    /// <summary>选中按钮命令（参数 = 按钮 Index）。</summary>
    public RelayCommand SelectButtonCmd { get; }
    /// <summary>设置选中按钮功能命令（参数 = 功能码）。</summary>
    public RelayCommand SetFuncCmd { get; }
    /// <summary>鼠标进入按键标签时联动对应图标高亮（参数 = 按钮 Index）。</summary>
    public RelayCommand HoverButtonCmd { get; }
    /// <summary>鼠标离开按键标签时取消对应图标高亮（参数 = 按钮 Index）。</summary>
    public RelayCommand UnhoverButtonCmd { get; }

    // ============ 左键改键风险确认（独立模态窗口，覆盖含标题栏的整个主窗口） ============
    // 仅左键（EntryIndex==0）点击标签即弹，未选功能前先提示（见 SelectButton / ConfirmLeftBtnChange）；
    // 弹窗显示/关闭由 MainWindow.UpdateLeftBtnConfirm 监听本属性驱动。
    private bool _leftBtnConfirmVisible;
    /// <summary>左键风险确认弹窗是否可见（独立模态窗口，MainWindow 监听本属性开合）。</summary>
    public bool LeftBtnConfirmVisible
    {
        get => _leftBtnConfirmVisible;
        set => SetProperty(ref _leftBtnConfirmVisible, value);
    }
    private string _leftBtnConfirmText = "";
    /// <summary>左键确认弹窗正文（仅风险说明；动作名由弹窗标题「修改左键」承载）。必须走 SetProperty：
    /// 绑定收不到变更通知时赋值后界面永不更新（历史事故：文案不显示）。</summary>
    public string LeftBtnConfirmText
    {
        get => _leftBtnConfirmText;
        private set => SetProperty(ref _leftBtnConfirmText, value);
    }
    /// <summary>「我知道了」→ 真正执行改键；「取消」→ 丢弃。</summary>
    public RelayCommand LeftBtnConfirmOkCmd { get; }
    /// <summary>「取消」→ 放弃本次改动。</summary>
    public RelayCommand LeftBtnConfirmCancelCmd { get; }

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
            // 标签坐标已实机拖动精调定稿（300×506 系，Coord 见 IconMarkers 已校准物理中心）。
            ("左键",   0, true,  "左键", 127.02, 145.99),
            ("右键",   1, true,  "右键", 188.05, 166.48),
            ("中键",   4, true,  "中键", 197.23, 71.17),   // 中键点击 + 上下滚（顶部中部）
            ("上滚",   16, false,"上滚", 246.16, 38.10),
            ("下滚",   17, false,"下滚", 248.28, 111.03),
            ("DPI 键", 5, false, "DPI", 50.97, 183.38),
            ("前进",   2, false, "前进", -36.10, 105.10),
            ("后退",   3, false, "后退", -50.03, 162.33),
            ("左滚",   14,false, "左滚", -55.88, 247.28),
            ("右滚",   15,false, "右滚", 0.84, 247.99),
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
        // 标签坐标已写死至下方 map 默认值（实机精调定稿），调试拖动/读文件逻辑保留但不启用。
        // LoadTagPositions();
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
            foreach (var m in IconMarkers) m.IsSelected = false;
            SelectedButton = null;
            return;
        }
        // 仅左键（index==0）：点标签先弹风险确认（独立模态窗口），**确认前不建立选中态、不展开右栏**；
        // 点「我知道了」才 SelectButtonCore(0)，点「取消」放弃（见 ConfirmLeftBtnChange / CancelLeftBtnChange）。
        if (index == 0)
        {
            LeftBtnConfirmText = LeftBtnConfirmBody;
            LeftBtnConfirmVisible = true;
            return;
        }
        SelectButtonCore(index);
    }

    /// <summary>建立按钮选中态并展开右侧分配功能面板（普通键点击即调；左键在确认弹窗点「我知道了」后调用）。</summary>
    private void SelectButtonCore(int index)
    {
        for (int i = 0; i < Buttons.Count; i++) Buttons[i].IsSelected = (i == index);
        SelectedButton = Buttons[index];
        // 联动高亮：仅选中按钮对应的位置图标点亮
        var selKey = Buttons[index].IconKey;
        foreach (var m in IconMarkers) m.IsSelected = (m.IconKey == selKey);
        BuildFuncOptions(BtnCfg.Entries[Buttons[index].EntryIndex][0]);
        // 若该按钮已绑定宏，宏列表勾选同步到该按钮的宏 ID（否则保持上次选择）
        var entry = BtnCfg.Entries[Buttons[index].EntryIndex];
        if (entry[0] == ButtonConfig.FuncCode.Macro && entry[2] >= 1 && entry[2] <= 6)
            SelectedMacroId = entry[2];
        else
            ClearMacroSelection(); // 未绑定宏的按钮：清空勾选，宏 Tab 不做任何预选（用户反馈的 MX1 假选中）
    }

    /// <summary>左键确认弹窗「取消」→ 放弃本次切换，回到**无选中态**（分配功能面板关闭）。
    /// 点击左键标签 = 尝试切换到左键（确认前不建立选中态，见 SelectButton）；取消即放弃，
    /// 原选中态一并清除——用户规格：改右键时点左键→取消，面板应关闭（初版仅在左键已选中时
    /// 才清除，改右键场景会残留右键选中态）。</summary>
    private void CancelLeftBtnChange()
    {
        LeftBtnConfirmVisible = false;
        for (int i = 0; i < Buttons.Count; i++) Buttons[i].IsSelected = false;
        foreach (var m in IconMarkers) m.IsSelected = false;
        SelectedButton = null;
        // 点击左键标签时 RadioButton 组互斥/用户点击在目标侧残留的勾选态（OneWay 不回写源），
        // 强制重推覆盖为「全未选中」，与 VM 一致。
        ReassertTagChecks();
    }

    /// <summary>强制重推各标签勾选态：先置反再置回（两次变更通知），让 OneWay 绑定覆盖
    /// RadioButton 组互斥/用户点击造成的目标侧残留（仅取消路径用于状态恢复）。</summary>
    private void ReassertTagChecks()
    {
        for (int i = 0; i < Buttons.Count; i++)
        {
            Buttons[i].IsSelected = !Buttons[i].IsSelected;
            Buttons[i].IsSelected = !Buttons[i].IsSelected;
        }
    }

    // ============ 按键标签 hover → 图标联动高亮 ============
    // 关联键：ButtonItem.IconKey ↔ IconMarker.IconKey（见 ADR buttons-hover-link）。
    // 选中态 IsSelected 优先级高于 hover 态 IsHovered：选中后即使移开鼠标仍保持高亮。
    private void SetButtonHover(int buttonIndex, bool hovered)
    {
        var btn = Buttons.FirstOrDefault(b => b.Index == buttonIndex);
        if (btn == null) return;
        foreach (var m in IconMarkers)
            m.IsHovered = hovered && m.IconKey == btn.IconKey;
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
        // 列表重建后通知 ListView 的 SelectedValue 重新匹配当前高亮项
        OnPropertyChanged(nameof(SelectedButtonFuncCode));
    }

    /// <summary>设置选中按钮的功能（克隆全表 → 改目标 entry → 防抖整表写）。
    /// 左键风险确认已前移到点击标签时（见 SelectButton），此处直接改键、不再弹窗。</summary>
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
        if (ApplyEntryChange(btn, code, param))
        {
            BuildFuncOptions(code);
            // 按钮功能改为非宏后清空宏勾选：否则切到宏 Tab 时仍会残留上次选中的宏 ID（假选中）
            if (code != ButtonConfig.FuncCode.Macro) ClearMacroSelection();
        }
    }

    /// <summary>左键确认弹窗「我知道了」→ 关闭弹窗并建立左键选中态、展开右栏（改键在选功能时执行）。</summary>
    private void ConfirmLeftBtnChange()
    {
        LeftBtnConfirmVisible = false;
        SelectButtonCore(0);
    }

    /// <summary>左键改键风险说明（文案既定，不润色）。</summary>
    private const string LeftBtnConfirmBody = "当前只有一个左键，改了后可能无法点击";

    /// <summary>改目标 entry（全表副本 → 改一项 → 防抖整表写）。**无变化**（同功能码/同宏 ID）时
    /// 不置待保存、不触发保存、不刷新 UI，返回 false——用户规格：点当前已选功能不应触发保存。</summary>
    private bool ApplyEntryChange(ButtonItem btn, byte code, byte param)
    {
        var cur = BtnCfg.Entries[btn.EntryIndex];
        if (cur[0] == code && cur[1] == 0x00 && cur[2] == param) return false;
        BtnCfg.Entries[btn.EntryIndex] = new byte[] { code, 0x00, param };
        btn.FunctionName = ButtonFunc.NameOf(code);
        SaveStatusText = "待保存…";
        _buttonSaveDebounce?.Change(AutoSaveDelayMs, System.Threading.Timeout.Infinite);
        NotifyMacroBoundChanged(); // 绑定关系变化 → 宏页未绑定警告同步
        return true;
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
        _armResetTimer?.Stop();
        _recorder.Dispose();
        _toastTimer?.Stop();
    }

    #endregion
}
