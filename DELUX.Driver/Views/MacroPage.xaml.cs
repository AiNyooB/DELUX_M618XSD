using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeluxDriver;

public partial class MacroPage : Page
{
    public MacroPage()
    {
        InitializeComponent();
        // 离开页面兜底卸载键盘钩子（防导航后常驻录制/捕获态）。
        Unloaded += (_, _) =>
        {
            var vm = DataContext as AppViewModel;
            vm?.CancelCapture();
            vm?.CloseMacro();
        };
    }

    private AppViewModel Vm => (AppViewModel)DataContext;

    /// <summary>行内按钮的 DataContext 即对应 MacroActionItem，取其在编辑器集合中的下标。</summary>
    private static int IndexOf(object sender, AppViewModel vm)
        => vm.EditingActions.IndexOf((AppViewModel.MacroActionItem)((FrameworkElement)sender).DataContext);

    private void Row_MouseUp(object sender, MouseButtonEventArgs e)
    {
        int i = IndexOf(sender, Vm);
        if (i >= 0) Vm.SelectAction(i);
    }

    /// <summary>ListView 单选切换 → 若当前有未保存修改，先确认（保存/丢弃/取消）。
    /// 左栏现在常驻可交互，故切宏不再禁用左栏；顶部「同 id」判断同时兜住 SelectMacro 回写导致的递归触发。</summary>
    private void MacroList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MacroList.SelectedItem is not AppViewModel.MacroItem target) return;
        if (Vm.EditingMacro != null && target.Id == Vm.EditingMacro.Id) return; // 无变化或回写递归
        if (Vm.SaveStatusText == "未保存" && Vm.EditingMacro != null)
        {
            var r = MessageBox.Show(
                "你有未保存的快捷指令，切换会丢失未保存的修改。是否先保存？",
                "未保存的修改",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (r == MessageBoxResult.Cancel)
            {
                MacroList.SelectedValue = Vm.EditingMacro?.Id ?? 0; // 回退选择，留在当前编辑
                return;
            }
            if (r == MessageBoxResult.Yes) Vm.SaveMacro();
            else Vm.DiscardMacro();
        }
        Vm.SelectMacroCmd.Execute(target.Id);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e) => Vm.TogglePress(IndexOf(sender, Vm));
    private void Delete_Click(object sender, RoutedEventArgs e) => Vm.DeleteAction(IndexOf(sender, Vm));

    /// <summary>键值捕获框：点击进入「等待按键」态，下一个按键/鼠标点击回写该动作（替代手输键名）。</summary>
    private void CaptureKey_Click(object sender, RoutedEventArgs e) => Vm.CaptureKeyEdit(IndexOf(sender, Vm));

    /// <summary>新建快捷指令：左栏常驻后「＋新建」在编辑中也可点，故先就未保存修改确认，再交由 VM 建草稿。</summary>
    private void NewMacro_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SaveStatusText == "未保存" && Vm.EditingMacro != null)
        {
            var r = MessageBox.Show(
                "你有未保存的快捷指令，新建会丢失未保存的修改。是否先保存？",
                "未保存的修改",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) Vm.SaveMacro();
            else Vm.DiscardMacro();
        }
        Vm.NewMacro();
    }

    /// <summary>延迟/循环次数输入框仅允许数字（防非法文本导致绑定红框且无反馈）。</summary>
    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsDigit);

    /// <summary>「✕ 关闭」：若有未保存修改，二次确认（是=保存并关闭 / 否=不保存直接关闭 / 取消=留在编辑态）。</summary>
    private void CloseMacro_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SaveStatusText == "未保存")
        {
            var result = MessageBox.Show(
                "你有未保存的快捷指令，确定要关闭吗？",
                "未保存的修改",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return;   // 留在编辑态
            if (result == MessageBoxResult.Yes) Vm.SaveMacro(); // 保存并关闭
            else Vm.DiscardMacro();                          // 否：丢弃并回滚到上次保存
        }
        Vm.CloseMacro();
    }

    /// <summary>名称框与右侧操作按钮组常驻共存（不再因聚焦改名而互藏），见 XAML。</summary>
}
