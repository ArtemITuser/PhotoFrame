// App.xaml.cs — v3.2
// По умолчанию тема System — следует за настройками Windows.

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
            base.OnStartup(e);
            (StartMode, PreviewHwnd) = SystemIntegration.ParseArgs(e.Args);

            DispatcherUnhandledException += (_, ex) =>
            {
                // v3.3: StackOverflowException НЕ перехватывается — процесс умирает
                // без этого окна. Лечим первопричины (итеративный FileScanner,
                // лимит пропуска битых файлов). Здесь — обычные исключения.
                Exception inner = ex.Exception;
                while (inner.InnerException != null) inner = inner.InnerException;
                MessageBox.Show($"Ошибка: {inner.Message}\n\nТип: {inner.GetType().Name}",
                    "PhotoFrame — Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            try
            {
                var s = SettingsService.Load();
                ApplyTheme(s.Theme, notify: false);
            }
            catch { ApplyTheme(AppTheme.System, notify: false); }

            if (StartMode == AppStartMode.Configure)
            {
                var sw = new Views.SettingsWindow(SettingsService.Load());
                sw.Show();
            }
        }

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
            bool replaced = false;
            for (int i = 0; i < dicts.Count; i++)
            {
                string src = dicts[i].Source?.OriginalString ?? "";
                if (src.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    // Вставляем перед удалением — CommonStyles не теряет ресурсы
                    dicts.Insert(i, new ResourceDictionary { Source = uri });
                    dicts.RemoveAt(i + 1);
                    replaced = true;
                    break;
                }
            }
            if (!replaced) dicts.Insert(0, new ResourceDictionary { Source = uri });

            if (notify) ThemeChanged?.Invoke(actual);
        }
    }
}
