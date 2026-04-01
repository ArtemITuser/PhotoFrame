// MainWindow.xaml.cs — v3 (финальная версия)
//
// Ключевые изменения v3:
//   • Все кнопки тулбара имеют контент прямо в XAML (Segoe MDL2 TextBlock).
//     Код только меняет .Text у именованных TbPlayIcon, TbThemeIcon, TbFullscreenIcon.
//     Это полностью устраняет класс ошибок «NullRef при инициализации иконок».
//   • Settings/EmptyPanel кнопки вызывают OpenSettings() с полным try-catch.
//   • Тулбар в оконном режиме не прячется.
//   • App.ThemeChanged → корректное обновление DWM и иконки темы.

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PhotoFrame.Helpers;
using PhotoFrame.Models;
using PhotoFrame.Services;
using PhotoFrame.Views;

namespace PhotoFrame
{
    public partial class MainWindow : Window
    {
        // ─── Состояние ────────────────────────────────────────────────────────────
        private AppSettings       _cfg      = new();
        private TransitionEngine? _engine;
        private PlaylistManager   _playlist = new();
        private bool              _playing  = false;
        private bool              _fullscreen = false;
        private WindowState       _prevWinState;

        // ─── Таймеры ──────────────────────────────────────────────────────────────
        private readonly DispatcherTimer _slideTimer = new();
        private readonly DispatcherTimer _hideTimer  = new() { Interval = TimeSpan.FromSeconds(3) };
        private bool _toolbarVisible = true;

        // ─── Тач ──────────────────────────────────────────────────────────────────
        private double _touchStartX;
        private const double SwipePx = 70;

        // ─── Трей ─────────────────────────────────────────────────────────────────
        private System.Windows.Forms.NotifyIcon? _tray;

        // ──────────────────────────────────────────────────────────────────────────

        public MainWindow()
        {
            InitializeComponent();
            _slideTimer.Tick += async (_, __) => await AdvanceAsync();
            _hideTimer.Tick  += (_, __) => TryHideToolbar();
        }

        // ─── ЗАГРУЗКА ─────────────────────────────────────────────────────────────

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Движок переходов — только после InitializeComponent
                _engine = new TransitionEngine(ImgA, ImgB, RootGrid);

                // 2. Загружаем настройки
                _cfg = SettingsService.Load();

                // 3. Применяем тему к заголовку DWM
                RefreshDwmTheme();

                // 4. Подписываемся на смену темы
                App.ThemeChanged += OnThemeChanged;

                // 5. Mica (Windows 11)
                if (_cfg.EnableMicaEffect)
                    WindowHelper.TryApplyMica(this);

                // 6. Электропитание
                SystemIntegration.PreventSleep(_cfg.PreventSleep);

                // 7. Трей
                BuildTray();

                // 8. В режиме скринсейвера — сразу полный экран
                if (App.StartMode == AppStartMode.Screensaver)
                {
                    EnterFullscreen();
                    _hideTimer.Start();
                }

                // 9. Синхронизируем иконки с начальным состоянием
                SyncPlayIcon();
                SyncThemeIcon();
                SyncPlayModeIcon();
                // TbFullscreenIcon уже = \uE740 (Enter FullScreen) в XAML — корректно

                // 10. Метка интервала
                SyncIntervalLabel();

                // 11. Загружаем фото или показываем пустое состояние
                if (_cfg.SelectedPaths.Count > 0)
                    await ReloadPhotosAsync();
                else
                    ShowEmpty();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при запуске:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── ЗАГРУЗКА ФОТОГРАФИЙ ─────────────────────────────────────────────────

        private async Task ReloadPhotosAsync()
        {
            ScanPanel.Visibility  = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Collapsed;

            var photos = await FileScanner.ScanAsync(
                _cfg.SelectedPaths,
                _cfg.IncludeSubdirectories,
                p => Dispatcher.InvokeAsync(() => TbScanPath.Text = p));

            ScanPanel.Visibility = Visibility.Collapsed;

            if (photos.Count == 0) { ShowEmpty(); return; }

            _playlist.SetPhotos(photos, _cfg.PlayMode);
            await ShowCurrentAsync(animate: false);

            if (_cfg.AutoStart) StartSlide();
        }

