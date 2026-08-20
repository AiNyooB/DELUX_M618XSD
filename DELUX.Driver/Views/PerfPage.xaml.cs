using System.Windows.Controls;

namespace DeluxDriver;

public partial class PerfPage : Page
{
    // 回报率卡片选中态判定用的整数常量（DataTrigger 的 Value 必须与 CurrentRateHz 的 int 类型一致，
    // 不能用字符串 "125"，否则字符串与数字比较永不相等，选中高亮不触发）。
    public const int Rate125 = 125;
    public const int Rate250 = 250;
    public const int Rate500 = 500;
    public const int Rate1000 = 1000;

    public PerfPage() => InitializeComponent();
}
