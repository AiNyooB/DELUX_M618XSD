using System;
using System.Globalization;
using System.Windows.Data;

namespace DeluxDriver;

/// <summary>
/// int 值 == ConverterParameter(int) → bool。
/// 用于一组互斥 RadioButton 的 IsChecked 绑定同一 int 属性（如播放方式三选一），
/// ConvertBack 仅在接受 true 时回写该选项值，其余返回 Binding.DoNothing 不打断互斥。
/// </summary>
public class IntEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int v = value is int i ? i : 0;
        int p = parameter != null && int.TryParse(parameter.ToString(), out int x) ? x : 0;
        return v == p;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter != null && int.TryParse(parameter.ToString(), out int p))
            return p;
        return Binding.DoNothing;
    }
}
