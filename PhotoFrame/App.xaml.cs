// App.xaml.cs — v4.1 (build 44 / v1.2.0.1)
//
// Ghost activation pattern:
//   Modern → AeroTheme.xaml absent from MergedDictionaries.
//   Aero7  → AeroTheme.xaml added LAST → overrides AccentButton, SecondaryButton,
//             NavItem, SectionHeader, CheckBox (TargetType), ToolbarBgBrush.
//   DynamicResource in all XAML files responds instantly with zero code-behind tree-walks.
//
// ResourceDictionary URIs use pack:// absolute form for reliable in-code resolution.

using System;
using System.Linq;
using System.Windows;
using PhotoFrame.Helpers;
using PhotoFrame.Models;
using PhotoFrame.Services;

namespace PhotoFrame
{
    public partial class App : Application
    {
        public static AppTheme     CurrentTheme  { get; private set; } = AppTheme.System;
        public static UiMode       CurrentUiMode { get; private set; } = UiMode.Modern;
        public static AppStartMode StartMode     { get; private set; } = AppStartMode.Normal;
        public static IntPtr       PreviewHwnd   { get; private set; } = IntPtr.Zero;

        public static event Action<AppTheme>? ThemeChanged;
        public static event Action<UiMode>?   UiModeChanged;

        // Absolute pack URIs — safe to use in code-behind (relative URIs can fail
        // when called before XAML BaseUri is established).
        private static readonly Uri _darkUri  = MakePackUri("Themes/DarkTheme.xaml");
        private static readonly Uri _lightUri = MakePackUri("Themes/LightTheme.xaml");
        private static readonly Uri _aeroUri  = MakePackUri("Themes/AeroTheme.xaml");

        private static Uri MakePackUri(string path)
            => new($"pack://application:,,,/{path}", UriKind.Absolute);

        protected override void OnStartup(StartupEventArgs e)
        {
            LiveTileService.SetAppUserModelId();
            base.OnStartup(e);
            (StartMode, PreviewHwnd) = SystemIntegration.ParseArgs(e.Args);

            DispatcherUnhandledException += (_, ex) =>
            {
                var inner = ex.Exception;
                while (inner?.InnerException != null) inner = inner.InnerException;
                MessageBox.Show(
                    $"Ошибка: {inner?.Message}\n\nТип: {inner?.GetType().Name}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            AppSettings cfg;
            try   { cfg = SettingsService.Load(); }
            catch { cfg = new AppSettings(); }

            try { ApplyTheme(cfg.Theme, notify: false); }
            catch { ApplyTheme(AppTheme.System, notify: false); }

            try { ApplyUiMode(cfg.UiMode, notify: false); }
            catch { }

            if (StartMode == AppStartMode.Configure)
                new Views.SettingsWindow(cfg).Show();
        }

        // ── Public API ───────────────────────────────────────────────────────────

        public static void ChangeTheme(AppTheme t)  => ApplyTheme(t,  notify: true);
        public static void ChangeUiMode(UiMode m)   => ApplyUiMode(m, notify: true);

        // ── Theme (Dark/Light colour tokens) ─────────────────────────────────────

        private static void ApplyTheme(AppTheme requested, bool notify)
        {
            AppTheme actual = requested == AppTheme.System
                ? (WindowHelper.IsSystemDarkMode() ? AppTheme.Dark : AppTheme.Light)
                : requested;
            CurrentTheme = actual;

            Uri uri = actual == AppTheme.Light ? _lightUri : _darkUri;

            var dicts   = Current.Resources.MergedDictionaries;
            bool replaced = false;
            for (int i = 0; i < dicts.Count; i++)
            {
                string? src = dicts[i].Source?.OriginalString;
                if (src != null &&
                    (src.EndsWith("DarkTheme.xaml",  StringComparison.OrdinalIgnoreCase) ||
                     src.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase)))
                {
                    dicts[i] = new ResourceDictionary { Source = uri };
                    replaced  = true;
                    break;
                }
            }
            if (!replaced)
                dicts.Insert(0, new ResourceDictionary { Source = uri });

            if (notify) ThemeChanged?.Invoke(actual);
        }

        // ── UiMode (Aero layer — always last to win the key override race) ────────

        private static void ApplyUiMode(UiMode mode, bool notify)
        {
            CurrentUiMode = mode;
            var dicts = Current.Resources.MergedDictionaries;

            // Remove any existing Aero dictionary first
            var toRemove = dicts
                .Where(d => d.Source?.OriginalString
                    .EndsWith("AeroTheme.xaml", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            foreach (var d in toRemove) dicts.Remove(d);

            // Add Aero LAST so its resource keys shadow CommonStyles & theme tokens
            if (mode == UiMode.Aero7)
                dicts.Add(new ResourceDictionary { Source = _aeroUri });

            if (notify) UiModeChanged?.Invoke(mode);
        }
    }
}