        private async Task ShowCurrentAsync(bool animate)
        {
            var photo = _playlist.Current;
            if (photo == null) return;

            // EXIF — лениво
            if (!photo.DateTaken.HasValue && photo.Latitude == null)
                await Task.Run(() => MetadataReader.Populate(photo));

            // Загружаем изображение
            var bmp = await Task.Run(() => LoadBitmap(photo.FilePath));
            if (bmp == null)
            {
                // Пропускаем битый файл
                if (_playlist.Count > 1)
                {
                    _playlist.Next(_cfg.PlayMode, _cfg.LoopSlideshow);
                    await ShowCurrentAsync(animate);
                }
                return;
            }

            UpdateOverlays(photo);

            if (animate && _engine != null && _playlist.Count > 1)
                _engine.Transition(bmp, _cfg.TransitionType, _cfg.TransitionDurationSeconds);
            else
                _engine?.ShowImmediate(bmp);

            TbCounter.Text = $"{_playlist.CurrentIndex + 1} / {_playlist.Count}";
        }

        private static BitmapImage? LoadBitmap(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource        = new Uri(path, UriKind.Absolute);
                b.CacheOption      = BitmapCacheOption.OnLoad;
                b.CreateOptions    = BitmapCreateOptions.IgnoreColorProfile;
                b.DecodePixelWidth = 2560; // ограничиваем для экономии RAM
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch { return null; }
        }

        // ─── НАВИГАЦИЯ ────────────────────────────────────────────────────────────

        private async Task AdvanceAsync()
        {
            if (_engine?.IsTransitioning == true) return;
            _playlist.Next(_cfg.PlayMode, _cfg.LoopSlideshow);
            await ShowCurrentAsync(animate: true);
        }

        private async Task GoBackAsync()
        {
            if (_engine?.IsTransitioning == true) return;
            _playlist.Prev(_cfg.LoopSlideshow);
            await ShowCurrentAsync(animate: true);
        }

        // ─── СЛАЙДШОУ ─────────────────────────────────────────────────────────────

        private void StartSlide()
        {
            _slideTimer.Interval = TimeSpan.FromSeconds(
                Math.Max(1, _cfg.SlideshowIntervalSeconds));
            _slideTimer.Start();
            _playing = true;
            SyncPlayIcon();
        }

        private void StopSlide()
        {
            _slideTimer.Stop();
            _playing = false;
            SyncPlayIcon();
        }

        // ─── ТУЛБАР ───────────────────────────────────────────────────────────────

        private void ShowToolbarNow()
        {
            _hideTimer.Stop();
            if (_toolbarVisible)
            {
                if (_fullscreen) _hideTimer.Start();
                return;
            }
            _toolbarVisible     = true;
            Toolbar.Visibility  = Visibility.Visible;
            TitleBar.Visibility = Visibility.Visible;
            var a = new DoubleAnimation(ToolbarSlide.Y, 0,
                new Duration(TimeSpan.FromMilliseconds(180)));
            ToolbarSlide.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty, a);
            if (_fullscreen) _hideTimer.Start();
        }

