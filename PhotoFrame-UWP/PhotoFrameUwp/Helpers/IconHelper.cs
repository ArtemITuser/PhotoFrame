// Helpers/IconHelper.cs — UWP-порт загрузчика иконок Aero из WPF v3.2
// Иконки лежат в Assets\ (перенесены из Resources\Icons исходной сборки):
//   472.png PLAY, 475.png PAUSE, 469.png PREV, 480.png NEXT,
//   102.png SETTINGS, 033.png FOLDER, 492.png THEME → Segoe MDL2 fallback.
// Fallback: Segoe MDL2 Assets (есть на Mobile 15254) → Unicode-символ.

using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace PhotoFrameUwp.Helpers
{
    public enum IconRole
    {
        Play           = 0,
        Pause          = 1,
        Previous       = 2,
        Next           = 3,
        Settings       = 4,
        Fullscreen     = 5,
        ExitFullscreen = 6,
        Shuffle        = 7,
        Folder         = 8,
        Theme          = 9,
    }

    public static class IconHelper
    {
        private static readonly string[] FileNames =
        {
            "Assets/472.png", // Play
            "Assets/475.png", // Pause
            "Assets/469.png", // Previous
            "Assets/480.png", // Next
            "Assets/102.png", // Settings
            null,             // Fullscreen — MDL2
            null,             // ExitFullscreen — MDL2
            null,             // Shuffle — MDL2
            "Assets/033.png", // Folder
            null,             // Theme — MDL2
        };

        private static readonly string[] Mdl2Fallbacks =
        {
            "\uE768", // Play
            "\uE769", // Pause
            "\uE76B", // Previous
            "\uE76C", // Next
            "\uE713", // Settings
            "\uE740", // Fullscreen
            "\uE73F", // ExitFullscreen
            "\uE8B3", // Shuffle
            "\uE8B7", // Folder
            "\uE706", // Theme/brightness
        };

        private static readonly System.Collections.Generic.Dictionary<string, ImageSource> _cache
            = new System.Collections.Generic.Dictionary<string, ImageSource>();

        /// <summary>Возвращает BitmapImage для роли или null (тогда используйте GetGlyph).</summary>
        public static ImageSource GetAero(IconRole role)
        {
            string file = FileNames[(int)role];
            if (file == null) return null;

            ImageSource src;
            if (_cache.TryGetValue(file, out src)) return src;

            try
            {
                var bmp = new BitmapImage(new Uri("ms-appx:///" + file, UriKind.Absolute));
                _cache[file] = bmp;
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Glyph из Segoe MDL2 как запасной вариант.</summary>
        public static string GetGlyph(IconRole role) => Mdl2Fallbacks[(int)role];

        /// <summary>
        /// Заполняет Button иконкой: сначала Aero PNG (как в оригинале),
        /// при недоступности — Segoe MDL2 glyph. Сохраняет ToolTip.
        /// </summary>
        public static void ApplyTo(Button btn, IconRole role, double iconSize = 22)
        {
            if (btn == null) return;
            var img = GetAero(role);
            if (img != null)
            {
                btn.Content = new Image
                {
                    Source         = img,
                    Width          = iconSize,
                    Height         = iconSize,
                    Stretch        = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                };
            }
            else
            {
                btn.Content = new TextBlock
                {
                    Text       = GetGlyph(role),
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize   = iconSize - 6,
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                };
            }
        }
    }
}
