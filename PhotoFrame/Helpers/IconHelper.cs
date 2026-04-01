// Helpers/IconHelper.cs
// Загрузчик иконок Aero из ресурсов сборки (pack:// URI).
// При ошибке загрузки — Segoe MDL2 Assets → Unicode-символ.
//
// ИДЕНТИФИЦИРОВАННЫЕ ИКОНКИ (ASCII-анализ по цвету и форме):
//  472.png (55x55) — круглая кнопка, треугольник PLAY ▶
//  475.png (55x55) — круглая кнопка, два прямоугольника PAUSE ⏸
//  469.png (45x45) — синяя кнопка, стрелка «Назад» ◄
//  480.png (45x45) — синяя кнопка, стрелка «Вперёд» ►
//  102.png (256x256) — серый инструмент/гаечный ключ → Настройки ⚙
//  033.png (256x256) — жёлтая папка Windows → Папка 📁
//  259.png (256x256) — зелёный круг → Случайный/Shuffle
//  265.png (32x32)  — диагональная стрелка → Полный экран
//  267.png (32x32)  — диагональная стрелка (другая) → Выход из экрана
//  492.png (48x48)  — диагональная полоска яркости → Тема/Яркость ☀

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoFrame.Helpers
{
    public enum IconRole
    {
        Play          = 0,
        Pause         = 1,
        Previous      = 2,
        Next          = 3,
        Settings      = 4,
        Fullscreen    = 5,
        ExitFullscreen= 6,
        Shuffle       = 7,
        Folder        = 8,
        Theme         = 9,
    }

    public static class IconHelper
    {
        // png filename, Segoe MDL2 codepoint, Unicode fallback
        private static readonly (string png, string mdl2, string uni)[] Map =
        {
            /* Play           */ ("472.png",  "\uE768", "▶"),
            /* Pause          */ ("475.png",  "\uE769", "⏸"),
            /* Previous       */ ("469.png",  "\uE892", "◀"),
            /* Next           */ ("480.png",  "\uE893", "▶"),
            /* Settings       */ ("102.png",  "\uE713", "⚙"),
            /* Fullscreen     */ ("265.png",  "\uE1D9", "⛶"),
            /* ExitFullscreen */ ("267.png",  "\uE1D8", "⊡"),
            /* Shuffle        */ ("259.png",  "\uE8B1", "⇄"),
            /* Folder         */ ("033.png",  "\uE8B7", "📁"),
            /* Theme          */ ("492.png",  "\uE793", "☀"),
        };

        private static readonly BitmapImage?[] _cache = new BitmapImage?[10];
        private static readonly bool[]         _tried = new bool[10];

        public static BitmapImage? GetBitmap(IconRole role)
        {
            int i = (int)role;
            if (_tried[i]) return _cache[i];
            _tried[i] = true;
            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/Icons/{Map[i].png}");
                var b   = new BitmapImage(uri);
                b.Freeze();
                _cache[i] = b;
            }
            catch { _cache[i] = null; }
            return _cache[i];
        }

        /// <summary>
        /// Создаёт Image (Aero PNG) или TextBlock (MDL2/Unicode) для кнопки.
        /// Размер size задаёт ширину/высоту в пикселях DeviceIndependent.
        /// </summary>
        public static FrameworkElement MakeContent(IconRole role, double size = 20)
        {
            var bmp = GetBitmap(role);
            if (bmp != null)
            {
                var image = new Image
                {
                    Source              = bmp,
                    Width               = size,
                    Height              = size,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding   = true
                };
                // Устанавливаем высокое качество масштабирования через присоединённое свойство
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                return image;
            }

            // Fallback: Segoe MDL2 Assets
            (_, string mdl2, string uni) = Map[(int)role];
            return new TextBlock
            {
                Text        = mdl2,
                FontFamily  = new FontFamily("Segoe MDL2 Assets,Segoe UI Symbol"),
                FontSize    = size * 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
        }

        public static void SetButtonIcon(ContentControl btn, IconRole role, double size = 20)
            => btn.Content = MakeContent(role, size);

        /// <summary>
        /// Загружает thumbnail из файла изображения для предпросмотра.
        /// Возвращает null при ошибке.
        /// </summary>
        public static BitmapImage? LoadThumbnail(string filePath, int maxPx = 96)
        {
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource        = new Uri(filePath, UriKind.Absolute);
                b.DecodePixelWidth = maxPx;
                b.CacheOption      = BitmapCacheOption.OnLoad;
                b.CreateOptions    = BitmapCreateOptions.IgnoreColorProfile;
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch { return null; }
        }
    }
}