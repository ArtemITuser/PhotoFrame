// App.xaml.cs — v3.6 (build 52)
using System;
using System.Windows;
using PhotoFrame.Helpers;
using PhotoFrame.Models;
using PhotoFrame.Services;

namespace PhotoFrame
{
    public partial class App : Application
    {
        public static AppTheme     CurrentTheme { get; private set; } = AppTheme.System;
        public static AppStartMode StartMode    { get; private set; } = AppStartMode.Normal;
        public static IntPtr       PreviewHwnd  { get; private set; } = IntPtr.Zero;
        public static event Action<AppTheme>? ThemeChanged;

        protected override void OnStartup(StartupEventArgs e)
        {
            LiveTileService.SetAppUserModelId();
            base.OnStartup(e);
            (StartMode, PreviewHwnd) = SystemIntegration.ParseArgs(e.Args);

            DispatcherUnhandledException += (_, ex) =>
            {
                var inner = ex.Exception;
                while (inner.InnerException != null) inner = inner.InnerException;
                MessageBox.Show($"Ошибка: {inner.Message}\n\nТип: {inner.GetType().Name}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            try
            {
                var settings = SettingsService.Load();
                ApplyTheme(settings.Theme, notify: false);
                ApplyUiMode(settings.UiMode);
            }
            catch
            {
                ApplyTheme(AppTheme.System, notify: false);
                ApplyUiMode(UiMode.Modern);
            }

            if (StartMode == AppStartMode.Configure)
                new Views.SettingsWindow(SettingsService.Load()).Show();
        }

        public static void ChangeTheme(AppTheme t) => ApplyTheme(t, notify: true);

        /// <summary>
        /// Применяет/снимает наложение AeroTheme (+ AeroDarkTheme при тёмной
        /// цветовой схеме) поверх CommonStyles. Снятие обоих словарей
        /// восстанавливает Modern-оформление по умолчанию.
        /// </summary>
        public static void ApplyUiMode(UiMode mode)
        {
            CurrentUiMode = mode;
            RefreshAeroOverlay();
            UiModeChanged?.Invoke(mode);
        }

        public static UiMode CurrentUiMode { get; private set; } = UiMode.Modern;
        public static event Action<UiMode>? UiModeChanged;

        /// <summary>
        /// Пересобирает стек Aero-словарей в соответствии с текущими
        /// CurrentUiMode и CurrentTheme. AeroTheme.xaml задаёт структуру
        /// (стеклянные шаблоны), AeroDarkTheme.xaml — переопределяет базовую
        /// палитру и те стили, что были зашиты под светлое стекло, поэтому
        /// он всегда добавляется ПОСЛЕ AeroTheme.xaml, чтобы выиграть в
        /// порядке поиска ресурсов WPF.
        /// </summary>
        private static void RefreshAeroOverlay()
        {
            var dicts = Current.Resources.MergedDictionaries;
            const string aeroSource     = "Themes/AeroTheme.xaml";
            const string aeroDarkSource = "Themes/AeroDarkTheme.xaml";

            for (int i = dicts.Count - 1; i >= 0; i--)
            {
                var src = dicts[i].Source?.OriginalString;
                if (src != null && (src.EndsWith("AeroTheme.xaml", StringComparison.OrdinalIgnoreCase)
                                  || src.EndsWith("AeroDarkTheme.xaml", StringComparison.OrdinalIgnoreCase)))
                    dicts.RemoveAt(i);
            }

            if (CurrentUiMode == UiMode.Aero7)
            {
                dicts.Add(new ResourceDictionary { Source = new Uri(aeroSource, UriKind.Relative) });
                if (CurrentTheme == AppTheme.Dark)
                    dicts.Add(new ResourceDictionary { Source = new Uri(aeroDarkSource, UriKind.Relative) });
            }
        }

        private static void ApplyTheme(AppTheme requested, bool notify)
        {
            AppTheme actual = requested == AppTheme.System
                ? (WindowHelper.IsSystemDarkMode() ? AppTheme.Dark : AppTheme.Light)
                : requested;
            CurrentTheme = actual;

            Uri uri = actual == AppTheme.Light
                ? new Uri("Themes/LightTheme.xaml", UriKind.Relative)
                : new Uri("Themes/DarkTheme.xaml",  UriKind.Relative);

            var dicts = Current.Resources.MergedDictionaries;
            bool replaced = false;
            for (int i = 0; i < dicts.Count; i++)
            {
                var src = dicts[i].Source?.OriginalString;
                if (src != null && src.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase)
                    && !src.EndsWith("AeroTheme.xaml", StringComparison.OrdinalIgnoreCase)
                    && !src.EndsWith("AeroDarkTheme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    dicts.Insert(i, new ResourceDictionary { Source = uri });
                    dicts.RemoveAt(i + 1);
                    replaced = true; break;
                }
            }
            if (!replaced) dicts.Insert(0, new ResourceDictionary { Source = uri });

            // Тема сменилась — Aero7-оверлей должен отреагировать (Light↔Dark),
            // поэтому пересобираем его стек здесь же, а не только в ApplyUiMode.
            RefreshAeroOverlay();

            if (notify) ThemeChanged?.Invoke(actual);
        }
    }
}