        private void TryHideToolbar()
        {
            _hideTimer.Stop();
            if (!_toolbarVisible || !_fullscreen) return;
            _toolbarVisible = false;
            var a = new DoubleAnimation(0, 62,
                new Duration(TimeSpan.FromMilliseconds(280)));
            a.Completed += (_, __) =>
            {
                Toolbar.Visibility  = Visibility.Collapsed;
                TitleBar.Visibility = Visibility.Collapsed;
            };
            ToolbarSlide.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty, a);
        }

        // ─── ПОЛНЫЙ ЭКРАН ─────────────────────────────────────────────────────────

        private void EnterFullscreen()
        {
            if (_fullscreen) return;
            _prevWinState = WindowState;
            WindowStyle   = WindowStyle.None;
            WindowState   = WindowState.Maximized;
            _fullscreen   = true;
            // \uE741 = BackToWindow (выход из полноэкранного режима)
            TbFullscreenIcon.Text = "\uE741";
            BtnFullscreen.ToolTip = "Оконный режим  F / F11";
        }

        private void ExitFullscreen()
        {
            if (!_fullscreen) return;
            WindowStyle   = WindowStyle.SingleBorderWindow;
            WindowState   = _prevWinState;
            _fullscreen   = false;
            ShowToolbarNow();
            // \uE740 = FullScreen (войти в полноэкранный режим)
            TbFullscreenIcon.Text = "\uE740";
            BtnFullscreen.ToolTip = "Полный экран  F / F11";
        }

        // ─── ТЕМА ─────────────────────────────────────────────────────────────────

        private void OnThemeChanged(AppTheme theme)
        {
            RefreshDwmTheme();
            SyncThemeIcon();
        }

        private void RefreshDwmTheme()
        {
            bool dark = App.CurrentTheme == AppTheme.Dark;
            WindowHelper.SetTitleBarDarkMode(this, dark);
        }

        // ─── СИНХРОНИЗАЦИЯ ИКОНОК ─────────────────────────────────────────────────
        // Только изменяемые иконки обновляются через код — статичные заданы в XAML.

        private void SyncPlayIcon()
        {
            // \uE768 = Play, \uE769 = Pause (Segoe MDL2 Assets)
            TbPlayIcon.Text        = _playing ? "\uE769" : "\uE768";
            BtnPlayPause.ToolTip   = _playing ? "Пауза  Пробел" : "Пуск  Пробел";
        }

        private void SyncThemeIcon()
        {
            // \uE708 = тёмная тема (луна+звезда), \uE706 = светлая (солнце)
            bool dark = App.CurrentTheme == AppTheme.Dark;
            TbThemeIcon.Text      = dark ? "\uE708" : "\uE706";
            BtnTheme.ToolTip      = dark ? "Переключить на светлую тему" : "Переключить на тёмную тему";
        }

        private void SyncPlayModeIcon()
        {
            // \uE8B1 = Shuffle, \uE8AC = Sort (Sequential), \uEA4F = Repeat1, \uEBE9 = ChevronDown
            (string glyph, string tip) = _cfg.PlayMode switch
            {
                PlayMode.Sequential     => ("\uE8AC", "Порядок: по имени"),
                PlayMode.Shuffle        => ("\uE8B1", "Порядок: перемешать"),
                PlayMode.TrueRandom     => ("\uE74D", "Порядок: случайно"),
                PlayMode.DateAscending  => ("\uE74A", "Порядок: дата ↑"),
                PlayMode.DateDescending => ("\uE74B", "Порядок: дата ↓"),
                _                       => ("\uE8B1", "Порядок")
            };
            TbPlayModeIcon.Text  = glyph;
            BtnPlayMode.ToolTip  = tip;
        }

        private void SyncIntervalLabel()
        {
            int s = _cfg.SlideshowIntervalSeconds;
            TbInterval.Text = s >= 60 ? $"{s / 60}м{s % 60:D2}с" : $"{s}с";
        }

        // ─── ОВЕРЛЕИ ──────────────────────────────────────────────────────────────

        private void UpdateOverlays(PhotoInfo photo)
        {
            double fs = _cfg.OverlayFontSize;

            ApplyOverlay(OverlayDir,  TbDir,  _cfg.ShowDirectoryOverlay, photo.Directory, fs);
            ApplyOverlay(OverlayLoc,  TbLoc,  _cfg.ShowLocationOverlay,  photo.LocationString, fs);
            ApplyOverlay(OverlayDate, TbDate, _cfg.ShowDateOverlay,       photo.DateString, fs);
        }

        private static void ApplyOverlay(Border b, TextBlock tb, bool show, string? text, double fs)
        {
            tb.Text     = text ?? "";
            tb.FontSize = fs;
            b.Visibility = show && !string.IsNullOrEmpty(text)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowEmpty()
        {
            EmptyPanel.Visibility = Visibility.Visible;
            _engine?.ShowImmediate(null);
            TbCounter.Text = "";
        }

        // ─── ТРЕЙ ─────────────────────────────────────────────────────────────────

        private void BuildTray()
        {
            try
            {
                _tray = new System.Windows.Forms.NotifyIcon
                {
                    Text    = "PhotoFrame",
                    Visible = true
                };

                // Иконка из ресурсов
                try
                {
                    var stream = Application.GetResourceStream(
                        new Uri("pack://application:,,,/Resources/Icons/Media001.ico"))?.Stream;
                    _tray.Icon = stream != null
                        ? new System.Drawing.Icon(stream)
                        : System.Drawing.SystemIcons.Application;
                }
                catch { _tray.Icon = System.Drawing.SystemIcons.Application; }

                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Показать",      null, (_, __) => ShowFromTray());
                menu.Items.Add("Следующее",     null, (_, __) => _ = AdvanceAsync());
                menu.Items.Add("Пауза / Пуск",  null, (_, __) => { if (_playing) StopSlide(); else StartSlide(); });
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Настройки",     null, (_, __) => OpenSettings());
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Выход",         null, (_, __) => Close());
                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick     += (_, __) => ShowFromTray();
            }
            catch { /* Трей недоступен — игнорируем */ }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        // ─── ОТКРЫТЬ НАСТРОЙКИ ────────────────────────────────────────────────────

        private void OpenSettings()
        {
            try
            {
                bool wasPlaying = _playing;
                StopSlide();

                // Создаём окно настроек — Owner задаётся отдельно чтобы избежать
                // проблем с инициализацией при прямой установке в конструкторе
                var dlg = new SettingsWindow(_cfg);
                dlg.Owner = this;

                if (dlg.ShowDialog() == true)
                {
                    _cfg = dlg.Result;
                    SettingsService.Save(_cfg);

                    App.ChangeTheme(_cfg.Theme);

                    if (_cfg.EnableMicaEffect) WindowHelper.TryApplyMica(this);
                    else                       WindowHelper.RemoveMica(this);

                    SystemIntegration.PreventSleep(_cfg.PreventSleep);

                    _slideTimer.Interval = TimeSpan.FromSeconds(
                        Math.Max(1, _cfg.SlideshowIntervalSeconds));
                    SyncIntervalLabel();
                    SyncPlayModeIcon();

                    // Перезагружаем библиотеку
                    _ = ReloadPhotosAsync();
                }
                else if (wasPlaying)
                {
                    StartSlide();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка настроек:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── ОБРАБОТЧИКИ КНОПОК ───────────────────────────────────────────────────

        private async void OnBtnPrev(object s, RoutedEventArgs e)    => await GoBackAsync();
        private async void OnBtnNext(object s, RoutedEventArgs e)    => await AdvanceAsync();

        private void OnBtnPlayPause(object s, RoutedEventArgs e)
        {
            if (_playing) StopSlide(); else StartSlide();
        }

        private void OnBtnSettings(object s, RoutedEventArgs e) => OpenSettings();

        private void OnBtnTheme(object s, RoutedEventArgs e)
        {
            _cfg.Theme = App.CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            SettingsService.Save(_cfg);
            App.ChangeTheme(_cfg.Theme);
        }

        private void OnBtnPlayMode(object s, RoutedEventArgs e)
        {
            // Цикл по 5 режимам
            _cfg.PlayMode = (PlayMode)(((int)_cfg.PlayMode + 1) % 5);
            SettingsService.Save(_cfg);
            _playlist.Rebuild(_cfg.PlayMode);
            SyncPlayModeIcon();
        }

        private void OnBtnFullscreen(object s, RoutedEventArgs e)
        {
            if (_fullscreen) ExitFullscreen(); else EnterFullscreen();
        }

        private void OnIntervalDown(object s, RoutedEventArgs e)
        {
            int v = _cfg.SlideshowIntervalSeconds;
            v = v <= 3 ? 1 : v <= 10 ? v - 1 : v <= 60 ? v - 5 : v - 30;
            _cfg.SlideshowIntervalSeconds = Math.Max(1, v);
            SettingsService.Save(_cfg);
            _slideTimer.Interval = TimeSpan.FromSeconds(_cfg.SlideshowIntervalSeconds);
            SyncIntervalLabel();
        }

        private void OnIntervalUp(object s, RoutedEventArgs e)
        {
            int v = _cfg.SlideshowIntervalSeconds;
            v = v < 3 ? 3 : v < 10 ? v + 1 : v < 60 ? v + 5 : v + 30;
            _cfg.SlideshowIntervalSeconds = Math.Min(3600, v);
            SettingsService.Save(_cfg);
            _slideTimer.Interval = TimeSpan.FromSeconds(_cfg.SlideshowIntervalSeconds);
            SyncIntervalLabel();
        }

        // ─── УПРАВЛЕНИЕ ОКНОМ ─────────────────────────────────────────────────────

        private void OnTitleBarMouseDown(object s, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (_fullscreen) ExitFullscreen(); else EnterFullscreen();
                return;
            }
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* игнорируем при Maximized */ }
            }
        }

        private void OnMinimize(object s, RoutedEventArgs e)
        {
            if (_cfg.MinimizeToTray) Hide();
            else WindowState = WindowState.Minimized;
        }

        private void OnMaximize(object s, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState      = WindowState.Normal;
                TbMaxIcon.Text   = "\uE922"; // ChromeMaximize
                BtnMaximize.ToolTip = "Развернуть";
            }
            else
            {
                WindowState      = WindowState.Maximized;
                TbMaxIcon.Text   = "\uE923"; // ChromeRestore
                BtnMaximize.ToolTip = "Восстановить";
            }
        }

        private void OnClose(object s, RoutedEventArgs e) => Close();

        private void OnStateChanged(object s, EventArgs e)
        {
            if (!_fullscreen && TbMaxIcon != null)
            {
                TbMaxIcon.Text = WindowState == WindowState.Maximized
                    ? "\uE923" : "\uE922";
            }
        }

        // ─── КЛАВИАТУРА ───────────────────────────────────────────────────────────

        private void OnKeyDown(object s, KeyEventArgs e)
        {
            ShowToolbarNow();

            if (App.StartMode == AppStartMode.Screensaver)
            {
                Close(); return;
            }

            switch (e.Key)
            {
                case Key.Right: case Key.Down: case Key.PageDown:
                    _ = AdvanceAsync(); break;
                case Key.Left: case Key.Up: case Key.PageUp:
                    _ = GoBackAsync(); break;
                case Key.Space:
                    if (_playing) StopSlide(); else StartSlide(); break;
                case Key.F: case Key.F11:
                    if (_fullscreen) ExitFullscreen(); else EnterFullscreen(); break;
                case Key.Escape:
                    if (_fullscreen) ExitFullscreen(); else Close(); break;
                case Key.OemComma
                    when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    OpenSettings(); break;
            }
        }

        // ─── МЫШЬ ─────────────────────────────────────────────────────────────────

        private void OnMouseMove(object s, MouseEventArgs e) => ShowToolbarNow();

        private void OnMouseLeave(object s, MouseEventArgs e)
        {
            if (_fullscreen) _hideTimer.Start();
        }

        private void OnMouseDown(object s, MouseButtonEventArgs e)
        {
            if (App.StartMode == AppStartMode.Screensaver) { Close(); return; }

            if (e.ClickCount == 2)
            {
                if (_fullscreen) ExitFullscreen(); else EnterFullscreen();
                return;
            }

            if (e.ChangedButton == MouseButton.Left && e.Source == RootGrid)
            {
                double x = e.GetPosition(RootGrid).X;
                if (x < RootGrid.ActualWidth / 2) _ = GoBackAsync();
                else                               _ = AdvanceAsync();
            }
        }

        // ─── ТАЧ-ЖЕСТЫ ───────────────────────────────────────────────────────────

        private void OnManipulationStarted(object s, ManipulationStartedEventArgs e)
        {
            _touchStartX = e.ManipulationOrigin.X;
            e.Handled    = true;
        }

        private void OnManipulationCompleted(object s, ManipulationCompletedEventArgs e)
        {
            double dx = e.TotalManipulation.Translation.X;
            if (Math.Abs(dx) >= SwipePx)
            {
                if (dx < 0) _ = AdvanceAsync();
                else        _ = GoBackAsync();
            }
            e.Handled = true;
        }

        // ─── ЗАКРЫТИЕ ─────────────────────────────────────────────────────────────

        private void OnClosing(object s, System.ComponentModel.CancelEventArgs e)
        {
            App.ThemeChanged -= OnThemeChanged;
            _slideTimer.Stop();
            _hideTimer.Stop();
            SystemIntegration.PreventSleep(false);
            SettingsService.Save(_cfg);
            try { _tray?.Dispose(); } catch { }
        }
    }
}
