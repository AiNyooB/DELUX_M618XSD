using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace MouseDriverClient;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // 把未处理异常（含内部异常，ex.ToString 会递归展开）写到 exe 旁边，
        // 便于离线定位启动期崩溃（Dispatcher 尚未就绪时 MessageBox 可能不弹）。
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
                var ex = e.ExceptionObject as Exception;
                File.WriteAllText(path, $"AppDomain.UnhandledException @ {System.DateTime.Now}\n\n{(ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "(null)")}");
            }
            catch { }
            if (e.ExceptionObject is Exception ex2)
                MessageBox.Show($"严重异常：{ex2.Message}\n\n{ex2.StackTrace}", "MouseDriverClient 崩溃", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        DispatcherUnhandledException += (s, e) =>
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.WriteAllText(path, $"DispatcherUnhandledException @ {System.DateTime.Now}\n\n{e.Exception}");
            }
            catch { }
            MessageBox.Show($"未处理异常：{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                            "MouseDriverClient 崩溃", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
