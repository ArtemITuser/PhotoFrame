// Helpers/DisplayHelper.cs — UWP-эквивалент WindowHelper.cs из WPF v3.2
// В sandbox недоступны DWM/Mica/Acrylic/P-Invoke → «эффект прозрачности»
// (EnableMicaEffect в настройках) реализуется как затемнённый фон + акрилоподобный
// оверлей тулбара; на Continuum дополнительно адаптируется размер тулбара.

using System;
using Windows.System.Profile;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace PhotoFrameUwp.Helpers
{
    public static class DisplayHelper
    {
        /// <summary>Телефон / Hybrid (Continuum-док) / Desktop по AnalyticsInfo.DeviceFamily.</summary>
        public static bool IsMobileFamily =>
            AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Mobile";

        public static bool IsIotFamily =>
            AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.IoT";

        /// <summary>Continuum-режим: телефон, развёрнутый во внешний дисплей.</summary>
        public static bool IsContinuum
        {
            get
            {
                try
                {
                    return IsMobileFamily && !ApplicationView.GetForCurrentView()
                        .TryEnterFullScreenMode() /* probe */ ;
                }
                catch { return false; }
            }
        }

        /// <summary>Освободить probe-полноэкранный режим сразу после проверки.</summary>
        public static void ReleaseProbe()
        {
            try { ApplicationView.GetForCurrentView().ExitFullScreenMode(); }
            catch { }
        }

        /// <summary>Ширина «зоны касания» для кнопок: 40dp телефон, 32dp desktop/continuum.</summary>
        public static double TouchTargetSize => IsMobileFamily ? 44 : 36;

        /// <summary>
        /// Адаптив тулбара: на телефоне — компактный (нижняя полоса),
        /// на desktop/continuum — расширенный с заголовком.
        /// </summary>
        public static void AdaptToolbar(FrameworkElement toolbar, FrameworkElement titleBar)
        {
            if (toolbar == null) return;
            if (IsMobileFamily)
            {
                toolbar.Height = 56;
                if (titleBar != null) titleBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                toolbar.Height = 64;
                if (titleBar != null) titleBar.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Эквивалент TryApplyMica: в UWP применяем полупрозрачный тёмный фон
        /// тулбара/оверлеев (AccentColor не устанавливаем — нет доступа к DWM).
        /// Возвращает имя применённого эффекта для статуса в настройках.
        /// </summary>
        public static string TryApplyTransparency(bool enable)
        {
            // На 15254/15400 системного acrylic-бэкдропа нет — эффект имитируется
            // кистями из темы (#CC... фоны). Ничего системного менять не нужно.
            return enable ? "acrylic-simulated" : "none";
        }

        public static void RemoveTransparency() { /* no-op — см. TryApplyTransparency */ }

        /// <summary>Тёмный/светлый заголовок системы — через ApplicationViewTitleBar цвета.</summary>
        public static void ApplyTitleBarTheme(bool dark)
        {
            try
            {
                var tb = ApplicationView.GetForCurrentView().TitleBar;
                if (tb == null) return;
                var fg = dark ? Windows.UI.Colors.White : Windows.UI.Colors.Black;
                var bg = dark ? Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E)
                              : Windows.UI.Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3);
                tb.BackgroundColor = bg;
                tb.ForegroundColor = fg;
                tb.InactiveBackgroundColor = bg;
                tb.ButtonBackgroundColor = bg;
                tb.ButtonForegroundColor = fg;
            }
            catch { }
        }
    }
}
