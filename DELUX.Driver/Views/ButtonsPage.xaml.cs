using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeluxDriver
{
    /// <summary>
    /// 改键设置页。
    /// </summary>
    public partial class ButtonsPage : Page
    {
        private AppViewModel.ButtonItem? _dragItem;
        private FrameworkElement? _dragContainer;
        private Point _dragStart;
        private double _dragOrigX;
        private double _dragOrigY;
        private bool _dragActive;

        public ButtonsPage()
        {
            InitializeComponent();
            // 鼠标图缩放跟随左栏可用区域（窗口尺寸变化时重算）；
            // 水平位置由 UpdateCanvasPosition 自适应计算（居中于整卡 / 居中于右栏左侧），见 XAML 注释。
            CardHost.SizeChanged += OnCardHostSizeChanged;
            // 页面 DataContext 由 MainWindow 导航时注入，注入后订阅选中态变化以重算水平位置
            DataContextChanged += OnPageDataContextChanged;
            // 标签拖动定位（调试用）：校准坐标时取消下方注释即可在实机拖动；
            // 发布版默认禁用，避免普通用户误拖标签改写键位标注位置并持久化。
            // TagsHost.PreviewMouseLeftButtonDown += OnTagMouseDown;
            // TagsHost.PreviewMouseMove += OnTagMouseMove;
            // TagsHost.PreviewMouseLeftButtonUp += OnTagMouseUp;
            // 鼠标悬停按键标签 → 对应图标联动高亮（见 ADR buttons-hover-link）
            TagsHost.PreviewMouseMove += OnTagsHostHover;
            TagsHost.MouseLeave += OnTagsHostLeave;
        }

        private void OnPageDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is AppViewModel oldVm) oldVm.PropertyChanged -= OnVmPropertyChanged;
            if (e.NewValue is AppViewModel vm)
            {
                vm.PropertyChanged += OnVmPropertyChanged;
                UpdateCanvasPosition();
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppViewModel.HasSelection))
                UpdateCanvasPosition();
        }

        // ── 标签 hover → 图标联动高亮 ───────────────────────────────────
        // 命中测试当前鼠标下的 RadioButton 项，变化时才发命令，避免每帧重复置位。
        private int _hoverIndex = -1;

        private void OnTagsHostHover(object sender, MouseEventArgs e)
        {
            var container = TagsHost.ContainerFromElement(e.OriginalSource as DependencyObject) as ContentPresenter;
            int idx = container?.DataContext is AppViewModel.ButtonItem b ? b.Index : -1;
            if (idx == _hoverIndex) return;
            if (DataContext is AppViewModel vm)
            {
                if (_hoverIndex >= 0) vm.UnhoverButtonCmd.Execute(_hoverIndex);
                if (idx >= 0) vm.HoverButtonCmd.Execute(idx);
            }
            _hoverIndex = idx;
        }

        private void OnTagsHostLeave(object sender, MouseEventArgs e)
        {
            if (_hoverIndex < 0 || DataContext is not AppViewModel vm) return;
            vm.UnhoverButtonCmd.Execute(_hoverIndex);
            _hoverIndex = -1;
        }

        // 「鼠标操作」Tab 的 ListView 单选 → 选中即应用功能（替代原 RadioButton 的 SetFuncCmd 互锁）。
        // SelectedValue 由 VM 单向驱动高亮；此处仅在用户交互选中新项时执行命令。
        private void OnFuncListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0 || DataContext is not AppViewModel vm) return;
            if (e.AddedItems[0] is AppViewModel.FuncOption opt)
                vm.SetFuncCmd.Execute(opt.Code);
        }

        private void OnCardHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 可用区域 = 左栏实际尺寸 − 外边距 16×2 − 底部状态行约 30px
            double availW = Math.Max(1, e.NewSize.Width - 32);
            double availH = Math.Max(1, e.NewSize.Height - 32 - 30);
            // 图片原始尺寸 300×506（已裁掉左右透明区），按宽高分别取最小缩放，完整显示
            double scale = Math.Min(availW / 300.0, availH / 506.0);
            MouseScale.ScaleX = scale;
            MouseScale.ScaleY = scale;
            UpdateCanvasPosition();
        }

        /// <summary>自适应水平定位：无右栏 → 居中于整卡；有右栏 → 居中于右栏（320px）左侧可用区。
        /// 替代原写死 Margin=70 的平移：内容区宽度变化（如侧边栏收窄）时，图片与右栏之间的空隙
        /// 不再被撑大（原实现 +40px 宽度全部变成缝隙）。</summary>
        private void UpdateCanvasPosition()
        {
            double scaledW = Math.Max(1, 300 * MouseScale.ScaleX);
            double cardW = Math.Max(1, CardHost.ActualWidth);
            bool selected = DataContext is AppViewModel vm && vm.HasSelection;
            double panelW = 320; // 右栏分配功能面板宽度（含左缘分隔线）
            double region = Math.Max(1, cardW - (selected ? panelW : 0));
            double left = Math.Max(0, (region - scaledW) / 2);
            MouseCanvas.HorizontalAlignment = HorizontalAlignment.Left;
            MouseCanvas.Margin = new Thickness(left, 0, 0, 0);
        }

        // ── 标签拖动定位 ──────────────────────────────────────────────
        // 位移超过 4px 判定为拖动（否则视为普通点击选中）；拖动期间不触发
        // RadioButton 的选中/命令，松手后把最终坐标写回 VM 并持久化。

        private void OnTagMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 用 ContainerFromElement 精确拿 Canvas 里的项容器——不能手动向上找
            // ContentPresenter（会命中 RadioButton 模板内部的 ContentPresenter，它不在
            // Canvas 中，Canvas.GetLeft 返回 NaN 导致标签跳到左上角）。
            var container = TagsHost.ContainerFromElement(e.OriginalSource as DependencyObject) as ContentPresenter;
            if (container?.DataContext is not AppViewModel.ButtonItem item) return;
            _dragItem = item;
            _dragContainer = container;
            _dragStart = e.GetPosition(TagsHost);
            // 兜底：绑定尚未生效时 GetLeft 可能为 NaN，退回 VM 值
            double ox = Canvas.GetLeft(container);
            double oy = Canvas.GetTop(container);
            _dragOrigX = double.IsNaN(ox) ? item.TagX : ox;
            _dragOrigY = double.IsNaN(oy) ? item.TagY : oy;
            _dragActive = false;
            container.CaptureMouse();
        }

        private void OnTagMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragItem == null || _dragContainer == null) return;
            var pos = e.GetPosition(TagsHost);
            double dx = pos.X - _dragStart.X;
            double dy = pos.Y - _dragStart.Y;
            if (!_dragActive && Math.Abs(dx) + Math.Abs(dy) < 4) return; // 未超过阈值 = 点击
            _dragActive = true;
            // 鼠标位移（屏幕 px）换算为 300×506 坐标系位移（除以 LayoutTransform 缩放）
            double scale = MouseScale.ScaleX > 0.01 ? MouseScale.ScaleX : 1;
            Canvas.SetLeft(_dragContainer, _dragOrigX + dx / scale);
            Canvas.SetTop(_dragContainer, _dragOrigY + dy / scale);
        }

        private void OnTagMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragItem == null || _dragContainer == null) return;
            _dragItem.TagX = Canvas.GetLeft(_dragContainer);
            _dragItem.TagY = Canvas.GetTop(_dragContainer);
            if (_dragActive)
            {
                e.Handled = true; // 拖动过：阻止 RadioButton 触发选中/命令
                if (DataContext is AppViewModel vm) vm.SaveTagPositions();
            }
            _dragContainer.ReleaseMouseCapture();
            _dragItem = null;
            _dragContainer = null;
        }

    }
}
