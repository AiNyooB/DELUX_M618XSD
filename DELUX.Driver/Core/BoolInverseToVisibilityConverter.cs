using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DeluxDriver;

/// <summary>布尔取反后转 Visibility（true→Collapsed，false→Visible）。</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolInverseToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? (b ? Visibility.Collapsed : Visibility.Visible) : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Collapsed;
}
