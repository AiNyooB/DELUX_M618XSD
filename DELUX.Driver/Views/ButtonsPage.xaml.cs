using System;
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
            // 右栏出现与否不影响图片尺寸——只改水平对齐（靠左平移），见 XAML 注释。
            CardHost.SizeChanged += OnCardHostSizeChanged;
            // 标签拖动定位（调试用）：拖动标签到按键上，松手后坐标写入 button-tags.json
            TagsHost.PreviewMouseLeftButtonDown += OnTagMouseDown;
            TagsHost.PreviewMouseMove += OnTagMouseMove;
            TagsHost.PreviewMouseLeftButtonUp += OnTagMouseUp;
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
