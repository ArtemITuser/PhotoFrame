// App.xaml.cs — точка входа, управление темой, перехват исключений.

using System;
using System.Windows;
using PhotoFrame.Helpers;
using PhotoFrame.Models;
using PhotoFrame.Services;

namespace PhotoFrame
{
    public partial class App : Application
    {
        public static AppTheme     CurrentTheme { get; private set; } = AppTheme.Dark;
        public static AppStartMode StartMode    { get; private set; } = AppStartMode.Normal;
        public static IntPtr       PreviewHwnd  { get; private set; } = IntPtr.Zero;

        // Событие смены темы — все окна подписываются и обновляют DWM
        public static event Action<AppTheme>? ThemeChanged;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Разбираем аргументы скринсейвера
            (StartMode, PreviewHwnd) = SystemIntegration.ParseArgs(e.Args);

            // Перехват необработанных исключений
            DispatcherUnhandledException += (_, ex) =>
            {
                // Извлекаем самое глубокое исключение для понятного сообщения
                Exception inner = ex.Exception;
                while (inner.InnerException != null) inner = inner.InnerException;
                MessageBox.Show(
                    $"Ошибка: {inner.Message}\n\nТип: {inner.GetType().Name}",
                    "PhotoFrame — Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            // Загружаем тему из настроек до появления окна
            try
            {
                var settings = SettingsService.Load();
                ApplyTheme(settings.Theme, notify: false);
            }
            catch
            {
                ApplyTheme(AppTheme.Dark, notify: false);
            }

            // Режим /c — только настройки
            if (StartMode == AppStartMode.Configure)
            {
                var sw = new Views.SettingsWindow(SettingsService.Load());
                sw.Show();
            }
        }

        // ─── Смена темы ───────────────────────────────────────────────────────────

        public static void ChangeTheme(AppTheme requested)
            => ApplyTheme(requested, notify: true);

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

            // ВАЖНО: вставляем новую тему ПЕРЕД удалением старой —
            // иначе CommonStyles теряет ресурсы и генерирует NullRef.
            bool replaced = false;
            for (int i = 0; i < dicts.Count; i++)
            {
                string src = dicts[i].Source?.OriginalString ?? "";
                if (src.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    dicts.Insert(i, new ResourceDictionary { Source = uri });
                    dicts.RemoveAt(i + 1);
                    replaced = true;
                    break;
                }
            }
            if (!replaced)
                dicts.Insert(0, new ResourceDictionary { Source = uri });

            if (notify)
                ThemeChanged?.Invoke(actual);
        }
    }
}
