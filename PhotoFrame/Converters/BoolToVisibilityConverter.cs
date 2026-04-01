// Converters/BoolToVisibilityConverter.cs
// Стандартный конвертер bool → Visibility для привязок XAML.
// Поддерживает инверсию через параметр "Inverse".

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoFrame.Converters
{
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool bVal    = value is bool b && b;
            bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
            bool visible = inverse ? !bVal : bVal;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visible = value is Visibility v && v == Visibility.Visible;
            bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
            return inverse ? !visible : visible;
        }
    }
}
