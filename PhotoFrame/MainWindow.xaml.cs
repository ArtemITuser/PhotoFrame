// MainWindow.xaml.cs — v3.7 (build 52)
//
// TOUCH ARCHITECTURE:
//   • Кнопки тулбара — стандартный Click (работает и мышью, и тачем).
//   • Stylus.IsFlicksEnabled=False на каждой кнопке — без системных жестов.
//   • SwipeZone (Rectangle над фото) — ManipulationStarting/Delta для свайпов.
//   • Window НЕ имеет IsManipulationEnabled — не крадёт touch у кнопок.
//
// TRANSPARENCY:
//   AllowsTransparency=True + WindowStyle=None + Background=Transparent.
//   DWM Acrylic (Win10) / Mica (Win11) применяется через WindowHelper.
//
// FULLSCREEN:
//   WindowStyle остаётся None всегда (AllowsTransparency требует этого).
//   Fullscreen = WindowState.Maximized + TitleBar скрыт.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
        private AppSettings       _cfg      = new();
        private TransitionEngine? _engine;
        private PlaylistManager   _playlist = new();
        private ScanResult?       _lastScan;
        private bool              _playing    = false;
        private bool              _fullscreen = false;
        private string            _effect    = "none";

        private readonly DispatcherTimer _slideTimer  = new();
        private readonly DispatcherTimer _hideTimer   = new()
            { Interval = TimeSpan.FromSeconds(3) };
        private readonly DispatcherTimer _counterTimer = new()
            { Interval = TimeSpan.FromSeconds(4) };
        private bool _toolbarVisible = true;

        private System.Windows.Forms.NotifyIcon? _tray;
        private ManagementEventWatcher?           _driveWatcher;
        private PhotoInfo? _contextPhoto;

        private List<DiskError> _diskErrors      = new();
        private int             _currentErrorIdx = 0;
        private HashSet<string> _dismissedPaths  = new(StringComparer.OrdinalIgnoreCase);
        private DateTime        _lastTileUpdateUtc = DateTime.MinValue;

        // Истинный простой системы — для скринсейвера из трея (см. OnIdleCheckTick)
        private readonly DispatcherTimer _idleCheckTimer = new()
            { Interval = TimeSpan.FromSeconds(20) };

        // Автоотключение рамки по расписанию (умный/ручное/закат-рассвет)
        private readonly AutoOffScheduler _autoOff = new();
        private bool _autoOffHidden = false; // true, если окно скрыто расписанием (не вручную)

        public MainWindow()
        {
            InitializeComponent();
            _slideTimer.Tick    += async (_, __) => await AdvanceAsync();
            _hideTimer.Tick     += (_, __) => TryHideToolbar();
            _counterTimer.Tick  += (_, __) => HideCounter();
            _idleCheckTimer.Tick += OnIdleCheckTick;
            _autoOff.ShouldBeActiveChanged += OnAutoOffShouldBeActiveChanged;
        }

        // ═══ LOADED ══════════════════════════════════════════════════════════════

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LiveTileService.SetAppUserModelId();
                _engine = new TransitionEngine(ImgA, ImgB, RootGrid);
                _cfg    = SettingsService.Load();
                _dismissedPaths = SystemIntegration.LoadDismissedPaths();

                // Window/app name only — no version number in UI (per user request)
                TbTitleVersion.Text = "PhotoFrame";
                Title = "PhotoFrame";

                // Apply UI mode (Modern/Aero7) — loads AeroTheme.xaml on demand
                App.ApplyUiMode(_cfg.UiMode);
                ApplyUiModeStyles();

                // Apply DWM backdrop BEFORE showing content
                RefreshDwmTheme();
                App.ThemeChanged += OnThemeChanged;

                ApplyBackdrop();

                SystemIntegration.PreventSleep(_cfg.PreventSleep);
                BuildTray();
                StartRemovableWatcher();
                _ = CheckForUpdatesIfDueAsync();

                // Истинный простой системы (для скринсейвера из трея) и
                // расписание автоотключения рамки — оба работают в фоне
                // независимо от того, свёрнуто окно или нет.
                _idleCheckTimer.Start();
                _autoOff.Settings = _cfg;
                _autoOff.Start();

                if (App.StartMode == AppStartMode.Screensaver)
                {
                    EnterFullscreen();
                    _hideTimer.Start();
                }

                // Apply Aero7 icons if mode set
                ApplyToolbarIcons();

                SyncPlayIcon();
                SyncThemeIcon();
                SyncPlayModeIcon();
                SyncIntervalLabel();

                if (_cfg.SelectedPaths.Count > 0)
                    await ReloadPhotosAsync();
                else
                    ShowEmpty();

                // Re-apply icons now that playlist state (Count/CurrentIndex) is known
                ApplyToolbarIcons();

                // Win7/8: предложить установить Segoe MDL2 Assets. Никогда не
                // показывается на Windows 10/11 (см. ShouldOfferMdl2Font).
                if (App.StartMode == AppStartMode.Normal
                    && SystemIntegration.ShouldOfferMdl2Font())
                    await OfferMdl2FontInstallAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyBackdrop()
        {
            if (_cfg.EnableMicaEffect)
            {
                _effect = WindowHelper.TryApplyBackdrop(this,
                    App.CurrentTheme == AppTheme.Dark);
            }
            else
            {
                // No DWM effect — semi-transparent solid fallback
                bool dark = App.CurrentTheme == AppTheme.Dark;
                Background = new SolidColorBrush(dark
                    ? Color.FromArgb(0xCC, 0x11, 0x11, 0x11)
                    : Color.FromArgb(0xCC, 0xF0, 0xF0, 0xF0));
            }
        }

        // ═══ PHOTOS ══════════════════════════════════════════════════════════════

        private async Task ReloadPhotosAsync()
        {
            ScanPanel.Visibility       = Visibility.Visible;
            EmptyPanel.Visibility      = Visibility.Collapsed;
            DiskErrorBanner.Visibility = Visibility.Collapsed;
            ContextPopup.IsOpen        = false;

            _lastScan = await FileScanner.ScanAsync(
                _cfg.SelectedPaths, _cfg.IncludeSubdirectories,
                p => Dispatcher.InvokeAsync(() => TbScanPath.Text = p));

            ScanPanel.Visibility = Visibility.Collapsed;

            if (_lastScan.DiskErrors.Count > 0)
            {
                // Не показываем баннер повторно для путей, которые пользователь
                // уже закрыл крестиком (см. OnCloseDiskError) — до тех пор, пока
                // они не вернутся в строй (т.е. перестанут встречаться в
                // DiskErrors) или пользователь не очистит стоп-лист вручную.
                _diskErrors = _lastScan.DiskErrors
                    .Where(err => !_dismissedPaths.Contains(err.Path))
                    .ToList();
                _currentErrorIdx = 0;
                if (_diskErrors.Count > 0) ShowDiskError(_diskErrors[0]);
            }

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
                await Task.Run(() => { try { MetadataReader.Populate(photo); } catch { } });

            var bmp = await Task.Run(() => LoadBitmapSafe(photo.FilePath));
            if (bmp == null)
            {
                if (_playlist.Count > 1)
                { _playlist.Next(_cfg.PlayMode, _cfg.LoopSlideshow); await ShowCurrentAsync(animate); }
                return;
            }

            UpdateOverlays(photo);

            if (animate && _engine != null && _playlist.Count > 1)
                _engine.Transition(bmp, _cfg.TransitionType, _cfg.TransitionDurationSeconds);
            else
                _engine?.ShowImmediate(bmp);

            // Live Tile: обновляем не на каждом фото, а по собственному интервалу
            // (LiveTileCycleIntervalSeconds; 0 = синхронно с интервалом слайдшоу).
            if (_cfg.LiveTilesEnabled)
            {
                int tileInt = _cfg.LiveTileCycleIntervalSeconds > 0
                    ? _cfg.LiveTileCycleIntervalSeconds
                    : Math.Max(1, _cfg.SlideshowIntervalSeconds);
                if ((DateTime.UtcNow - _lastTileUpdateUtc).TotalSeconds >= tileInt)
                { LiveTileService.UpdateTile(photo.FilePath, _cfg.TilePhotoDistance); _lastTileUpdateUtc = DateTime.UtcNow; }
            }

            // GPS reverse-геокодирование (опционально). Не блокирует показ фото —
            // результат подставляется в оверлей асинхронно, если придёт ответ.
            if (_cfg.GpsReverseGeocodeEnabled && photo.Latitude.HasValue
                && photo.Longitude.HasValue && string.IsNullOrEmpty(photo.ResolvedLocationName))
                _ = ResolveLocationAsync(photo);

            SyncCounter();
            ContextPopup.IsOpen = false;
        }

        /// <summary>
        /// Асинхронно резолвит читаемое название места через ReverseGeocodeService
        /// и обновляет оверлей, если это всё ещё текущее отображаемое фото
        /// (защита от гонки при быстрой навигации вперёд/назад).
        /// </summary>
        private async Task ResolveLocationAsync(PhotoInfo photo)
        {
            try
            {
                string? name = await ReverseGeocodeService.ResolveAsync(
                    photo.Latitude!.Value, photo.Longitude!.Value);
                if (string.IsNullOrEmpty(name)) return;
                photo.ResolvedLocationName = name;
                if (ReferenceEquals(_playlist.Current, photo))
                    Dispatcher.Invoke(() => UpdateOverlays(photo));
            }
            catch { /* сеть недоступна — остаются сырые координаты */ }
        }

        private static BitmapImage? LoadBitmapSafe(string path)
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
                b.EndInit(); b.Freeze();
                return b;
            }
            catch { return null; }
        }

        private void SyncCounter()
        {
            if (_cfg.CounterDisplayFormat == CounterFormat.Hidden)
            { TbCounter.Text = ""; CounterBadge.Visibility = Visibility.Collapsed; return; }

            CounterBadge.Visibility = Visibility.Visible;
            int photos = _lastScan?.Photos.Count ?? _playlist.Count;
            TbCounter.Text = _cfg.CounterDisplayFormat == CounterFormat.WithTotal
                && _lastScan?.TotalFilesScanned > photos
                ? $"{_playlist.PlaybackPosition+1} / {photos}  [{_lastScan!.TotalFilesScanned} файлов]"
                : $"{_playlist.PlaybackPosition+1} / {photos}";

            CounterBadge.Opacity = 1;
            _counterTimer.Stop();
            _counterTimer.Start();
        }

        private void HideCounter()
        {
            _counterTimer.Stop();
            var a = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(0.8)));
            CounterBadge.BeginAnimation(OpacityProperty, a);
        }

        // ═══ NAVIGATION ══════════════════════════════════════════════════════════

        private async Task AdvanceAsync()
        {
            if (_engine?.IsTransitioning == true) return;
            _playlist.Next(_cfg.PlayMode, _cfg.LoopSlideshow);
            await ShowCurrentAsync(animate: true);
            ApplyToolbarIcons(); // refresh Start/End-unavailable (Aero7)
        }

        private async Task GoBackAsync()
        {
            if (_engine?.IsTransitioning == true) return;
            _playlist.Prev(_cfg.LoopSlideshow);
            await ShowCurrentAsync(animate: true);
            ApplyToolbarIcons(); // refresh Start/End-unavailable (Aero7)
        }

        // ═══ SLIDESHOW ═══════════════════════════════════════════════════════════

        private void StartSlide()
        {
            _slideTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _cfg.SlideshowIntervalSeconds));
            _slideTimer.Start(); _playing = true; SyncPlayIcon();
        }

        private void StopSlide()
        { _slideTimer.Stop(); _playing = false; SyncPlayIcon(); }

        // ═══ TOOLBAR SHOW/HIDE ═══════════════════════════════════════════════════

        private void ShowToolbarNow()
        {
            _hideTimer.Stop();
            if (_toolbarVisible) { if (_fullscreen) _hideTimer.Start(); return; }
            _toolbarVisible    = true;
            Toolbar.Visibility = Visibility.Visible;
            var a = new DoubleAnimation(ToolbarSlide.Y, 0,
                new Duration(TimeSpan.FromMilliseconds(180)));
            ToolbarSlide.BeginAnimation(TranslateTransform.YProperty, a);
            if (_fullscreen) _hideTimer.Start();
        }

        private void TryHideToolbar()
        {
            _hideTimer.Stop();
            if (!_toolbarVisible || !_fullscreen) return;
            _toolbarVisible = false;
            var a = new DoubleAnimation(0, 72, new Duration(TimeSpan.FromMilliseconds(280)));
            a.Completed += (_, __) => Toolbar.Visibility = Visibility.Collapsed;
            ToolbarSlide.BeginAnimation(TranslateTransform.YProperty, a);
        }

        // ═══ FULLSCREEN (WindowStyle stays None always) ══════════════════════════

        private void EnterFullscreen()
        {
            if (_fullscreen) return;
            WindowState            = WindowState.Maximized;
            ResizeMode             = ResizeMode.NoResize;
            TitleBar.Visibility    = Visibility.Collapsed;
            _fullscreen            = true;
            TbFullscreenIcon.Text  = "\uE741";
            TbFullscreenLabel.Text = "Окно";
            _hideTimer.Start();
        }

        private void ExitFullscreen()
        {
            if (!_fullscreen) return;
            WindowState            = WindowState.Normal;
            ResizeMode             = ResizeMode.CanResize;
            TitleBar.Visibility    = Visibility.Visible;
            _fullscreen            = false;
            _hideTimer.Stop();
            ShowToolbarNow();
            Toolbar.Visibility     = Visibility.Visible;
            TbFullscreenIcon.Text  = "\uE740";
            TbFullscreenLabel.Text = "Экран";
        }

        // ═══ THEME ═══════════════════════════════════════════════════════════════

        private void OnThemeChanged(AppTheme t)
        {
            RefreshDwmTheme();
            SyncThemeIcon();
            ApplyBackdrop();
        }

        private void RefreshDwmTheme()
            => WindowHelper.SetTitleBarDarkMode(this, App.CurrentTheme == AppTheme.Dark);

        // ═══ ICON SYNC ═══════════════════════════════════════════════════════════
        //
        // Modern mode : Segoe MDL2 glyphs only (IconHelper.RestoreMdl2).
        // Aero7 mode  : state-aware PNG icons via IconHelper.SwapAeroStateIcon —
        //               hover/pressed PNG variants activate automatically through
        //               DataTriggers bound to the ancestor Button (no extra
        //               event handlers needed). Disabled/Unavailable variants
        //               are applied explicitly when the action is not possible.

        /// <summary>
        /// Applies Aero7 PNG icons (with hover/pressed states) to the toolbar's
        /// navigation buttons, or restores Segoe MDL2 glyphs for Modern mode.
        /// Called on load, after UiMode change, and whenever playback/navigation
        /// state changes (so disabled/unavailable states stay in sync).
        /// </summary>
        private void ApplyToolbarIcons()
        {
            if (_cfg.UiMode == UiMode.Aero7)
            {
                bool atStart = _playlist.IsAtStart && !_cfg.LoopSlideshow;
                bool atEnd   = _playlist.IsAtEnd   && !_cfg.LoopSlideshow;
                bool noPhotos = _playlist.Count == 0;

                // BtnPrev → Start* icon set (with Unavailable when at first photo)
                if (noPhotos || atStart)
                    IconHelper.SwapIcon(BtnPrev, IconRole.StartUnavailable, 20);
                else
                    IconHelper.SwapAeroStateIcon(BtnPrev,
                        IconRole.Start, IconRole.StartHover, IconRole.StartClicked, 20);

                // BtnNext → End* icon set (with Unavailable when at last photo)
                if (noPhotos || atEnd)
                    IconHelper.SwapIcon(BtnNext, IconRole.EndUnavailable, 20);
                else
                    IconHelper.SwapAeroStateIcon(BtnNext,
                        IconRole.End, IconRole.EndHover, IconRole.EndClicked, 20);

                // BtnPlayPause handled by SyncPlayIcon() (depends on _playing)
                SyncPlayIcon();
            }
            else
            {
                IconHelper.RestoreMdl2(BtnPrev, "\uE892", 20);
                IconHelper.RestoreMdl2(BtnNext, "\uE893", 20);
                IconHelper.RestoreMdl2(BtnPlayPause,
                    _playing ? "\uE769" : "\uE768", 24);
            }
        }

        /// <summary>
        /// Applies Aero7CaptionButton/Aero7CloseButton styles to the custom
        /// TitleBar buttons, and Aero7ToolbarButton to the bottom toolbar
        /// buttons, when UiMode.Aero7 is active. In Modern mode the styles
        /// fall back to the ones defined in CommonStyles.xaml (DynamicResource).
        /// </summary>
        private void ApplyUiModeStyles()
        {
            bool aero = _cfg.UiMode == UiMode.Aero7;

            // ── TitleBar caption buttons ──────────────────────────────────────
            // XAML defines NO explicit Style for these (default WPF chrome +
            // inline Background=Transparent). In Aero7 we apply the glass
            // caption styles; switching back to Modern clears the override so
            // the original default chrome returns.
            if (aero)
            {
                var cap   = TryFindResource("Aero7CaptionButton") as Style;
                var close = TryFindResource("Aero7CloseButton")   as Style;
                if (cap   != null) { BtnWinMinimize.Style = cap; BtnWinMaximize.Style = cap; }
                if (close != null) BtnWinClose.Style = close;
            }
            else
            {
                BtnWinMinimize.ClearValue(StyleProperty);
                BtnWinMaximize.ClearValue(StyleProperty);
                BtnWinClose.ClearValue(StyleProperty);
            }

            // ── Bottom toolbar buttons ─────────────────────────────────────────
            // XAML sets Style="{DynamicResource ToolbarButton}". In Aero7 we
            // override with the glass Aero7ToolbarButton; switching back to
            // Modern restores the original DynamicResource binding via
            // SetResourceReference (so theme changes keep working too).
            var toolbarBtns = new[]
            {
                BtnPlayMode, BtnIntervalDown, BtnIntervalUp,
                BtnPrev, BtnPlayPause, BtnNext,
                BtnTheme, BtnSettings, BtnFullscreen
            };

            if (aero)
            {
                var aeroStyle = TryFindResource("Aero7ToolbarButton") as Style;
                if (aeroStyle != null)
                    foreach (var btn in toolbarBtns) btn.Style = aeroStyle;
            }
            else
            {
                foreach (var btn in toolbarBtns)
                    btn.SetResourceReference(StyleProperty, "ToolbarButton");
            }
        }

        /// <summary>
        /// Updates the central Play/Pause(Stop) button icon and label based on
        /// <see cref="_playing"/>. In Aero7 mode this swaps between the Play*
        /// and Stop* PNG state-sets (hover/pressed react automatically via
        /// DataTriggers); a PlayDisabled icon is shown when the playlist is empty.
        /// In Modern mode only the Segoe MDL2 glyph/label change.
        /// </summary>
        private void SyncPlayIcon()
        {
            if (_cfg.UiMode == UiMode.Aero7)
            {
                if (_playlist.Count == 0)
                {
                    IconHelper.SwapIcon(BtnPlayPause, IconRole.PlayDisabled, 26);
                }
                else if (_playing)
                {
                    // "Playing" → button now represents STOP
                    IconHelper.SwapAeroStateIcon(BtnPlayPause,
                        IconRole.Stop, IconRole.StopHover, IconRole.StopClicked, 26);
                }
                else
                {
                    IconHelper.SwapAeroStateIcon(BtnPlayPause,
                        IconRole.Play, IconRole.PlayHover, IconRole.PlayClicked, 26);
                }
            }
            else
            {
                // Use RestoreMdl2 (not TbPlayIcon.Text) so this stays correct
                // even after the icon element was previously swapped by Aero7.
                IconHelper.RestoreMdl2(BtnPlayPause,
                    _playing ? "\uE769" : "\uE768", 26);
            }

            TbPlayLabel.Text = _playing ? "Пауза" : "Пуск";
        }

        private void SyncThemeIcon()
        {
            bool dark = App.CurrentTheme == AppTheme.Dark;
            TbThemeIcon.Text = dark ? "\uE708" : "\uE706";
        }

        private void SyncPlayModeIcon()
        {
            bool shuffle = _cfg.PlayMode != PlayMode.Sequential;
            TbPlayModeIcon.Text  = shuffle ? "\uE8B1" : "\uE8AC";
            TbPlayModeLabel.Text = shuffle ? "Случайно" : "По порядку";
        }

        private void SyncIntervalLabel()
        {
            int s = _cfg.SlideshowIntervalSeconds;
            TbInterval.Text = s >= 60 ? $"{s/60}м{s%60:D2}с" : $"{s}с";
        }

        // ═══ OVERLAYS ════════════════════════════════════════════════════════════

        private void UpdateOverlays(PhotoInfo photo)
        {
            SetOverlay(OverlayDir,  TbDir,  _cfg.ShowDirectoryOverlay, photo.Directory,      _cfg.OverlayFontSize);
            SetOverlay(OverlayLoc,  TbLoc,  _cfg.ShowLocationOverlay,  photo.LocationString, _cfg.OverlayFontSize);
            SetOverlay(OverlayDate, TbDate, _cfg.ShowDateOverlay,       photo.DateString,     _cfg.OverlayFontSize);
        }

        private static void SetOverlay(System.Windows.Controls.Border b,
            System.Windows.Controls.TextBlock tb, bool show, string? text, double fs)
        {
            tb.Text = text ?? ""; tb.FontSize = fs;
            b.Visibility = show && !string.IsNullOrEmpty(text)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowEmpty()
        {
            EmptyPanel.Visibility = Visibility.Visible;
            _engine?.ShowImmediate(null);
            TbCounter.Text = "";
        }

        // ═══ DISK ERRORS ═════════════════════════════════════════════════════════

        private void ShowDiskError(DiskError err)
        {
            string cnt = _diskErrors.Count > 1 ? $" ({_currentErrorIdx+1}/{_diskErrors.Count})" : "";
            TbDiskErrorMsg.Text =
                $"⚠ Проблема{cnt}: {err.Path}\n{err.Message}" +
                (err.Kind == ErrorKind.IoError ? "\nДиск повреждён. Файлы пропущены." : "");
            BtnChkdsk.Visibility = err.Kind == ErrorKind.IoError && err.Volume != null
                ? Visibility.Visible : Visibility.Collapsed;
            BtnChkdsk.Tag              = err.Volume;
            DiskErrorBanner.Visibility = Visibility.Visible;
        }

        private void OnChkdsk(object s, RoutedEventArgs e)
        {
            string? vol = (BtnChkdsk.Tag as string)?.TrimEnd('\\');
            if (string.IsNullOrEmpty(vol)) return;
            if (MessageBox.Show($"Запустить chkdsk {vol} /r ?\n\n⚠ Потребуется перезагрузка.",
                "Проверка диска", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName="cmd.exe", Arguments=$"/k chkdsk {vol} /r", Verb="runas", UseShellExecute=true }); }
            catch { }
        }

        private void OnRemoveErrorPath(object s, RoutedEventArgs e)
        {
            if (_currentErrorIdx >= _diskErrors.Count) return;
            var err = _diskErrors[_currentErrorIdx];
            if (MessageBox.Show($"Удалить из списка?\n\n{err.Path}",
                "Удалить путь", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;
            _cfg.SelectedPaths.RemoveAll(p =>
                p.StartsWith(err.Path, StringComparison.OrdinalIgnoreCase));
            SettingsService.Save(_cfg);
            // Путь удалён насовсем — больше нет смысла держать его в стоп-листе
            _dismissedPaths.Remove(err.Path);
            SystemIntegration.SaveDismissedPaths(_dismissedPaths);
            _diskErrors.RemoveAt(_currentErrorIdx);
            if (_currentErrorIdx >= _diskErrors.Count) _currentErrorIdx = 0;
            if (_diskErrors.Count > 0) ShowDiskError(_diskErrors[_currentErrorIdx]);
            else DiskErrorBanner.Visibility = Visibility.Collapsed;
        }

        private void OnCloseDiskError(object s, RoutedEventArgs e)
        {
            // Закрытие крестиком ≠ удаление из источников: путь остаётся в
            // SelectedPaths, но баннер для него больше не показывается при
            // следующих запусках — пока он не вернётся в строй или пользователь
            // не очистит стоп-лист вручную (см. SystemIntegration.ClearDismissedPaths).
            if (_currentErrorIdx < _diskErrors.Count)
            {
                _dismissedPaths.Add(_diskErrors[_currentErrorIdx].Path);
                SystemIntegration.SaveDismissedPaths(_dismissedPaths);
            }
            _currentErrorIdx++;
            if (_currentErrorIdx < _diskErrors.Count) ShowDiskError(_diskErrors[_currentErrorIdx]);
            else DiskErrorBanner.Visibility = Visibility.Collapsed;
        }

        // ═══ CONTEXT MENU ════════════════════════════════════════════════════════

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            _contextPhoto = _playlist.Current;
            if (_contextPhoto == null) return;
            TbContextFileName.Text = Path.GetFileName(_contextPhoto.FilePath);
            ContextPopup.IsOpen    = true;
        }

        private async void OnContextNext(object s, RoutedEventArgs e)
        { ContextPopup.IsOpen=false; await AdvanceAsync(); }
        private async void OnContextPrev(object s, RoutedEventArgs e)
        { ContextPopup.IsOpen=false; await GoBackAsync(); }
        private void OnContextShowExplorer(object s, RoutedEventArgs e)
        { ContextPopup.IsOpen=false; if (_contextPhoto!=null) RecycleBinHelper.ShowInExplorer(_contextPhoto.FilePath); }
        private async void OnContextDelete(object s, RoutedEventArgs e)
        {
            ContextPopup.IsOpen=false;
            if (_contextPhoto==null) return;
            string name = Path.GetFileName(_contextPhoto.FilePath);
            if (MessageBox.Show($"Переместить в Корзину?\n\n{name}",
                "Удалить фото", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;
            if (!RecycleBinHelper.SendToRecycleBin(_contextPhoto.FilePath))
            { MessageBox.Show($"Не удалось: {name}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            await ReloadPhotosAsync();
        }

        // ═══ TRAY ════════════════════════════════════════════════════════════════

        private void BuildTray()
        {
            try
            {
                _tray = new System.Windows.Forms.NotifyIcon { Text="PhotoFrame", Visible=true };
                try
                {
                    var st = Application.GetResourceStream(
                        new Uri("pack://application:,,,/Resources/Icons/AppIcon.ico"))?.Stream;
                    _tray.Icon = st!=null
                        ? new System.Drawing.Icon(st)
                        : System.Drawing.SystemIcons.Application;
                }
                catch { _tray.Icon = System.Drawing.SystemIcons.Application; }

                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Показать",    null, (_,__) => ShowFromTray());
                menu.Items.Add("Следующее",   null, (_,__) => _ = AdvanceAsync());
                menu.Items.Add("Пауза/Пуск",  null, (_,__) => { if(_playing) StopSlide(); else StartSlide(); });
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Настройки",   null, (_,__) => OpenSettings());
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Выход",       null, (_,__) => Close());
                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick     += (_,__) => ShowFromTray();
            }
            catch { }
        }

        private void ShowFromTray() { Show(); WindowState=WindowState.Normal; Activate(); }

        // ═══ АВТОПРОВЕРКА ОБНОВЛЕНИЙ (build 52) ═══════════════════════════════════
        // Служба обновлений проверяет наличие новой версии НЕ чаще, чем раз в
        // AppSettings.UpdateCheckPeriodDays — вместо проверки при каждом
        // запуске (что при частых перезапусках приложения без надобности
        // дёргает GitHub API/зеркало). Время последней проверки хранится в
        // LastUpdateCheckUtc; таймстемп обновляется только при УСПЕШНОМ
        // обращении к серверу — если сеть недоступна, следующая попытка
        // будет предпринята при следующем запуске, а не только через N дней.
        private async Task CheckForUpdatesIfDueAsync()
        {
            try
            {
                if (!_cfg.AutoCheckUpdatesEnabled) return;

                DateTime? last = DateTime.TryParse(_cfg.LastUpdateCheckUtc,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

                int periodDays = Math.Max(1, _cfg.UpdateCheckPeriodDays);
                if (last.HasValue && (DateTime.UtcNow - last.Value).TotalDays < periodDays) return;

                var cur = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
                var (tag, isNewer) = await SystemIntegration.CheckUpdateAsync(cur, _cfg.UpdateMirrorUrl);
                if (tag == null) return; // сервер недоступен — таймстемп не трогаем, повторим при следующем запуске

                _cfg.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
                SettingsService.Save(_cfg);

                if (isNewer && _tray != null)
                {
                    _tray.BalloonTipTitle = "Доступно обновление PhotoFrame";
                    _tray.BalloonTipText  = $"Новая версия: {tag}. Нажмите, чтобы открыть страницу релизов.";
                    _tray.BalloonTipIcon  = System.Windows.Forms.ToolTipIcon.Info;
                    void OnClicked(object? s2, EventArgs e2)
                    {
                        SystemIntegration.OpenGitHub();
                        if (_tray != null) _tray.BalloonTipClicked -= OnClicked;
                    }
                    _tray.BalloonTipClicked += OnClicked;
                    _tray.ShowBalloonTip(8000);
                }
            }
            catch { /* автопроверка не должна ронять приложение */ }
        }

        // ═══ REMOVABLE MEDIA ═════════════════════════════════════════════════════

        private void StartRemovableWatcher()
        {
            if (!_cfg.WatchRemovableMedia) return;
            try
            {
                _driveWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2"));
                _driveWatcher.EventArrived += OnDriveInserted;
                _driveWatcher.Start();
            }
            catch { }
        }

        private async void OnDriveInserted(object sender, EventArrivedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var removable = await FileScanner.GetRemovableWithPhotosAsync();
                    if (removable.Count == 0) return;
                    string drives = string.Join(", ", removable.ConvertAll(d => d.Name));
                    if (_cfg.SuggestRemovableMedia)
                    {
                        if (MessageBox.Show($"Фото на носителе: {drives}\nДобавить?",
                            "PhotoFrame", MessageBoxButton.YesNo, MessageBoxImage.Question)
                            == MessageBoxResult.Yes)
                        {
                            foreach (var d in removable)
                                if (!_cfg.SelectedPaths.Contains(d.RootDirectory.FullName))
                                    _cfg.SelectedPaths.Add(d.RootDirectory.FullName);
                            SettingsService.Save(_cfg);
                            await ReloadPhotosAsync();
                        }
                    }
                }
                catch { }
            });
        }

        // ═══ SETTINGS ════════════════════════════════════════════════════════════

        private void OpenSettings()
        {
            try
            {
                bool was = _playing;
                StopSlide(); ContextPopup.IsOpen = false;
                var dlg = new SettingsWindow(_cfg) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    _cfg = dlg.Result;
                    SettingsService.Save(_cfg);
                    App.ChangeTheme(_cfg.Theme);
                    App.ApplyUiMode(_cfg.UiMode);
                    ApplyUiModeStyles();
                    ApplyBackdrop();
                    SystemIntegration.PreventSleep(_cfg.PreventSleep);
                    _autoOff.Settings = _cfg; // подхватить новое расписание/режим
                    _slideTimer.Interval =
                        TimeSpan.FromSeconds(Math.Max(1, _cfg.SlideshowIntervalSeconds));
                    SyncIntervalLabel(); SyncPlayModeIcon();
                    ApplyToolbarIcons();
                    if (!_cfg.LiveTilesEnabled) LiveTileService.ClearTile();
                    _driveWatcher?.Stop(); _driveWatcher?.Dispose(); _driveWatcher=null;
                    StartRemovableWatcher();
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

        // ═══ BUTTON HANDLERS (work for both mouse and touch via Click) ════════════

        private async void OnBtnPrev(object s, RoutedEventArgs e)      => await GoBackAsync();
        private async void OnBtnNext(object s, RoutedEventArgs e)      => await AdvanceAsync();
        private void       OnBtnPlayPause(object s, RoutedEventArgs e)
            { if (_playing) StopSlide(); else StartSlide(); }
        private void       OnBtnSettings(object s, RoutedEventArgs e)
            { ContextPopup.IsOpen=false; OpenSettings(); }
        private void       OnBtnFullscreen(object s, RoutedEventArgs e)
            { if (_fullscreen) ExitFullscreen(); else EnterFullscreen(); }
        private void       OnBtnTheme(object s, RoutedEventArgs e)
        {
            _cfg.Theme = App.CurrentTheme==AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            SettingsService.Save(_cfg); App.ChangeTheme(_cfg.Theme);
        }
        private void OnBtnPlayMode(object s, RoutedEventArgs e)
        {
            _cfg.PlayMode = _cfg.PlayMode==PlayMode.Sequential ? PlayMode.Shuffle : PlayMode.Sequential;
            SettingsService.Save(_cfg); _playlist.Rebuild(_cfg.PlayMode); SyncPlayModeIcon();
        }
        private void OnIntervalDown(object s, RoutedEventArgs e)
        {
            int v = _cfg.SlideshowIntervalSeconds;
            v = v<=3?1:v<=10?v-1:v<=60?v-5:v-30;
            _cfg.SlideshowIntervalSeconds = Math.Max(1,v);
            SettingsService.Save(_cfg);
            if (_playing) _slideTimer.Interval=TimeSpan.FromSeconds(_cfg.SlideshowIntervalSeconds);
            SyncIntervalLabel();
        }
        private void OnIntervalUp(object s, RoutedEventArgs e)
        {
            int v = _cfg.SlideshowIntervalSeconds;
            v = v<3?3:v<10?v+1:v<60?v+5:v+30;
            _cfg.SlideshowIntervalSeconds = Math.Min(3600,v);
            SettingsService.Save(_cfg);
            if (_playing) _slideTimer.Interval=TimeSpan.FromSeconds(_cfg.SlideshowIntervalSeconds);
            SyncIntervalLabel();
        }

        // ═══ WIN7/8: SEGOE MDL2 FONT FALLBACK ════════════════════════════════════════
        private async Task OfferMdl2FontInstallAsync()
        {
            var res = MessageBox.Show(
                "На вашей Windows отсутствует шрифт значков Segoe MDL2 Assets (появился в Win10).\n" +
                "Без него значки отображаются как квадраты.\n\n" +
                "Да — скачать и установить (потребуется UAC)\n" +
                "Нет — спросить в следующий раз\n" +
                "Отмена — не предлагать больше никогда",
                "PhotoFrame — отсутствует шрифт значков",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
            if (res == MessageBoxResult.Cancel)
            { SystemIntegration.SetMdl2FontDeclinedForever(); return; }
            if (res == MessageBoxResult.No) return;
            bool ok = await SystemIntegration.DownloadAndInstallMdl2FontAsync();
            MessageBox.Show(ok
                ? "Шрифт установлен. Перезапустите PhotoFrame."
                : "Не удалось. Скачайте вручную: https://archive.org/download/segmdl2/segmdl2.ttf",
                "PhotoFrame", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        // ═══ IDLE-AWARE SCREENSAVER (когда свёрнуто в трей) ════════════════════════
        private void OnIdleCheckTick(object? sender, EventArgs e)
        {
            if (Visibility != Visibility.Hidden) return;
            if (_cfg.ScreensaverDelayMinutes <= 0) return;
            if (!SystemIntegration.IsSystemIdle(
                    TimeSpan.FromMinutes(_cfg.ScreensaverDelayMinutes))) return;

            Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                EnterFullscreen();
                if (!_playing && _playlist.Count > 0) StartSlide();
            });
        }

        // ═══ АВТООТКЛЮЧЕНИЕ РАМКИ ПО РАСПИСАНИЮ ════════════════════════════════════
        private void OnAutoOffShouldBeActiveChanged(bool shouldBeActive)
        {
            Dispatcher.Invoke(() =>
            {
                if (!shouldBeActive && _cfg.AutoOffMode != AutoOffMode.Disabled)
                {
                    if (IsVisible && Visibility != Visibility.Hidden)
                    {
                        _autoOffHidden = true;
                        Hide();
                        if (_playing) StopSlide();
                    }
                }
                else if (shouldBeActive && _autoOffHidden)
                {
                    _autoOffHidden = false;
                    Show();
                    WindowState = WindowState.Normal;
                    if (_cfg.AutoStart && !_playing && _playlist.Count > 0) StartSlide();
                }
            });
        }

        // ═══ TITLE BAR (custom — WindowStyle=None) ═══════════════════════════════

        private void OnTitleBarDrag(object s, MouseButtonEventArgs e)
        {
            if (e.ClickCount==2) { if(_fullscreen) ExitFullscreen(); else EnterFullscreen(); return; }
            if (e.LeftButton==MouseButtonState.Pressed)
                try { DragMove(); } catch { }
        }

        private void OnWinMinimize(object s, RoutedEventArgs e)
        { if (_cfg.MinimizeToTray) Hide(); else WindowState=WindowState.Minimized; }

        private void OnWinMaximize(object s, RoutedEventArgs e)
        {
            if (WindowState==WindowState.Maximized)
            { WindowState=WindowState.Normal;    TbWinMax.Text="\uE922"; }
            else
            { WindowState=WindowState.Maximized; TbWinMax.Text="\uE923"; }
        }

        private void OnWinClose(object s, RoutedEventArgs e) => Close();

        // ═══ KEYBOARD ════════════════════════════════════════════════════════════

        private void OnKeyDown(object s, KeyEventArgs e)
        {
            ShowToolbarNow();
            if (App.StartMode==AppStartMode.Screensaver) { Close(); return; }
            switch (e.Key)
            {
                case Key.Right: case Key.Down:  case Key.PageDown: _ = AdvanceAsync(); break;
                case Key.Left:  case Key.Up:    case Key.PageUp:   _ = GoBackAsync();  break;
                case Key.Space: if (_playing) StopSlide(); else StartSlide();           break;
                case Key.F:     case Key.F11:
                    if (_fullscreen) ExitFullscreen(); else EnterFullscreen(); break;
                case Key.Escape:
                    if (ContextPopup.IsOpen) { ContextPopup.IsOpen=false; break; }
                    if (_fullscreen) ExitFullscreen(); else Close(); break;
                case Key.OemComma when e.KeyboardDevice.Modifiers==ModifierKeys.Control:
                    OpenSettings(); break;
            }
        }

        // ═══ MOUSE ═══════════════════════════════════════════════════════════════

        private void OnMouseMove(object s, MouseEventArgs e) => ShowToolbarNow();
        private void OnMouseLeave(object s, MouseEventArgs e)
            { if (_fullscreen) _hideTimer.Start(); }

        // Mouse clicks on photo area (SwipeZone)
        private void OnPhotoAreaMouseDown(object s, MouseButtonEventArgs e)
        {
            if (App.StartMode==AppStartMode.Screensaver) { Close(); return; }
            if (e.ChangedButton==MouseButton.Right) return;
            if (ContextPopup.IsOpen) { ContextPopup.IsOpen=false; return; }

            ShowToolbarNow();

            if (e.ClickCount==2 && e.ChangedButton==MouseButton.Left)
            { if (_fullscreen) ExitFullscreen(); else EnterFullscreen(); return; }

            if (e.ChangedButton==MouseButton.Left)
            {
                double x = e.GetPosition(RootGrid).X;
                if (x < RootGrid.ActualWidth/2) _ = GoBackAsync();
                else _ = AdvanceAsync();
            }
        }

        // ═══ TOUCH — SwipeZone ManipulationStarting / ManipulationDelta ══════════
        // Кнопки тулбара получают обычный Click от touch — не перехватываем.
        // SwipeZone лежит ПОД тулбаром (Z-order) и ловит только свайпы по фото.

        private void OnManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = this;
            // ManipulationModes: WPF does NOT have TranslateInertia here.
            // Inertia is enabled via ManipulationInertiaStarting event.
            e.Mode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
            e.Handled = true;
        }

        private void OnManipulationInertiaStarting(object sender,
            ManipulationInertiaStartingEventArgs e)
        {
            // Enable deceleration so swipe coasts naturally
            e.TranslationBehavior = new InertiaTranslationBehavior
            {
                DesiredDeceleration = 10.0 * 96.0 / (1000.0 * 1000.0)
            };
            e.Handled = true;
        }

        private void OnManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            ShowToolbarNow();

            if (!e.IsInertial) { e.Handled = true; return; }

            double vx = e.Velocities.LinearVelocity.X;
            double vy = e.Velocities.LinearVelocity.Y;

            if (Math.Abs(vx) > Math.Abs(vy) * 1.5)
            {
                if (vx < -80) { e.Complete(); _ = AdvanceAsync(); return; }
                if (vx >  80) { e.Complete(); _ = GoBackAsync();  return; }
            }
            else if (vy < -150 && Math.Abs(vy) > Math.Abs(vx)*1.5)
            { e.Complete(); OpenSettings(); return; }

            e.Handled = true;
        }

        // ═══ WINDOW STATE ════════════════════════════════════════════════════════

        private void OnStateChanged(object s, EventArgs e)
        {
            if (!_fullscreen && TbWinMax != null)
                TbWinMax.Text = WindowState==WindowState.Maximized ? "\uE923" : "\uE922";
        }

        // ═══ CLOSING ═════════════════════════════════════════════════════════════

        private void OnClosing(object s, System.ComponentModel.CancelEventArgs e)
        {
            App.ThemeChanged -= OnThemeChanged;
            _slideTimer.Stop(); _hideTimer.Stop(); _counterTimer.Stop();
            _idleCheckTimer.Stop();
            _autoOff.ShouldBeActiveChanged -= OnAutoOffShouldBeActiveChanged;
            _autoOff.Stop();
            _driveWatcher?.Stop(); _driveWatcher?.Dispose();
            SystemIntegration.PreventSleep(false);
            if (!_cfg.LiveTilesEnabled) LiveTileService.ClearTile();
            SettingsService.Save(_cfg);
            try { _tray?.Dispose(); } catch { }
        }
    }
}
