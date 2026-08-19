using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

    /// <summary>ListView 单选切换 → 驱动 VM 选中（复用现有 SelectMacro，会同步 IsSelected 与编辑器）。</summary>
    private void MacroList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MacroList.SelectedItem is AppViewModel.MacroItem m)
            Vm.SelectMacroCmd.Execute(m.Id);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e) => Vm.TogglePress(IndexOf(sender, Vm));
    private void Delete_Click(object sender, RoutedEventArgs e) => Vm.DeleteAction(IndexOf(sender, Vm));

    /// <summary>延迟/循环次数输入框仅允许数字（防非法文本导致绑定红框且无反馈）。</summary>
    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(char.IsDigit);

    // ---- 动作行拖拽排序（精致版：半透明 ghost 跟随 + 原行占位透明 + 圆角插入线） ----

    private int _dragFrom = -1;
    private Border? _dragRow;
    private double _grabOffset;   // 指针相对行顶的偏移，使 ghost 居中于指针

    private void DragHandle_DragStarted(object sender, DragStartedEventArgs e)
    {
        var thumb = (Thumb)sender;
        _dragRow = FindVisualParent<Border>(thumb,
            b => b.DataContext is AppViewModel.MacroActionItem);
        if (_dragRow == null) return;
        _dragFrom = Vm.EditingActions.IndexOf((AppViewModel.MacroActionItem)_dragRow.DataContext);

        // 生成被拖行的半透明视觉副本作为 ghost
        var bmp = new RenderTargetBitmap(
            (int)_dragRow.ActualWidth, (int)_dragRow.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(_dragRow);
        DragGhostBorder.Width = _dragRow.ActualWidth;
        DragGhostBorder.Height = _dragRow.ActualHeight;
        DragGhostBorder.Background = new VisualBrush(_dragRow) { Opacity = 0.85 };
        DragGhostBorder.BorderBrush = (Brush)FindResource("ControlStrongStrokeColorDefaultBrush");
        DragGhostBorder.BorderThickness = new Thickness(1);

        _grabOffset = Mouse.GetPosition(_dragRow).Y;
        var ic = (ItemsControl)FindVisualParent<ItemsControl>(_dragRow);
        DragGhost.PlacementTarget = ic;
        DragGhost.HorizontalOffset = _dragRow.TranslatePoint(new Point(0, 0), ic).X;
        UpdateGhost(ic);

        DragGhost.Visibility = Visibility.Visible;
        DropLine.Visibility = Visibility.Visible;
        _dragRow.Opacity = 0;   // 原行收为透明占位，列表不跳动
    }

    private void DragHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_dragRow == null) return;
        var ic = (ItemsControl)FindVisualParent<ItemsControl>(_dragRow);
        UpdateGhost(ic);
        PositionDropLine(ic, ComputeDropIndex(ic, Mouse.GetPosition(ic).Y));
    }

    private void DragHandle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_dragRow != null)
        {
            var ic = (ItemsControl)FindVisualParent<ItemsControl>(_dragRow);
            int to = ComputeDropIndex(ic, Mouse.GetPosition(ic).Y);
            Vm.MoveAction(_dragFrom, to);
            _dragRow.Opacity = 1;
        }
        DragGhost.Visibility = Visibility.Collapsed;
        DropLine.Visibility = Visibility.Collapsed;
        _dragFrom = -1;
        _dragRow = null;
    }

    /// <summary>把 ghost 垂直跟随到指针（水平锁定原行 X，垂直按指针 Y 减抓取偏移）。</summary>
    private void UpdateGhost(ItemsControl ic)
    {
        Point p = Mouse.GetPosition(ic);
        DragGhost.VerticalOffset = p.Y - _grabOffset;
    }

    /// <summary>根据鼠标 Y 坐标算出应插入的目标下标（落在某行上半部→插到该行前，否则该行后）。</summary>
    private static int ComputeDropIndex(ItemsControl ic, double y)
    {
        int idx = ic.Items.Count;
        for (int i = 0; i < ic.Items.Count; i++)
        {
            var container = (FrameworkElement)ic.ItemContainerGenerator.ContainerFromIndex(i);
            if (container == null) continue;
            double top = container.TranslatePoint(new Point(0, 0), ic).Y;
            double h = container.ActualHeight;
            if (y < top + h / 2) { idx = i; break; }
        }
        return idx;
    }

    private void PositionDropLine(ItemsControl ic, int to)
    {
        double y;
        if (to < ic.Items.Count)
        {
            var c = (FrameworkElement)ic.ItemContainerGenerator.ContainerFromIndex(to);
            y = c.TranslatePoint(new Point(0, 0), ic).Y - DropLine.Height / 2;
        }
        else if (ic.Items.Count > 0)
        {
            var c = (FrameworkElement)ic.ItemContainerGenerator.ContainerFromIndex(ic.Items.Count - 1);
            y = c.TranslatePoint(new Point(0, 0), ic).Y + c.ActualHeight - DropLine.Height / 2;
        }
        else return;
        DropLine.Margin = new Thickness(4, y, 4, 0);
    }

    private static T? FindVisualParent<T>(DependencyObject o, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        var p = VisualTreeHelper.GetParent(o);
        while (p != null)
        {
            if (p is T t && (predicate == null || predicate(t))) return t;
            p = VisualTreeHelper.GetParent(p);
        }
        return null;
    }

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

    /// <summary>名称框获得焦点：隐藏右侧操作按钮组，并让名称框跨占其列以拉满整行（编辑时增宽）。</summary>
    private void MacroName_GotFocus(object sender, RoutedEventArgs e)
    {
        if (FindName("MacroActionBar") is FrameworkElement bar)
            bar.Visibility = Visibility.Collapsed;
        if (FindName("MacroNameBox") is FrameworkElement box)
            Grid.SetColumnSpan(box, 2);
    }

    /// <summary>名称框失去焦点：恢复右侧操作按钮组与名称框列宽。</summary>
    private void MacroName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (FindName("MacroActionBar") is FrameworkElement bar)
            bar.Visibility = Visibility.Visible;
        if (FindName("MacroNameBox") is FrameworkElement box)
            Grid.SetColumnSpan(box, 1);
    }
}
