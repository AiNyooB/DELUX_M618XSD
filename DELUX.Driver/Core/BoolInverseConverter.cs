using System;
using System.Globalization;
using System.Windows.Data;

namespace DeluxDriver;

/// <summary>布尔取反转换器（用于禁用态）。</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class BoolInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
