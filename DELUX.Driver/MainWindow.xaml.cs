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

        // Win11 22H2+ 应用 DWM Mica 背景材质（明暗跟随自研主题）；其余系统静默保持纯色。
        if (DwmBackdrop.Apply(this, DwmBackdrop.BackdropType.Mica, _vm.IsDark))
        {
            // 材质生效后窗口背景须透明才能透出（DwmBackdrop 已设 CompositionTarget 透明）。
            Background = Brushes.Transparent;
            _vm.PropertyChanged += OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 浅/深主题切换时，同步 DWM 材质着色与标题栏明暗，保证与自研主题字典一致。
        if (e.PropertyName == nameof(AppViewModel.IsDark))
            DwmBackdrop.Apply(this, DwmBackdrop.BackdropType.Mica, _vm.IsDark);
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
        CurrentPageKey = pageKey;
        _vm.CurrentPageKey = pageKey;

        // 两态布局切换（原型 §1）：等待页无左栏，设备页带左栏。
        if (pageKey == "Connect")
        {
            DeviceGrid.Visibility = Visibility.Collapsed;
            ConnectFrame.Navigated -= ConnectFrame_Navigated;
            ConnectFrame.Navigate(factory());
            ConnectFrame.Navigated += ConnectFrame_Navigated;
        }
        else
        {
            ConnectFrame.Visibility = Visibility.Collapsed;
            ConnectFrame.Content = null;
            DeviceGrid.Visibility = Visibility.Visible;
            DeviceFrame.Navigated -= DeviceFrame_Navigated;
            DeviceFrame.Navigate(factory());
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
