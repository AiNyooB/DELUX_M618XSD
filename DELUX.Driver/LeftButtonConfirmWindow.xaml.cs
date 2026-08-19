using System.Windows;

namespace DeluxDriver
{
    /// <summary>
    /// 左键改键风险确认弹窗：独立模态窗口，覆盖含标题栏的整个主窗口（见 MainWindow.UpdateLeftBtnConfirm）。
    /// 卡片不透明实底、无描边；Esc=取消，Enter/初始焦点=我知道了。
    /// </summary>
    public partial class LeftButtonConfirmWindow : Window
    {
        public LeftButtonConfirmWindow()
        {
            InitializeComponent();
            // 初始焦点给「我知道了」：Enter 直接确认（IsDefault），Esc 走「取消」（IsCancel）
            Loaded += (_, _) => OkButton.Focus();
        }
    }
}
