// App.xaml.cs — UWP-порт App.xaml.cs + части MainWindow из PhotoFrame v1.2.2.0.
// Логика применения темы сохранена 1:1 (System→ActualTheme, Dark/Light), но вместо
// WindowHelper (DWM/Mica P/Invoke) — DisplayHelper (UWP-эквиваленты).

using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using PhotoFrameUwp.Helpers;
using PhotoFrameUwp.Models;
using PhotoFrameUwp.Services;

namespace PhotoFrameUwp
{
    sealed partial class App : Application
    {
        public static AppSettings Settings { get; private set; } = new AppSettings();

        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.Resuming += (s, e) =>
            {
                // Возобновление слайдшоу после Continuum-переключения/сворачивания
                var root = Window.Current?.Content as MainPage;
                root?.ResumeAfterSuspend();
            };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            // ── Настройки до построения UI (аналог конструктора MainWindow) ────
            await SettingsService.InitCacheAsync();
            Settings = SettingsService.Load();

            // ── Режимы активации (аналог /S /P /C скринсейвера WPF) ───────────
            var mode = SystemIntegration.ParseArgs(
                string.IsNullOrEmpty(e.Arguments)
                    ? new string[0]
                    : e.Arguments.Split(' '));

            var frame = Window.Current.Content as Frame;

            if (frame == null)
            {
                frame = new Frame();
                frame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = frame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (frame.Content == null)
                {
                    // Configure-режим (/C) открывает сразу настройки — как в WPF
                    frame.Navigate(typeof(MainPage), mode);
                }

                // Полноэкранный режим фоторамки:
                //   * Mobile 15254/15400 — системный FullScreenMode (скрытие статус-бара);
                //   * Desktop/Continuum  — максимизация окна.
                ApplyViewMode(mode);

                Window.Current.Activate();

                // Тема и прозрачность (логика = MainWindow.ApplyTheme)
                ApplyTheme(Settings.Theme);
            }
        }

        private static void ApplyViewMode(AppStartMode mode)
        {
            try
            {
                var av = ApplicationView.GetForCurrentView();
                if (DisplayHelper.IsMobileFamily || mode == AppStartMode.Screensaver)
                {
                    if (av.IsFullScreenModeSupported())
                        av.TryEnterFullScreenMode();
                }
                else
                {
                    av.TryResize(new Windows.Foundation.Size(1280, 720));
                    if (mode == AppStartMode.Screensaver)
                        av.FullScreenSystemOverlayMode = FullScreenSystemOverlayMode.Minimal;
                }
            }
            catch { }
        }

        /// <summary>Применяет тему ко всем словарям уровня приложения (логика = ApplyTheme).</summary>
        public static void ApplyTheme(AppTheme theme)
        {
            ActualTheme effective;
            switch (theme)
            {
                case AppTheme.Light: effective = ActualTheme.Light; break;
                case AppTheme.Dark:  effective = ActualTheme.Dark;  break;
                default:             effective = RequestedTheme == ElementTheme.Dark
                                              ? ActualTheme.Dark : ActualTheme.Light; break;
            }

            var dicts = Application.Current.Resources.MergedDictionaries;
            string themeSrc = effective == ActualTheme.Dark
                ? "/Themes/DarkTheme.xaml"
                : "/Themes/LightTheme.xaml";

            for (int i = 0; i < dicts.Count; i++)
            {
                var src = dicts[i].Source?.ToString() ?? "";
                if (src.Contains("DarkTheme") || src.Contains("LightTheme"))
                {
                    dicts[i] = new ResourceDictionary { Source = new Uri(themeSrc, UriKind.Relative) };
                    break;
                }
            }

            DisplayHelper.ApplyTitleBarTheme(effective == ActualTheme.Dark);
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                var root = Window.Current?.Content as MainPage;
                root?.PrepareForSuspend();
                SettingsService.Save(App.Settings);
            }
            finally { deferral.Complete(); }
        }
    }
}
