// MainWindow.xaml.cs — v4.0 (build 43)
//
// UI MODE SWITCHING (ghost-activation):
//   Modern  → ToolbarButton style + Segoe MDL2 Assets glyphs.
//   Aero7   → Aero7ToolbarButton style + PNG icons from Resources/Icons/
//             + Aero7CaptionButton on title bar Min/Max/Close.
//   Switch is instant, no restart required. Triggered from Внешний вид → UiMode combobox.
//
// ADAPTIVE DECODE WIDTH:
//   x64: screen width (no cap).  x86/≤2GB: 1920px cap.
//
// TRANSPARENCY:
//   AllowsTransparency=True + WindowStyle=None + Background=Transparent.
//   DWM Mica (Win11) / Acrylic (Win10) via WindowHelper.
//
// SCREENSAVER: auto-starts slideshow in /S mode regardless of AutoStart setting.
// AUTOSTART: reconciles HKCU\Run vs saved setting on each launch.
// SCAN: CancellationToken cancels stale scan before reload.

using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        private bool              _pendingScreensaverAutoStart = false;

        private CancellationTokenSource _scanCts = new();

        private readonly DispatcherTimer _slideTimer   = new();
        private readonly DispatcherTimer _hideTimer    = new()
            { Interval = TimeSpan.FromSeconds(3) };
        private readonly DispatcherTimer _counterTimer = new()
            { Interval = TimeSpan.FromSeconds(4) };
        private readonly DispatcherTimer _tileTimer    = new();
        private bool _toolbarVisible = true;

        private System.Windows.Forms.NotifyIcon? _tray;
        private ManagementEventWatcher?           _driveWatcher;
        private PhotoInfo? _contextPhoto;
        private List<DiskError> _diskErrors      = new();
        private int             _currentErrorIdx = 0;

        // Adaptive decode width: x64 = screen width, x86 = capped
        private int _decodeWidth = ComputeDecodeWidth();

        private static int ComputeDecodeWidth()
        {
            if (IntPtr.Size == 8)
            {
                try { return Math.Max(1920, (int)SystemParameters.PrimaryScreenWidth); }
                catch { return 3840; }
            }
            try
            {
                long mb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
                return mb <= 2048 ? 1920 : 2560;
            }
            catch { return 1920; }
        }

        public MainWindow()
        {
            InitializeComponent();
            _slideTimer.Tick   += async (_, __) => await AdvanceAsync();
            _hideTimer.Tick    += (_, __) => TryHideToolbar();
            _counterTimer.Tick += (_, __) => HideCounter();
            _tileTimer.Tick    += (_, __) => OnTileCycle();
        }

        // ═══ LOADED ══════════════════════════════════════════════════════════════

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LiveTileService.SetAppUserModelId();
                _engine = new TransitionEngine(ImgA, ImgB, RootGrid);
                _cfg    = SettingsService.Load();

                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                TbTitleVersion.Text = ver != null
                    ? $"PhotoFrame  v{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}"
                    : "PhotoFrame";
                Title = TbTitleVersion.Text;

                RefreshDwmTheme();
                ApplyBackdrop();
                App.ThemeChanged  += OnThemeChanged;
                App.UiModeChanged += OnUiModeChanged;

                SystemIntegration.PreventSleep(_cfg.PreventSleep);

                // Reconcile autostart
                bool actualAutostart = SystemIntegration.IsAutostartEnabled();
                if (_cfg.AutostartEnabled && !actualAutostart)
                    SystemIntegration.SetAutostart(true);
                else if (!_cfg.AutostartEnabled && actualAutostart)
                    _cfg.AutostartEnabled = false;

                BuildTray();
                StartRemovableWatcher();

                if (App.StartMode == AppStartMode.Screensaver)
                {
                    EnterFullscreen();
                    _hideTimer.Start();
                    if (_cfg.SelectedPaths.Count > 0)
                        _pendingScreensaverAutoStart = true;
                }

                // Apply UI mode (toolbar icon style + caption buttons)
                ApplyUiModeStyles(_cfg.UiMode);
                SyncPlayIcon();
                SyncThemeIcon();
                SyncPlayModeIcon();
                SyncIntervalLabel();
                RestartTileTimer();

                if (_cfg.SelectedPaths.Count > 0)
                    await ReloadPhotosAsync();
                else
                    ShowEmpty();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═══ UI MODE — STYLE SWITCHING ════════════════════════════════════════
        //
        // ApplyUiModeStyles() swaps:
        //   1. Style on every toolbar Button (ToolbarButton ↔ Aero7ToolbarButton)
        //   2. Icon content: Segoe MDL2 glyph ↔ PNG Image via IconHelper
        //   3. Caption buttons style (transparent ↔ Aero7CaptionButton)
        //   4. Close button special style (Aero7CloseButton)

        private void ApplyUiModeStyles(UiMode mode)
        {
            bool aero = mode == UiMode.Aero7;

            Style toolbarBtnStyle = (TryFindResource(aero
                ? "Aero7ToolbarButton"
                : "ToolbarButton") as Style)!;

            // Toolbar buttons
            Button[] toolbarBtns = {
                BtnPlayMode, BtnIntervalDown, BtnIntervalUp,
                BtnPrev, BtnPlayPause, BtnNext,
                BtnTheme, BtnSettings, BtnFullscreen
            };
            foreach (var btn in toolbarBtns)
            {
                if (btn == null) continue;
                btn.Style = toolbarBtnStyle;
            }

            // Icons inside toolbar buttons
            if (aero)
            {
                IconHelper.SwapIcon(BtnPrev,      IconRole.Start,   20);
                IconHelper.SwapIcon(BtnNext,      IconRole.End,     20);
                IconHelper.SwapIcon(BtnSettings,  IconRole.Settings, 20);
                // Play/Pause synced separately
                SyncPlayIconAero();
            }
            else
            {
                IconHelper.RestoreMdl2(BtnPrev,     "\uE892", 20);
                IconHelper.RestoreMdl2(BtnNext,     "\uE893", 20);
                IconHelper.RestoreMdl2(BtnSettings, "\uE713", 20);
                // Restore glyph for play
                if (TbPlayIcon != null) TbPlayIcon.Visibility = Visibility.Visible;
            }

            // Caption buttons
            if (BtnWinMin != null && BtnWinMax != null && BtnWinClose != null)
            {
                if (aero)
                {
                    var capStyle   = TryFindResource("Aero7CaptionButton")  as Style;
                    var closeStyle = TryFindResource("Aero7CloseButton")    as Style;
                    BtnWinMin.Style   = capStyle;
                    BtnWinMax.Style   = capStyle;
                    BtnWinClose.Style = closeStyle ?? capStyle;
                }
                else
                {
                    // Restore default transparent caption style
                    BtnWinMin.ClearValue(StyleProperty);
                    BtnWinMax.ClearValue(StyleProperty);
                    BtnWinClose.ClearValue(StyleProperty);
                }
            }

            // Toolbar background: Aero uses AeroTheme's ToolbarBgBrush override
            // (already merged in App.xaml.cs — DynamicResource picks it up automatically)
        }

        private void OnUiModeChanged(UiMode mode)
        {
            _cfg.UiMode = mode;
            ApplyUiModeStyles(mode);
            SyncPlayIcon();
        }

        // ═══ BACKDROP ════════════════════════════════════════════════════════════

        private void ApplyBackdrop()
        {
            if (_cfg.EnableMicaEffect)
            {
                WindowHelper.TryApplyBackdrop(this, App.CurrentTheme == AppTheme.Dark);
            }
            else
            {
                WindowHelper.RemoveBackdrop(this);
                bool dark = App.CurrentTheme == AppTheme.Dark;
                Background = new SolidColorBrush(dark
                    ? Color.FromArgb(0xCC, 0x11, 0x11, 0x11)
                    : Color.FromArgb(0xCC, 0xF0, 0xF0, 0xF0));
            }
        }

        // ═══ TILE TIMER ══════════════════════════════════════════════════════════

        private void RestartTileTimer()
        {
            _tileTimer.Stop();
            if (!_cfg.LiveTilesEnabled) return;
            int sec = _cfg.LiveTileCycleIntervalSeconds > 0
                ? _cfg.LiveTileCycleIntervalSeconds
                : _cfg.SlideshowIntervalSeconds;
            _tileTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, sec));
            _tileTimer.Start();
        }

        private void OnTileCycle()
        {
            if (_cfg.LiveTilesEnabled)
                LiveTileService.CycleTile(_cfg.LiveTilesLargeEnabled);
        }

        // ═══ PHOTOS ══════════════════════════════════════════════════════════════

        private async Task ReloadPhotosAsync()
        {
            await _scanCts.CancelAsync();
            _scanCts.Dispose();
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;

            ScanPanel.Visibility       = Visibility.Visible;
            EmptyPanel.Visibility      = Visibility.Collapsed;
            DiskErrorBanner.Visibility = Visibility.Collapsed;
            ContextPopup.IsOpen        = false;

            try
            {
                _lastScan = await FileScanner.ScanAsync(
                    _cfg.SelectedPaths, _cfg.IncludeSubdirectories,
                    p => Dispatcher.InvokeAsync(() => TbScanPath.Text = p),
                    ct);
            }
            catch (OperationCanceledException) { return; }

            ScanPanel.Visibility = Visibility.Collapsed;

            if (_lastScan.DiskErrors.Count > 0)
            { _diskErrors = _lastScan.DiskErrors; _currentErrorIdx = 0; ShowDiskError(_diskErrors[0]); }

            if (_lastScan.Photos.Count == 0) { ShowEmpty(); return; }

            _playlist.SetPhotos(_lastScan.Photos, _cfg.PlayMode);
            await ShowCurrentAsync(animate: false);
            if (_cfg.AutoStart || _pendingScreensaverAutoStart)
            { _pendingScreensaverAutoStart = false; StartSlide(); }
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

            if (_cfg.LiveTilesEnabled)
            {
                LiveTileService.EnqueuePhoto(photo.FilePath);
                if (!_tileTimer.IsEnabled)
                    LiveTileService.UpdateTile(photo.FilePath, _cfg.LiveTilesLargeEnabled);
            }

            SyncCounter();
            ContextPopup.IsOpen = false;
        }

        private BitmapImage? LoadBitmapSafe(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource        = new Uri(path, UriKind.Absolute);
                b.CacheOption      = BitmapCacheOption.OnLoad;
                b.CreateOptions    = BitmapCreateOptions.IgnoreColorProfile;
                b.DecodePixelWidth = _decodeWidth;
                b.EndInit(); b.Freeze();
                return b;
            }
            catch (OutOfMemoryException)
            {
                try
                {
                    var b2 = new BitmapImage();
                    b2.BeginInit();
                    b2.UriSource        = new Uri(path, UriKind.Absolute);
                    b2.CacheOption      = BitmapCacheOption.OnLoad;
                    b2.CreateOptions    = BitmapCreateOptions.IgnoreColorProfile;
                    b2.DecodePixelWidth = Math.Max(480, _decodeWidth / 2);
                    b2.EndInit(); b2.Freeze();
                    return b2;
                }
                catch { return null; }
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
                && (_lastScan?.TotalFilesScanned ?? 0) > photos
                ? $"{_playlist.CurrentIndex+1} / {photos}  [{_lastScan!.TotalFilesScanned} файлов]"
                : $"{_playlist.CurrentIndex+1} / {photos}";
            CounterBadge.Opacity = 1;
            _counterTimer.Stop(); _counterTimer.Start();
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
        }

        private async Task GoBackAsync()
        {
            if (_engine?.IsTransitioning == true) return;
            _playlist.Prev(_cfg.LoopSlideshow);
            await ShowCurrentAsync(animate: true);
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

        // ═══ FULLSCREEN ══════════════════════════════════════════════════════════

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
            RefreshDwmTheme(); SyncThemeIcon(); ApplyBackdrop();
        }

        private void RefreshDwmTheme()
            => WindowHelper.SetTitleBarDarkMode(this, App.CurrentTheme == AppTheme.Dark);

        // ═══ ICON SYNC ═══════════════════════════════════════════════════════════

        private void SyncPlayIcon()
        {
            if (_cfg.UiMode == UiMode.Aero7) { SyncPlayIconAero(); return; }
            if (TbPlayIcon  != null) { TbPlayIcon.Text  = _playing ? "\uE769" : "\uE768"; TbPlayIcon.Visibility = Visibility.Visible; }
            if (TbPlayLabel != null)   TbPlayLabel.Text = _playing ? "Пауза"  : "Пуск";
        }

        private void SyncPlayIconAero()
        {
            IconHelper.SwapIcon(BtnPlayPause,
                _playing ? IconRole.Pause : IconRole.Play, 22);
            if (TbPlayLabel != null) TbPlayLabel.Text = _playing ? "Пауза" : "Пуск";
            // Hide underlying TextBlock (replaced by Image)
            if (TbPlayIcon != null) TbPlayIcon.Visibility = Visibility.Collapsed;
        }

        private void SyncThemeIcon()
        {
            bool dark = App.CurrentTheme == AppTheme.Dark;
            if (TbThemeIcon != null) TbThemeIcon.Text = dark ? "\uE708" : "\uE706";
        }

        private void SyncPlayModeIcon()
        {
            bool shuffle = _cfg.PlayMode != PlayMode.Sequential;
            if (TbPlayModeIcon  != null) TbPlayModeIcon.Text  = shuffle ? "\uE8B1" : "\uE8AC";
            if (TbPlayModeLabel != null) TbPlayModeLabel.Text = shuffle ? "Случайно" : "По порядку";
        }

        private void SyncIntervalLabel()
        {
            int s = _cfg.SlideshowIntervalSeconds;
            if (TbInterval != null) TbInterval.Text = s >= 60 ? $"{s/60}м{s%60:D2}с" : $"{s}с";
        }

        // ═══ OVERLAYS ════════════════════════════════════════════════════════════

        private void UpdateOverlays(PhotoInfo photo)
        {
            SetOverlay(OverlayDir,  TbDir,  _cfg.ShowDirectoryOverlay, photo.Directory,      _cfg.OverlayFontSize);
            SetOverlay(OverlayLoc,  TbLoc,  _cfg.ShowLocationOverlay,  photo.LocationString, _cfg.OverlayFontSize);
            SetOverlay(OverlayDate, TbDate, _cfg.ShowDateOverlay,       photo.DateString,     _cfg.OverlayFontSize);
        }

        private static void SetOverlay(Border b, TextBlock tb, bool show, string? text, double fs)
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
                (err.Kind == ErrorKind.IoError ? "\nДиск повреждён." : "");
            BtnChkdsk.Visibility = err.Kind == ErrorKind.IoError && err.Volume != null
                ? Visibility.Visible : Visibility.Collapsed;
            BtnChkdsk.Tag              = err.Volume;
            DiskErrorBanner.Visibility = Visibility.Visible;
        }

        private void OnChkdsk(object s, RoutedEventArgs e)
        {
            string? vol = (BtnChkdsk.Tag as string)?.TrimEnd('\\');
            if (string.IsNullOrEmpty(vol)) return;
            if (MessageBox.Show($"Запустить chkdsk {vol} /r?\n\n⚠ Потребуется перезагрузка.",
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
            _diskErrors.RemoveAt(_currentErrorIdx);
            if (_currentErrorIdx >= _diskErrors.Count) _currentErrorIdx = 0;
            if (_diskErrors.Count > 0) ShowDiskError(_diskErrors[_currentErrorIdx]);
            else DiskErrorBanner.Visibility = Visibility.Collapsed;
        }

        private void OnCloseDiskError(object s, RoutedEventArgs e)
        {
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
                    if (_cfg.SuggestRemovableMedia &&
                        MessageBox.Show($"Фото на носителе: {drives}\nДобавить?",
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
                    App.ChangeUiMode(_cfg.UiMode);   // triggers OnUiModeChanged → ApplyUiModeStyles
                    ApplyBackdrop();
                    SystemIntegration.PreventSleep(_cfg.PreventSleep);
                    _slideTimer.Interval =
                        TimeSpan.FromSeconds(Math.Max(1, _cfg.SlideshowIntervalSeconds));
                    SyncIntervalLabel(); SyncPlayModeIcon();
                    if (!_cfg.LiveTilesEnabled) LiveTileService.ClearTile();
                    RestartTileTimer();
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

        // ═══ BUTTON HANDLERS ═════════════════════════════════════════════════════

        private async void OnBtnPrev(object s, RoutedEventArgs e)     => await GoBackAsync();
        private async void OnBtnNext(object s, RoutedEventArgs e)     => await AdvanceAsync();
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

        // ═══ CAPTION BUTTONS ═════════════════════════════════════════════════════

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
                case Key.F: case Key.F11:
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

        // ═══ TOUCH — SwipeZone ═══════════════════════════════════════════════════

        private void OnManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = this;
            e.Mode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
            e.Handled = true;
        }

        private void OnManipulationInertiaStarting(object sender,
            ManipulationInertiaStartingEventArgs e)
        {
            e.TranslationBehavior = new InertiaTranslationBehavior
            { DesiredDeceleration = 10.0 * 96.0 / (1000.0 * 1000.0) };
            e.Handled = true;
        }

        private void OnManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            ShowToolbarNow();
            if (!e.IsInertial) { e.Handled = true; return; }
            double vx = e.Velocities.LinearVelocity.X;
            double vy = e.Velocities.LinearVelocity.Y;
            if (Math.Abs(vx) > Math.Abs(vy)*1.5)
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
            App.ThemeChanged  -= OnThemeChanged;
            App.UiModeChanged -= OnUiModeChanged;
            _slideTimer.Stop(); _hideTimer.Stop(); _counterTimer.Stop(); _tileTimer.Stop();
            _scanCts.Cancel(); _scanCts.Dispose();
            _driveWatcher?.Stop(); _driveWatcher?.Dispose();
            SystemIntegration.PreventSleep(false);
            if (!_cfg.LiveTilesEnabled) LiveTileService.ClearTile();
            SettingsService.Save(_cfg);
            try { _tray?.Dispose(); } catch { }
        }
    }
}
