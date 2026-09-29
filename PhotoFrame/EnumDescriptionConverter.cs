// Converters/EnumDescriptionConverter.cs — v3.7 (build 57)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using PhotoFrame.Models;

namespace PhotoFrame.Converters
{
    /// <summary>build 57: см. AppSettings.GpsPrecisionLevel.</summary>
    [ValueConversion(typeof(LocationPrecision), typeof(string))]
    public class LocationPrecisionToStringConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v switch
        {
            LocationPrecision.City     => "Город",
            LocationPrecision.District => "Район",
            LocationPrecision.Street   => "Улица и дом",
            _                          => v?.ToString() ?? ""
        };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    [ValueConversion(typeof(TransitionType), typeof(string))]
    public class TransitionTypeToStringConverter : IValueConverter
    {
        private static readonly Dictionary<TransitionType, string> N = new()
        {
            {TransitionType.Random,       "Случайный"},
            {TransitionType.Fade,         "Затухание"},
            {TransitionType.SlideLeft,    "Сдвиг влево"},
            {TransitionType.SlideRight,   "Сдвиг вправо"},
            {TransitionType.SlideUp,      "Сдвиг вверх"},
            {TransitionType.SlideDown,    "Сдвиг вниз"},
            {TransitionType.ZoomIn,       "Приближение"},
            {TransitionType.ZoomOut,      "Отдаление"},
            {TransitionType.FlipH,        "Переворот по горизонтали"},
            {TransitionType.FlipV,        "Переворот по вертикали"},
            {TransitionType.BlurDissolve, "Блюр-растворение"},
            {TransitionType.WipeLeft,     "Шторка влево"},
            {TransitionType.WipeRight,    "Шторка вправо"},
            {TransitionType.WipeUp,       "Шторка вверх"},
            {TransitionType.WipeDown,     "Шторка вниз"},
            {TransitionType.Checkerboard, "Шахматная доска"},
            {TransitionType.KenBurns,     "Эффект Кен Бёрнса"},
            {TransitionType.Mosaic,       "Мозаика"},
            {TransitionType.Spiral,       "Спираль"},
            {TransitionType.PageTurn,     "Листание страницы"},
        };
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is TransitionType tt && N.TryGetValue(tt, out var n) ? n : v?.ToString() ?? "";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    [ValueConversion(typeof(AppTheme), typeof(string))]
    public class AppThemeToStringConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v switch
        {
            AppTheme.System => "Системная",
            AppTheme.Dark   => "Тёмная",
            AppTheme.Light  => "Светлая",
            _               => v?.ToString() ?? ""
        };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    [ValueConversion(typeof(PlayMode), typeof(string))]
    public class PlayModeToStringConverter : IValueConverter
    {
        private static readonly Dictionary<PlayMode, string> N = new()
        {
            {PlayMode.Sequential,     "По порядку (имя файла)"},
            {PlayMode.Shuffle,        "Перемешать (без повторов)"},
            {PlayMode.TrueRandom,     "Случайно (с повторами)"},
            {PlayMode.DateAscending,  "По дате: старые → новые"},
            {PlayMode.DateDescending, "По дате: новые → старые"},
        };
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is PlayMode pm && N.TryGetValue(pm, out var n) ? n : v?.ToString() ?? "";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    [ValueConversion(typeof(CounterFormat), typeof(string))]
    public class CounterFormatToStringConverter : IValueConverter
    {
        private static readonly Dictionary<CounterFormat, string> N = new()
        {
            {CounterFormat.PhotoOnly, "Только фото:  3 / 47"},
            {CounterFormat.WithTotal, "Фото + файлы:  3 / 47  [312 файлов]"},
            {CounterFormat.Hidden,    "Скрыть счётчик"},
        };
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is CounterFormat cf && N.TryGetValue(cf, out var n) ? n : v?.ToString() ?? "";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    [ValueConversion(typeof(UiMode), typeof(string))]
    public class UiModeToStringConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v switch
        {
            UiMode.Modern => "Modern (Windows 10/11 Fluent)",
            UiMode.Aero7  => "Aero7 (Windows 7 — экспериментально)",
            _             => v?.ToString() ?? ""
        };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    [ValueConversion(typeof(AutoOffMode), typeof(string))]
    public class AutoOffModeToStringConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v switch
        {
            AutoOffMode.Disabled        => "Выключено",
            AutoOffMode.SmartUsage      => "Умный подсчёт использования",
            AutoOffMode.ManualSchedule  => "Ручное расписание (от/до)",
            AutoOffMode.SunsetToSunrise => "Автоматически: закат → рассвет",
            _                           => v?.ToString() ?? ""
        };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }

    [ValueConversion(typeof(TilePhotoDistance), typeof(string))]
    public class TilePhotoDistanceToStringConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v switch
        {
            TilePhotoDistance.Close    => "Крупный план",
            TilePhotoDistance.Balanced => "Сбалансированно",
            TilePhotoDistance.Far      => "Целиком",
            _                          => v?.ToString() ?? ""
        };
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => Binding.DoNothing;
    }
}
