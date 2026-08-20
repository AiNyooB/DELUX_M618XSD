using System.Windows.Controls;

namespace DeluxDriver;

/// <summary>
/// 本地配置页：多套设备配置快照（槽位）的管理与切换。
/// 纯视图层 —— 业务编排（槽位增删、切换写设备、回滚）在 AppViewModel（AGENTS 六）。
/// 设备无配置槽位，Profile 为纯软件层（AGENTS.md §3.6）。
/// </summary>
public partial class LocalConfigPage : Page
{
    public LocalConfigPage() => InitializeComponent();
}