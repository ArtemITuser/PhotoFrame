// MainWindow.xaml.cs — v3.3-sp1 (UI/функционал строго v1.2.2.0; только патчи зависаний)
// - ПАТЧ sp1: итеративный пропуск битых файлов (нет рекурсии → нет StackOverflow/зависаний)
// - ПАТЧ sp1: фоновая проверка обновлений GitHub Releases (BalloonTip в трее)
// - ScanResult вместо List<PhotoInfo>: indexed photos / total files
// - Removable media watch (WMI DeviceInsertedEvent)
// - PlayMode 2 states: ⇄ / ↕ 
// - Version from Assembly in title bar

using System;
using System.IO;
using System.Management; // for WMI removable drive detection
using System.Reflection;
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
        private AppSettings  _cfg       = new();
        private TransitionEngine? _engine;
        private PlaylistManager   _playlist = new();
        private ScanResult?  _lastScan;
        private bool         _playing   = false;
        private bool         _fullscreen = false;
        private WindowState  _prevWinState;
        private string       _effectApplied = "none";

        private readonly DispatcherTimer _slideTimer = new();
        private readonly DispatcherTimer _hideTimer  = new() { Interval = TimeSpan.FromSeconds(3) };
        private bool _toolbarVisible = true;

        private double _touchStartX; // v1.2.2.0 (как в оригинале)
        private const double SwipePx = 70;

        private System.Windows.Forms.NotifyIcon? _tray;
        private ManagementEventWatcher?           _driveWatcher;

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
                _engine = new TransitionEngine(ImgA, ImgB, RootGrid);
                _cfg    = SettingsService.Load();

                // Версия в заголовке из Assembly
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                TbTitleVersion.Text = ver != null
                    ? $"PhotoFrame  v{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}"
                    : "PhotoFrame";

                RefreshDwmTheme();
                App.ThemeChanged += OnThemeChanged;

                if (_cfg.EnableMicaEffect)
                    _effectApplied = WindowHelper.TryApplyMica(this);

                SystemIntegration.PreventSleep(_cfg.PreventSleep);
                BuildTray();
                StartRemovableMediaWatcher();

                if (App.StartMode == AppStartMode.Screensaver)
                {
                    EnterFullscreen();
                    _hideTimer.Start();
                }

                SyncPlayIcon();
                SyncThemeIcon();
                SyncPlayModeIcon();
                SyncIntervalLabel();

                if (_cfg.SelectedPaths.Count > 0)
                    await ReloadPhotosAsync();
                else
                    ShowEmpty();

                // ПАТЧ 1.2.2.0-sp1: фоновая проверка обновлений (не блокирует старт;
                // при новой версии — BalloonTip в системном трее).
                if (_cfg.AutoCheckUpdates)
                    _ = CheckUpdatesSilentlyAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── ФОТОГРАФИИ ───────────────────────────────────────────────────────────

        private async Task ReloadPhotosAsync()
        {
            ScanPanel.Visibility  = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Collapsed;

            _lastScan = await FileScanner.ScanAsync(
                _cfg.SelectedPaths, _cfg.IncludeSubdirectories,
                p => Dispatcher.InvokeAsync(() => TbScanPath.Text = p));

            ScanPanel.Visibility = Visibility.Collapsed;

            if (_lastScan.Photos.Count == 0) { ShowEmpty(); return; }

            _playlist.SetPhotos(_lastScan.Photos, _cfg.PlayMode);
            await ShowCurrentAsync(animate: false);
            if (_cfg.AutoStart) StartSlide();
        }

        private async Task ShowCurrentAsync(bool animate)
        {
            var photo = _playlist.Current;
            if (photo == null) return;

            if (!photo.DateTaken.HasValue && photo.Latitude == null)
                await Task.Run(() => MetadataReader.Populate(photo));

            // ПАТЧ 1.2.2.0-sp1: вместо рекурсивного само-вызова при битом файле —
            // итеративный цикл с лимитом MaxCorruptSkip. Рекурсия на серии повреждённых
            // файлов давала StackOverflowException («вылет через некоторое время работы»),
            // а без лимита на полностью повреждённой папке — бесконечное «зависание».
            for (int attempt = 0; attempt < MaxCorruptSkip; attempt++)
            {
                var bmp = await Task.Run(() => LoadBitmap(photo.FilePath));
                if (bmp != null)
                {
                    RenderCurrent(bmp, animate);
                    return;
                }
                if (_playlist.Count <= 1) break;
                if (_playlist.Next(_cfg.PlayMode, _cfg.LoopSlideshow) == null) break;
                photo = _playlist.Current;
                if (photo == null) break;
            }
            UpdateCounter();
        }

        private const int MaxCorruptSkip = 50;

        private void RenderCurrent(BitmapSource bmp, bool animate)
        {
            var photo = _playlist.Current;
            if (photo == null) return;
            UpdateOverlays(photo);
            UpdateMoreInfo(photo); // ПАТЧ sp1

            if (animate && _engine != null && _playlist.Count > 1)
                _engine.Transition(bmp, _cfg.TransitionType, _cfg.TransitionDurationSeconds);
            else
                _engine?.ShowImmediate(bmp);

            UpdateCounter();
        }

        private void UpdateCounter()
        {
            // Счётчик: текущий / проиндексировано (всего файлов)
            int indexed = _lastScan?.Photos.Count ?? _playlist.Count;
            int total   = _lastScan?.TotalFilesScanned ?? indexed;
            TbCounter.Text = total > indexed
                ? $"{_playlist.CurrentIndex + 1} / {indexed}  [{total} файлов]"
                : $"{_playlist.CurrentIndex + 1} / {indexed}";
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
                b.DecodePixelWidth = 2560;
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch { return null; }
        }

        // ─── NAVGATION ────────────────────────────────────────────────────────────

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
            _slideTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _cfg.SlideshowIntervalSeconds));
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
            if (_toolbarVisible) { if (_fullscreen) _hideTimer.Start(); return; }
            _toolbarVisible    = true;
            Toolbar.Visibility = TitleBar.Visibility = Visibility.Visible;
            var a = new DoubleAnimation(ToolbarSlide.Y, 0,
                new Duration(TimeSpan.FromMilliseconds(180)));
            ToolbarSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, a);
            if (_fullscreen) _hideTimer.Start();
        }

        private void TryHideToolbar()
        {
            _hideTimer.Stop();
            if (!_toolbarVisible || !_fullscreen) return;
            _toolbarVisible = false;
            var a = new DoubleAnimation(0, 76,
                new Duration(TimeSpan.FromMilliseconds(280)));
            a.Completed += (_, __) =>
            {
                Toolbar.Visibility  = Visibility.Collapsed;
                TitleBar.Visibility = Visibility.Collapsed;
            };
            ToolbarSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, a);
        }

        // ─── ПОЛНЫЙ ЭКРАН ─────────────────────────────────────────────────────────

        private void EnterFullscreen()
        {
            if (_fullscreen) return;
            _prevWinState = WindowState;
            WindowStyle   = WindowStyle.None;
            WindowState   = WindowState.Maximized;
            _fullscreen   = true;
            TbFullscreenIcon.Text  = "\uE741";  // BackToWindow
            TbFullscreenLabel.Text = "Окно";
            BtnFullscreen.ToolTip  = "Оконный режим  F / F11";
        }

        private void ExitFullscreen()
        {
            if (!_fullscreen) return;
            WindowStyle   = WindowStyle.SingleBorderWindow;
            WindowState   = _prevWinState;
            _fullscreen   = false;
            ShowToolbarNow();
            TbFullscreenIcon.Text  = "\uE740";  // FullScreen
            TbFullscreenLabel.Text = "Экран";
            BtnFullscreen.ToolTip  = "Полный экран  F / F11";
        }

        // ─── ТЕМА ─────────────────────────────────────────────────────────────────

        private void OnThemeChanged(AppTheme theme)
        {
            RefreshDwmTheme();
            SyncThemeIcon();
            // Переприменяем Acrylic с правильным цветом для новой темы
            if (_cfg.EnableMicaEffect)
                _effectApplied = WindowHelper.TryApplyMica(this);
        }

        private void RefreshDwmTheme()
            => WindowHelper.SetTitleBarDarkMode(this, App.CurrentTheme == AppTheme.Dark);

        // ─── ИКОНКИ ───────────────────────────────────────────────────────────────

        private void SyncPlayIcon()
        {
            TbPlayIcon.Text  = _playing ? "\uE769" : "\uE768";
            TbPlayLabel.Text = _playing ? "Пауза"  : "Пуск";
            BtnPlayPause.ToolTip = _playing ? "Пауза  Пробел" : "Пуск  Пробел";
        }

        private void SyncThemeIcon()
        {
            bool dark = App.CurrentTheme == AppTheme.Dark;
            TbThemeIcon.Text = dark ? "\uE708" : "\uE706";
            BtnTheme.ToolTip = dark
                ? "Переключить на светлую тему"
                : "Переключить на тёмную тему";
        }

        /// <summary>
        /// PlayMode в тулбаре: 2 состояния.
        ///   ⇄ (U+21C4, Shuffle) — стрелки пересекаются
        ///   ↕ (U+2195, Sequential) — стрелки вверх-вниз (по порядку)
        /// Segoe MDL2 Assets:
        ///   \uE8B1 = Shuffle (лучше читается)
        ///   \uE8AC = Sort/Sequential
        /// </summary>
        private void SyncPlayModeIcon()
        {
            bool shuffle = _cfg.PlayMode != PlayMode.Sequential;
            TbPlayModeIcon.Text  = shuffle ? "\uE8B1" : "\uE8AC";
            TbPlayModeLabel.Text = shuffle ? "Случайно" : "По порядку";
            BtnPlayMode.ToolTip  = shuffle
                ? "Перемешать (клик — по порядку)"
                : "По порядку (клик — перемешать)";
        }

        private void SyncIntervalLabel()
        {
            int s = _cfg.SlideshowIntervalSeconds;
            TbInterval.Text = s >= 60 ? $"{s / 60}м{s % 60:D2}с" : $"{s}с";
        }

        // ─── ОВЕРЛЕИ ──────────────────────────────────────────────────────────────

        private void UpdateOverlays(PhotoInfo photo)
        {
            bool any = _cfg.ShowDirectoryOverlay || _cfg.ShowDateOverlay || _cfg.ShowLocationOverlay;
            Set(OverlayDir,  TbDir,  any && _cfg.ShowDirectoryOverlay, photo.Directory,      _cfg.OverlayFontSize);
            // ПАТЧ sp1: местоположение — регион (Крым корректно «Россия») + опционально точные координаты
            Set(OverlayLoc,  TbLoc,  any && _cfg.ShowLocationOverlay,  photo.LocationStringPrecise(_cfg.ShowExactCoordinates), _cfg.OverlayFontSize);
            Set(OverlayDate, TbDate, any && _cfg.ShowDateOverlay,       photo.DateString,     _cfg.OverlayFontSize);
        }

        private static void Set(
            System.Windows.Controls.Border b,
            System.Windows.Controls.TextBlock tb,
            bool show, string? text, double fs)
        {
            tb.Text = text ?? ""; tb.FontSize = fs;
            b.Visibility = show && !string.IsNullOrEmpty(text)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─── ПАТЧ sp1: панель «Больше…» (опциональные EXIF-сведения) ─────────────

        private DispatcherTimer? _moreHideTimer;

        private void UpdateMoreInfo(PhotoInfo photo)
        {
            string? txt = photo.BuildMoreInfo(_cfg);
            TbMoreInfo.Text = txt ?? "";
            if (txt == null) MoreInfoPanel.Visibility = Visibility.Collapsed;
            // если включено пользователем ("показывать постоянно" = вкладка Больше… с полями),
            // панель остаётся видимой; иначе скрывается через 6 c после вызова
            else if (_moreHideTimer != null) MoreInfoPanel.Visibility = Visibility.Visible;
            else MoreInfoPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Открыть панель «Больше…» для текущего фото (жест/клавиша I).</summary>
        public void ShowMoreInfoPanel()
        {
            var photo = _playlist.Current;
            if (photo == null) return;
            string? txt = photo.BuildMoreInfo(_cfg);
            TbMoreInfo.Text = txt ?? "Включите нужные поля в Настройки → Больше…";
            MoreInfoPanel.Visibility = Visibility.Visible;

            _moreHideTimer?.Stop();
            _moreHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _moreHideTimer.Tick += (_, __) =>
            { _moreHideTimer!.Stop(); MoreInfoPanel.Visibility = Visibility.Collapsed; };
            _moreHideTimer.Start();
        }

        private void ShowEmpty()
        {
            EmptyPanel.Visibility = Visibility.Visible;
            _engine?.ShowImmediate(null);
            TbCounter.Text = "";
        }

        // ─── REMOVABLE MEDIA WATCHER ─────────────────────────────────────────────

        private void StartRemovableMediaWatcher()
        {
            if (!_cfg.WatchRemovableMedia) return;
            try
            {
                _driveWatcher = new ManagementEventWatcher(
                    new WqlEventQuery(
                        "SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2"));
                _driveWatcher.EventArrived += OnDriveInserted;
                _driveWatcher.Start();
            }
            catch { /* WMI недоступен — игнорируем */ }
        }

        private async void OnDriveInserted(object sender, EventArrivedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Ищем съёмные носители с фото
                    var removable = await FileScanner.GetRemovableWithPhotosAsync();
                    if (removable.Count == 0) return;

                    string drives = string.Join(", ", removable.ConvertAll(d => d.Name));
                    if (_cfg.SuggestRemovableMedia)
                    {
                        var r = MessageBox.Show(
                            $"Обнаружены фото на съёмном носителе: {drives}\n" +
                            "Добавить в список источников?",
                            "PhotoFrame", MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (r == MessageBoxResult.Yes)
                        {
                            foreach (var d in removable)
                                if (!_cfg.SelectedPaths.Contains(d.RootDirectory.FullName))
                                    _cfg.SelectedPaths.Add(d.RootDirectory.FullName);

                            SettingsService.Save(_cfg);
                            await ReloadPhotosAsync();
                        }
                    }
                    else if (_cfg.WatchRemovableMedia)
                    {
                        // Тихое добавление без вопроса
                        bool added = false;
                        foreach (var d in removable)
                            if (!_cfg.SelectedPaths.Contains(d.RootDirectory.FullName))
                            { _cfg.SelectedPaths.Add(d.RootDirectory.FullName); added = true; }
                        if (added) { SettingsService.Save(_cfg); await ReloadPhotosAsync(); }
                    }
                }
                catch { }
            });
        }

        // ─── ТРЕЙ ────────────────────────────────────────────────────────────────

        private void BuildTray()
        {
            try
            {
                _tray = new System.Windows.Forms.NotifyIcon { Text = "PhotoFrame", Visible = true };
                try
                {
                    var s = Application.GetResourceStream(
                        new Uri("pack://application:,,,/Resources/Icons/AppIcon.ico"))?.Stream;
                    _tray.Icon = s != null
                        ? new System.Drawing.Icon(s)
                        : System.Drawing.SystemIcons.Application;
                }
                catch { _tray.Icon = System.Drawing.SystemIcons.Application; }

                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Показать",     null, (_, __) => ShowFromTray());
                menu.Items.Add("Следующее",    null, (_, __) => _ = AdvanceAsync());
                menu.Items.Add("Пауза / Пуск", null, (_, __) => { if (_playing) StopSlide(); else StartSlide(); });
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Настройки",    null, (_, __) => OpenSettings());
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Выход",        null, (_, __) => Close());
                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick     += (_, __) => ShowFromTray();
            }
            catch { }
        }

        private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }

        // ─── НАСТРОЙКИ ────────────────────────────────────────────────────────────

        private void OpenSettings()
        {
            try
            {
                bool was = _playing;
                StopSlide();
                var dlg = new SettingsWindow(_cfg) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    _cfg = dlg.Result;
                    SettingsService.Save(_cfg);
                    App.ChangeTheme(_cfg.Theme);
                    if (_cfg.EnableMicaEffect) _effectApplied = WindowHelper.TryApplyMica(this);
                    else                       WindowHelper.RemoveMica(this);
                    SystemIntegration.PreventSleep(_cfg.PreventSleep);
                    _slideTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _cfg.SlideshowIntervalSeconds));
                    SyncIntervalLabel();
                    SyncPlayModeIcon();
                    // Перезапуск watcher если изменилась настройка
                    _driveWatcher?.Stop();
                    _driveWatcher?.Dispose();
                    _driveWatcher = null;
                    StartRemovableMediaWatcher();
                    _ = ReloadPhotosAsync();
                }
                else if (was) StartSlide();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка настроек:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── КНОПКИ ───────────────────────────────────────────────────────────────

        private async void OnBtnPrev(object s, RoutedEventArgs e)    => await GoBackAsync();
        private async void OnBtnNext(object s, RoutedEventArgs e)    => await AdvanceAsync();
        private void OnBtnPlayPause(object s, RoutedEventArgs e)
        { if (_playing) StopSlide(); else StartSlide(); }
        private void OnBtnSettings(object s, RoutedEventArgs e)      => OpenSettings();
        private void OnBtnFullscreen(object s, RoutedEventArgs e)
        { if (_fullscreen) ExitFullscreen(); else EnterFullscreen(); }

        private void OnBtnTheme(object s, RoutedEventArgs e)
        {
            _cfg.Theme = App.CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            SettingsService.Save(_cfg);
            App.ChangeTheme(_cfg.Theme);
        }

        /// <summary>Переключает 2 состояния: Shuffle ↔ Sequential.</summary>
        private void OnBtnPlayMode(object s, RoutedEventArgs e)
        {
            _cfg.PlayMode = _cfg.PlayMode == PlayMode.Sequential
                ? PlayMode.Shuffle
                : PlayMode.Sequential;
            SettingsService.Save(_cfg);
            _playlist.Rebuild(_cfg.PlayMode);
            SyncPlayModeIcon();
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
            if (e.ClickCount == 2) { if (_fullscreen) ExitFullscreen(); else EnterFullscreen(); return; }
            if (e.LeftButton == MouseButtonState.Pressed)
                try { DragMove(); } catch { }
        }

        private void OnMinimize(object s, RoutedEventArgs e)
        { if (_cfg.MinimizeToTray) Hide(); else WindowState = WindowState.Minimized; }

        private void OnMaximize(object s, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            { WindowState = WindowState.Normal;    TbMaxIcon.Text = "\uE922"; }
            else
            { WindowState = WindowState.Maximized; TbMaxIcon.Text = "\uE923"; }
        }

        private void OnClose(object s, RoutedEventArgs e) => Close();

        private void OnStateChanged(object s, EventArgs e)
        {
            if (!_fullscreen && TbMaxIcon != null)
                TbMaxIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        // ─── КЛАВИАТУРА ───────────────────────────────────────────────────────────

        private void OnKeyDown(object s, KeyEventArgs e)
        {
            ShowToolbarNow();
            if (App.StartMode == AppStartMode.Screensaver) { Close(); return; }
            switch (e.Key)
            {
                case Key.Right: case Key.Down: case Key.PageDown: _ = AdvanceAsync(); break;
                case Key.Left:  case Key.Up:   case Key.PageUp:   _ = GoBackAsync();  break;
                case Key.Space: if (_playing) StopSlide(); else StartSlide();          break;
                case Key.F: case Key.F11:
                    if (_fullscreen) ExitFullscreen(); else EnterFullscreen();         break;
                case Key.Escape:
                    if (_fullscreen) ExitFullscreen(); else Close();                   break;
                case Key.OemComma when e.KeyboardDevice.Modifiers == ModifierKeys.Control:
                    OpenSettings(); break;
                // ПАТЧ sp1: «I» — показать панель «Больше…» (опциональные EXIF-сведения)
                case Key.I:
                    ShowMoreInfoPanel(); break;
            }
        }

        // ─── МЫШЬ ─────────────────────────────────────────────────────────────────

        private void OnMouseMove(object s, MouseEventArgs e) => ShowToolbarNow();
        private void OnMouseLeave(object s, MouseEventArgs e) { if (_fullscreen) _hideTimer.Start(); }

        private void OnMouseDown(object s, MouseButtonEventArgs e)
        {
            if (App.StartMode == AppStartMode.Screensaver) { Close(); return; }
            if (e.ClickCount == 2)
            { if (_fullscreen) ExitFullscreen(); else EnterFullscreen(); return; }
            if (e.ChangedButton == MouseButton.Left && e.Source == RootGrid)
            {
                if (e.GetPosition(RootGrid).X < RootGrid.ActualWidth / 2) _ = GoBackAsync();
                else                                                        _ = AdvanceAsync();
            }
        }

        // ─── ТАЧ ─────────────────────────────────────────────────────────────────
        // ПАТЧ 1.2.2.0-sp1: рабочий тач-жест (свайп).
        // Причина «не работал» в v1.2.2.0 — в OnManipulationStarted стоял e.Handled=true,
        // из-за чего WPF не генерировал ни ManipulationDelta, ни ManipulationCompleted.
        // Свойства args (ManipulationOrigin / TotalManipulation.Translation) и обработчики
        // оставлены СТРОГО как в v1.2.2.0 — только снят Handled и добавлен ShowToolbarNow.
        // Логика тулбара/кнопок/перелистывания v1.2.2.0 не затронута.

        private void OnManipulationStarted(object s, ManipulationStartedEventArgs e)
        { _touchStartX = e.ManipulationOrigin.X; /* НЕ handled — иначе Delta/Completed не придут */ }

        private void OnManipulationDelta(object s, ManipulationDeltaEventArgs e)
        {
            // ПОКАЗАТЕЛЬНОЕ событие жеста (тулбар); перелистывание — в Completed,
            // чтобы не срабатывало несколько раз на один свайп.
            ShowToolbarNow();
        }

        private void OnManipulationCompleted(object s, ManipulationCompletedEventArgs e)
        {
            double dx = e.TotalManipulation.Translation.X;
            if (Math.Abs(dx) >= SwipePx) { ShowToolbarNow(); if (dx < 0) _ = AdvanceAsync(); else _ = GoBackAsync(); }
            e.Handled = true;
        }

        // ─── ЗАКРЫТИЕ ─────────────────────────────────────────────────────────────

        private void OnClosing(object s, System.ComponentModel.CancelEventArgs e)
        {
            App.ThemeChanged -= OnThemeChanged;
            _slideTimer.Stop(); _hideTimer.Stop();
            _driveWatcher?.Stop(); _driveWatcher?.Dispose();
            SystemIntegration.PreventSleep(false);
            SettingsService.Save(_cfg);
            try { _tray?.Dispose(); } catch { }
        }

        /// <summary>ПАТЧ 1.2.2.0-sp1: тихая проверка обновлений; при новой версии — BalloonTip в трее.</summary>
        private async Task CheckUpdatesSilentlyAsync()
        {
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
                var res = await UpdateService.CheckAsync(ver);
                if (res.Ok && res.IsNewer && res.Update != null && _tray != null)
                {
                    _tray.Visible = true;
                    _tray.BalloonTipTitle = "PhotoFrame: доступно обновление";
                    _tray.BalloonTipText  = $"Версия {res.Update.Tag}. Откройте Настройки → Обновления.";
                    _tray.ShowBalloonTip(8000);
                }
            }
            catch { /* сеть недоступна — молча пропускаем */ }
        }
    }
}
