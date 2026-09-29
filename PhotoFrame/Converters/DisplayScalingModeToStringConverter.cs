using System;
using System.Globalization;
using System.Windows.Data;
using PhotoFrame.Models;

namespace PhotoFrame.Converters
{
    public sealed class DisplayScalingModeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value switch
            {
                DisplayScalingMode.Fit => "Вписать целиком",
                DisplayScalingMode.Fill => "Заполнить экран",
                DisplayScalingMode.SmartCrop => "Умное кадрирование",
                DisplayScalingMode.PixelPerfect => "Точное 1:1",
                _ => value?.ToString() ?? string.Empty
            };
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
