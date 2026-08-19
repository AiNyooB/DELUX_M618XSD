using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DeluxDriver;

/// <summary>
/// 主窗口：原生 Window + 侧边导航 + 顶部标题栏。
/// 纯视图层：连接状态/导航状态来自 AppViewModel；业务逻辑在后续阶段页面中实现。
/// </summary>
public partial class MainWindow : Window, INavigationService
{
    private readonly AppViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        _vm = App.Services.GetRequiredService<AppViewModel>();
        _vm.Navigation = this;
        DataContext = _vm;

        BuildNavigation();
        Navigate("Connect");

        // 打开软件即自动识别鼠标（无需用户点击）。
        _vm.AutoConnect();

        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "startup.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] MainWindow ctor 完成（导航到 {CurrentPageKey}）\n");
        }
        catch { }
    }

    /// <summary>页 key → 页面实例工厂（原型 §0.2 划分：改键/快捷指令/DPI/性能/参数/配置/其他）。</summary>
    private System.Collections.Generic.Dictionary<string, Func<Page>> Pages { get; } = new()
    {
        ["Connect"] = () => new ConnectPage(),    // 连接设备等待页（原型 §1）
        ["Buttons"] = () => new ButtonsPage(),   // 改键设置
        ["Macro"] = () => new MacroPage(),       // 快捷指令
        ["Dpi"] = () => new DpiPage(),           // DPI 设置
        ["Perf"] = () => new PerfPage(),         // 性能设置（回报率 + 去抖）
        ["Params"] = () => new ParamsPage(),     // 参数设置（灯光 + 电源）
        ["Profile"] = () => new ProfilePage(),   // 配置管理
        ["Other"] = () => new OtherPage(),       // 其他设置（帮助与反馈）
    };

    private void BuildNavigation()
    {
        // 导航项已在 XAML 中以 ListBoxItem 声明，此处仅确保初始高亮。
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 始终订阅主题变更：即便首次 Apply 因瞬态原因失败，后续切换主题时也会重试应用材质。
        _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyBackdrop();
    }

    /// <summary>应用 DWM Mica 背景材质；不支持或失败时窗口保持纯色背景（由 XAML 的 AppBackgroundBrush 提供）。</summary>
    private void ApplyBackdrop()
    {
        if (DwmBackdrop.Apply(this, DwmBackdrop.BackdropType.Mica, _vm.IsDark))
            Background = Brushes.Transparent;
    }

    private LeftButtonConfirmWindow? _leftBtnConfirmWindow;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 浅/深主题切换时，同步 DWM 材质着色与标题栏明暗，保证与自研主题字典一致。
        if (e.PropertyName == nameof(AppViewModel.IsDark))
            ApplyBackdrop();
        // 左键风险确认弹窗：VM 置可见 → 打开独立模态窗口；置不可见 → 关闭。
        if (e.PropertyName == nameof(AppViewModel.LeftBtnConfirmVisible))
            UpdateLeftBtnConfirm();
    }

    /// <summary>同步左键风险确认弹窗的显示/隐藏。
    /// 用独立模态窗口（WindowStyle=None + Owner，覆盖主窗口外框含标题栏）替代内容内遮罩——
    /// 内容树元素盖不住系统标题栏，模态 ShowDialog 同时获得「主窗口禁用 + Esc/Enter + 焦点管理」。
    /// 注意重入：SelectButton 置 Visible=true → 此处 ShowDialog 进入嵌套消息循环；
    /// 用户点按钮 → 命令把 Visible 置 false → 此处 Close → ShowDialog 返回 → SelectButton 继续。</summary>
    private void UpdateLeftBtnConfirm()
    {
        if (_vm.LeftBtnConfirmVisible)
        {
            if (_leftBtnConfirmWindow != null) return; // 已显示
            var w = new LeftButtonConfirmWindow
            {
                Owner = this,
                DataContext = _vm,
            };
            // 覆盖整个主窗口外框（含标题栏）；模态期间 Owner 被禁用，位置不会漂移
            w.Left = Left;
            w.Top = Top;
            w.Width = ActualWidth;
            w.Height = ActualHeight;
            // 弹窗被外部关闭（如 Alt+F4）时同步 VM 状态，避免 Visible 残留导致下次无法打开
            w.Closed += (_, _) =>
            {
                _leftBtnConfirmWindow = null;
                if (_vm.LeftBtnConfirmVisible)
                    _vm.LeftBtnConfirmVisible = false;
            };
            _leftBtnConfirmWindow = w;
            w.ShowDialog();
        }
        else if (_leftBtnConfirmWindow is { } win)
        {
            win.Close();
            _leftBtnConfirmWindow = null;
        }
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is ListBoxItem item && item.Tag is string tag)
            Navigate(tag);
    }

    #region INavigationService

    public string CurrentPageKey { get; private set; } = "Buttons";

    public void Navigate(string pageKey)
    {
        if (!Pages.TryGetValue(pageKey, out var factory)) return;
        // 离开快捷指令页时若有未保存宏编辑，先二次确认（AGENTS.md 3.4：切页前确认，不丢用户输入）。
        // 快捷指令页是手动保存页（SaveStatusText=="未保存" 即存在未落本地修改），其余页为自动保存不拦截。
        if (CurrentPageKey == "Macro" && _vm.SaveStatusText == "未保存")
        {
            var r = MessageBox.Show(this,
                "你有未保存的快捷指令，确定要离开此页面吗？",
                "未保存的修改",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (r == MessageBoxResult.Cancel) return;          // 留在当前页
            if (r == MessageBoxResult.Yes) _vm.SaveMacro();    // 保存并离开
            else _vm.DiscardMacro();                           // 否：丢弃并回滚后离开
        }
        CurrentPageKey = pageKey;
        _vm.CurrentPageKey = pageKey;

        // 两态布局切换（原型 §1）：等待页无左栏，设备页带左栏。
        // 注意：Frame 宿主的 Page 不会继承 Window 的 DataContext（Frame 隔离内容树），
        // 故所有页面在导航时统一注入 AppViewModel，否则页面内 {Binding ...} 全部绑到 null。
        if (pageKey == "Connect")
        {
            DeviceGrid.Visibility = Visibility.Collapsed;
            ConnectFrame.Navigated -= ConnectFrame_Navigated;
            var connectPage = factory();
            connectPage.DataContext = _vm;
            ConnectFrame.Navigate(connectPage);
            ConnectFrame.Navigated += ConnectFrame_Navigated;
        }
        else
        {
            ConnectFrame.Visibility = Visibility.Collapsed;
            ConnectFrame.Content = null;
            DeviceGrid.Visibility = Visibility.Visible;
            DeviceFrame.Navigated -= DeviceFrame_Navigated;
            var page = factory();
            page.DataContext = _vm;
            DeviceFrame.Navigate(page);
            DeviceFrame.Navigated += DeviceFrame_Navigated;

            // 同步侧边栏高亮。
            foreach (var mi in Nav.Items.OfType<ListBoxItem>())
                mi.IsSelected = mi.Tag is string t && t == CurrentPageKey;
        }
    }

    private void ConnectFrame_Navigated(object? sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        // 等待页无导航高亮
    }

    private void DeviceFrame_Navigated(object? sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        foreach (var mi in Nav.Items.OfType<ListBoxItem>())
            if (mi.Tag is string t) mi.IsSelected = t == CurrentPageKey;
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            // 先停定时器，避免关闭后 Timer 回调还在跑
            _vm.Dispose();

            var hid = App.Services.GetRequiredService<HidComm>();
            hid.StopInputListener();
            hid.Dispose();
            base.OnClosed(e);
        }
        finally
        {
            // 安全网：确保进程退出（防止 Dispatcher.Invoke 等残留回调阻塞；即便清理抛异常也必达）
            Environment.Exit(0);
        }
    }
}
