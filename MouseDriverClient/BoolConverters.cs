using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MouseDriverClient
{
    /// <summary>bool → 连接状态文字（已连接 / 未连接）。</summary>
    public class BoolToText : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? "已连接" : "未连接";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>bool → 状态指示灯颜色（已连接绿 / 未连接灰）。</summary>
    public class BoolToBrush : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var green = new SolidColorBrush(Color.FromRgb(0x3C, 0xB3, 0x71));
            var gray = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            return value is bool b && b ? green : gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
