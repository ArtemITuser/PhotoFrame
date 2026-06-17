// App.xaml.cs — v3.5
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

            try { ApplyTheme(SettingsService.Load().Theme, notify: false); }
            catch  { ApplyTheme(AppTheme.System, notify: false); }

            if (StartMode == AppStartMode.Configure)
                new Views.SettingsWindow(SettingsService.Load()).Show();
        }

        public static void ChangeTheme(AppTheme t) => ApplyTheme(t, notify: true);

        /// <summary>
        /// Applies or removes AeroTheme overlay on top of CommonStyles.
        /// AeroTheme overrides key styles (AccentButton, NavItem, Slider…) with
        /// Aero7 glass variants. Removing it restores Modern (CommonStyles) defaults.
        /// </summary>
        public static void ApplyUiMode(UiMode mode)
        {
            var dicts = Current.Resources.MergedDictionaries;
            const string aeroSource = "Themes/AeroTheme.xaml";

            // Remove existing Aero entry (if any)
            for (int i = dicts.Count - 1; i >= 0; i--)
                if (dicts[i].Source?.OriginalString.EndsWith("AeroTheme.xaml",
                    StringComparison.OrdinalIgnoreCase) == true)
                {
                    dicts.RemoveAt(i); break;
                }

            if (mode == UiMode.Aero7)
            {
                // Insert AFTER CommonStyles so Aero keys win via WPF lookup order
                dicts.Add(new ResourceDictionary
                    { Source = new Uri(aeroSource, UriKind.Relative) });
            }

            CurrentUiMode = mode;
            UiModeChanged?.Invoke(mode);
        }

        public static UiMode CurrentUiMode { get; private set; } = UiMode.Modern;
        public static event Action<UiMode>? UiModeChanged;

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
                if (dicts[i].Source?.OriginalString.EndsWith("Theme.xaml",
                    StringComparison.OrdinalIgnoreCase) == true)
                {
                    dicts.Insert(i, new ResourceDictionary { Source = uri });
                    dicts.RemoveAt(i + 1);
                    replaced = true; break;
                }
            }
            if (!replaced) dicts.Insert(0, new ResourceDictionary { Source = uri });
            if (notify) ThemeChanged?.Invoke(actual);
        }
    }
}
