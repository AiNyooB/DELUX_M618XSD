using System.Windows.Controls;

namespace DeluxDriver;

/// <summary>
/// 连接设备等待页（原型 §1）：未连接时展示引导空态；连接成功由 AppViewModel 自动跳转改键设置。
/// 纯视图：连接状态/失败原因来自 AppViewModel。
/// </summary>
public partial class ConnectPage : Page
{
    public ConnectPage() => InitializeComponent();
    // DataContext 由 MainWindow.Navigate 统一注入（Frame 不继承 Window 的 DataContext）。
}
