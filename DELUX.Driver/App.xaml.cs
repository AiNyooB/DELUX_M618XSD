using System;
using System.IO;
using System.Windows;

namespace DeluxDriver;

/// <summary>
/// 应用入口：初始化原生 WPF 主题、最小 DI 容器、未处理异常崩溃日志。
/// </summary>
public partial class App : Application
{
    /// <summary>最小 DI 容器（手写，避免引入 Microsoft.Extensions.Hosting 之外更多依赖）。</summary>
    public static ServiceContainer Services { get; } = new();

    /// <summary>
    /// 原生 WPF 主题：切换浅色/深色资源字典（替换 App.Resources 中第一项主题字典）。
    /// </summary>
    public static void ApplyTheme(bool isDark)
    {
        const string light = "Themes/LightTheme.xaml";
        const string dark = "Themes/DarkTheme.xaml";
        var dict = new ResourceDictionary
        {
            Source = new Uri(isDark ? dark : light, UriKind.Relative)
        };

        // 移除旧的浅/深主题字典（保留 Styles.xaml 与转换器）。
        for (int i = Current.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var src = Current.Resources.MergedDictionaries[i].Source?.ToString();
            if (src == light || src == dark)
                Current.Resources.MergedDictionaries.RemoveAt(i);
        }
        Current.Resources.MergedDictionaries.Insert(0, dict);
    }

    public App()
    {
        // 背景材质由自研 DwmBackdrop 全权接管（明暗跟随应用自研主题），关闭 WPF 内置
        // WindowBackdropManager（其明暗跟随系统主题，与自研主题不一致）。
        AppContext.SetSwitch("Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop", true);

        InitializeComponent();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 未处理异常写本地崩溃日志（exe 同目录 crash.log），便于离线定位启动期崩溃。
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            try
            {
                var ex = ev.ExceptionObject as Exception;
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"),
                    $"AppDomain.UnhandledException @ {DateTime.Now}\n\n{ex?.ToString() ?? ev.ExceptionObject?.ToString() ?? "(null)"}");
            }
            catch { }
        };
        DispatcherUnhandledException += (s, ev) =>
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"),
                    $"DispatcherUnhandledException @ {DateTime.Now}\n\n{ev.Exception}");
            }
            catch { }
            // 不再吞掉异常：让窗口构造期异常留下 crash.log 而非静默无窗口。
            ev.Handled = false;
        };

        // 注册服务（Phase 1：通信层 + 主视图模型 + 主窗口）
        // 用工厂注册以表达依赖关系（AppViewModel 需要 HidComm 实例）。
        Services.AddSingleton(() => new HidComm());
        Services.AddSingleton(() => new AppViewModel(Services.GetRequiredService<HidComm>()));
        Services.AddSingleton<MainWindow>();

        // MainWindow 自身实现 INavigationService，构造后注册为导航服务实现。
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] OnStartup: 服务注册完成，准备创建 MainWindow\n");
        }
        catch { }

        var main = Services.GetRequiredService<MainWindow>();
        Services.AddSingleton<INavigationService>(main);

        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] OnStartup: MainWindow 已创建，准备 Show\n");
        }
        catch { }

        // 应用启动期主题：唯一事实来源为 AppViewModel.IsDark（其已加载持久化用户选择或系统偏好）。
        try
        {
            var vm = Services.GetRequiredService<AppViewModel>();
            ApplyTheme(vm.IsDark);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"),
                $"ApplyTheme @ {DateTime.Now}\n\n{ex}");
        }

        main.Show();
        main.Activate();

        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] OnStartup: MainWindow.Show 已调用\n");
        }
        catch { }
    }
}
