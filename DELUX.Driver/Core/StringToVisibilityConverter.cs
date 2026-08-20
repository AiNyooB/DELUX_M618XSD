using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DeluxDriver;

/// <summary>
/// 非空非空字符串 → Visible，否则 → Collapsed。
/// 用于 SwitchProgress / SwitchError 等可选文案的显隐。
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
