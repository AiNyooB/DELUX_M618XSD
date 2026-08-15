using System;
using System.Globalization;
using System.Windows.Data;

namespace DeluxDriver;

/// <summary>
/// 将宽度值转换为竖版黄金比例高度（宽 × 1.618）。
/// 用于保持卡片容器为竖版黄金比例（高 > 宽）。
/// </summary>
[ValueConversion(typeof(double), typeof(double))]
public class GoldenRatioConverter : IValueConverter
{
    private const double GoldenRatio = 1.618;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double w && w > 0)
            return w * GoldenRatio;
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}