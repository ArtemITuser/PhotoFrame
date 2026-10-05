// MainPage.xaml.cs — UWP-порт MainWindow.xaml.cs (PhotoFrame v1.2.2.0).
// Логика слайдшоу перенесена 1:1 (методы LoadPhotos/ShowCurrentPhoto/Next/Prev/TogglePlay/
// UpdateOverlays/UpdateStatus/OnTimerTick/ApplyTheme), без смешивания с кодом настроек:
// настройки живут в Views/SettingsView (отдельное подокно).
//
// Адаптации WPF → UWP:
//   * DispatcherTimer            → Windows.UI.Xaml.DispatcherTimer (есть);
//   * Window.Title               → TxtStatus.Text;
//   * BitmapImage(UriSource)     → StorageFile.OpenStreamAsync + SetSourceAsync (sandbox);
//   * FolderBrowserDialog(WinForms) → FolderPicker;
//   * Mouse.Enter/Leave          → PointerEntered/PointerExited (Continuum-мышь поддерживается);
//   * KeyDown (пробел/стрелки/Esc/F11) → CoreWindow.InputReader? Нет — UseLayoutRounding:
//     подписка на Window.Current.CoreWindow.KeyDown (работает и на Continuum-клавиатуре);
//   * MinimizeToTray / NotifyIcon → нет в sandbox: свернуть нельзя, «Выход» закрывает view;
//   * Fullscreen WindowState      → ApplicationView.TryEnterFullScreenMode (Mobile) /
//                                   TryUninstallForAllUsers? Нет — ExitFullScreenMode.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using PhotoFrameUwp.Helpers;
using PhotoFrameUwp.Models;
using PhotoFrameUwp.Services;
using PhotoFrameUwp.Views;

namespace PhotoFrameUwp
{
    public sealed partial class MainPage : Page
    {
        // ─── Поля (идентичны MainWindow.xaml.cs) ──────────────────────────────────
        private readonly PlaylistManager _playlist = new PlaylistManager();
        private TransitionEngine _engine;
        private DispatcherTimer  _timer;

        private AppSettings _settings = new AppSettings();
        private bool _isPlaying       = false;
        private bool _isFullscreen    = false;
        private int  _currentInterval = 5;

        // Кэш источников по пути (аналог _bitmapCache)
        private readonly Dictionary<string, BitmapImage> _cache =
            new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);

        public MainPage()
        {
            this.InitializeComponent();

            _settings = App.Settings ?? new AppSettings();
            _engine   = new TransitionEngine(ImgA, ImgB, PhotoContainer);

            Loaded += OnLoaded;
        }

        // ─── Загрузка страницы (аналог конструктора MainWindow после InitializeComponent) ─

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                IconHelper.ApplyTo(BtnPlayPause, IconRole.Pause);
                IconHelper.ApplyTo(BtnPrev,      IconRole.Previous);
                IconHelper.ApplyTo(BtnNext,      IconRole.Next);
                IconHelper.ApplyTo(BtnShuffle,   IconRole.Shuffle);
                IconHelper.ApplyTo(BtnAddFolder, IconRole.Folder);

                SldInterval.Value = _settings.SlideshowIntervalSeconds;
                _currentInterval  = _settings.SlideshowIntervalSeconds;
                TxtInterval.Text  = $"{_currentInterval} с";

                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_currentInterval) };
                _timer.Tick += OnTimerTick;

                // Наведение мыши (Continuum/desktop): показ панели управления
                PhotoContainer.PointerEntered += (s, ev) => ControlPanel.Visibility = Visibility.Visible;
                PhotoContainer.PointerExited  += (s, ev) => { if (_isPlaying) ControlPanel.Visibility = Visibility.Collapsed; };

                // Клавиатура (Continuum): пробел/стрелки/Esc/F11 — как в оригинале
                Window.Current.CoreWindow.KeyDown += OnGlobalKeyDown;

                DisplayHelper.AdaptToolbar(ControlPanel, TitleBarArea);

                await LoadPhotosAsync();

                if (_settings.AutoStart && _playlist.Count > 0)
                    StartSlideshow();
                else
                    UpdatePlayButton();

                // /C (Configure) — сразу открыть подокно настроек
                if (_navMode == AppStartMode.Configure)
                    OpenSettingsPopup();
            }
            catch (Exception ex)
            {
                UpdateStatus("Ошибка запуска: " + ex.Message);
            }
        }

        private AppStartMode _navMode = AppStartMode.Normal;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is AppStartMode m) _navMode = m;
            base.OnNavigatedTo(e);
        }

        // ─── Фотографии (логика = LoadPhotos) ─────────────────────────────────────

        private async Task LoadPhotosAsync()
        {
            var scan = await FileScanner.ScanAsync(
                _settings.SelectedPaths, _settings.IncludeSubdirectories);

            _playlist.SetList(scan.Photos);
            _playlist.Loop = _settings.LoopSlideshow;
            ApplyPlayMode();

            EmptyMsg.Visibility = _playlist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtCount.Text = ""; TbCountShadow.Text = "";

            if (_playlist.Count > 0)
                ShowCurrentPhoto(immediate: true);
            UpdateStatus();
        }

        private void ApplyPlayMode()
        {
            switch (_settings.PlayMode)
            {
                case PlayMode.Sequential:     _playlist.Mode = ShuffleMode.None; break;
                case PlayMode.Shuffle:        _playlist.Mode = ShuffleMode.NoRepeat; break;
                case PlayMode.TrueRandom:     _playlist.Mode = ShuffleMode.Random; break;
                case PlayMode.DateAscending:  _playlist.SortByDate(ascending: true);  _playlist.Mode = ShuffleMode.None; break;
                case PlayMode.DateDescending: _playlist.SortByDate(ascending: false); _playlist.Mode = ShuffleMode.None; break;
            }
        }

        // ─── Показ фото (логика = ShowCurrentPhoto) ───────────────────────────────

        private async void ShowCurrentPhoto(bool immediate = false)
        {
            var photo = _playlist.Current;
            if (photo == null) return;

            var img = await LoadBitmapAsync(photo.FilePath);
            if (img == null) { Advance(); return; }

            if (immediate || _settings.TransitionType == TransitionType.None)
                _engine.ShowImmediate(img);
            else
                _engine.Transition(img, _settings.TransitionType,
                    _settings.TransitionDurationSeconds);

            UpdateOverlays(photo);
            UpdateCounter();
        }

        /// <summary>
        /// Загрузка BitmapImage через StorageFile (вместо BitmapImage.UriSource из WPF):
        /// DecodePixelWidth ≈ ширина контейнера × DPI — экономия RAM на ARM32.
        /// </summary>
        private async Task<BitmapImage> LoadBitmapAsync(string path)
        {
            BitmapImage cached;
            if (_cache.TryGetValue(path, out cached)) return cached;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    var bmp = new BitmapImage();
                    double targetW = Math.Max(480, PhotoContainer.ActualWidth *
                        (DisplayHelper.IsMobileFamily ? 1.0 : 0.75));
                    bmp.DecodePixelWidth = (int)Math.Min(targetW, 1920);
                    await bmp.SetSourceAsync(stream);
                    if (_cache.Count > 24) _cache.Clear(); // ограничение кэша для Mobile RAM
                    _cache[path] = bmp;
                    return bmp;
                }
            }
            catch { return null; }
        }

        // ─── Оверлеи (логика = UpdateOverlays) ────────────────────────────────────

        private void UpdateOverlays(PhotoInfo photo)
        {
            DirBadge.Visibility = _settings.ShowDirectoryOverlay
                ? Visibility.Visible : Visibility.Collapsed;
            if (_settings.ShowDirectoryOverlay)
                TxtDir.Text = System.IO.Path.GetFileName(photo.Directory ?? "");

            DateBadge.Visibility = (_settings.ShowDateOverlay && photo.DateTaken.HasValue)
                ? Visibility.Visible : Visibility.Collapsed;
            if (_settings.ShowDateOverlay && photo.DateTaken.HasValue)
                TxtDate.Text = photo.DateTaken.Value.ToString("dd.MM.yyyy HH:mm");

            LocBadge.Visibility = (_settings.ShowLocationOverlay
                                   && photo.Latitude.HasValue && photo.Longitude.HasValue)
                ? Visibility.Visible : Visibility.Collapsed;
            if (_settings.ShowLocationOverlay && photo.Latitude.HasValue)
                TxtLocation.Text =
                    $"{photo.Latitude.Value:F4}, {photo.Longitude.Value:F4}";
        }

        private void UpdateCounter()
        {
            string t = $"{_playlist.Index + 1} / {_playlist.Count}";
            TxtCount.Text = t; TbCountShadow.Text = t;
        }

        private void UpdateStatus()
        {
            TxtStatus.Text = _playlist.Count == 0
                ? "Папки не выбраны"
                : $"{_playlist.Count} фото · " +
                  (_isPlaying ? $"Играет ({_currentInterval} с)" : "Пауза");
        }

        // ─── Управление (логика = OnTogglePlay/OnNext/OnPrevious) ─────────────────

        private void OnTogglePlay(object sender, RoutedEventArgs e)
        {
            if (_playlist.Count == 0) { _ = PickFolderAndReloadAsync(); return; }
            if (_isPlaying) StopSlideshow(); else StartSlideshow();
        }

        private void StartSlideshow()
        {
            _isPlaying = true;
            _timer.Start();
            ControlPanel.Visibility = Visibility.Collapsed;
            SystemIntegration.PreventSleep(_settings.PreventSleep);
            UpdatePlayButton(); UpdateStatus();
        }

        private void StopSlideshow()
        {
            _isPlaying = false;
            _timer.Stop();
            ControlPanel.Visibility = Visibility.Visible;
            SystemIntegration.PreventSleep(false);
            UpdatePlayButton(); UpdateStatus();
        }

        private void UpdatePlayButton()
        {
            IconHelper.ApplyTo(BtnPlayPause, _isPlaying ? IconRole.Play : IconRole.Pause);
            TbPlayGlyph.Text  = _isPlaying ? "\uE768" : "\uE769";
            TbPlayLabel.Text  = _isPlaying ? "  Старт" : "  Пауза";
        }

        private void OnNext(object sender, RoutedEventArgs e)     => Advance();
        private void OnPrevious(object sender, RoutedEventArgs e) => Back();

        private void Advance()
        {
            if (!_playlist.MoveNext()) { if (!_settings.LoopSlideshow) StopSlideshow(); return; }
            ShowCurrentPhoto();
        }

        private void Back()
        {
            _playlist.MovePrevious();
            ShowCurrentPhoto();
        }

        private void OnTimerTick(object sender, object e)
        {
            if (_engine.IsTransitioning) return;
            Advance();
        }

        private void OnIntervalChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            _currentInterval = (int)e.NewValue;
            TxtInterval.Text = $"{_currentInterval} с";
            if (_timer != null) _timer.Interval = TimeSpan.FromSeconds(_currentInterval);
            _settings.SlideshowIntervalSeconds = _currentInterval;
        }

        private void OnToggleShuffle(object sender, RoutedEventArgs e)
        {
            _settings.PlayMode = _settings.PlayMode == PlayMode.Shuffle
                ? PlayMode.Sequential : PlayMode.Shuffle;
            _playlist.ResetVisited();
            ApplyPlayMode();
            UpdateStatus();
        }

        // ─── Выбор папки (FolderPicker вместо FolderBrowserDialog) ────────────────

        private async void OnAddFolder(object sender, RoutedEventArgs e) => await PickFolderAndReloadAsync();

        private async Task PickFolderAndReloadAsync()
        {
            try
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add(".jpg");
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

                IStorageFolder folder;
                if (DisplayHelper.IsMobileFamily)
                {
                    folder = await picker.PickSingleFolderAsync();
                }
                else
                {
                    // Continuum/desktop: WinRT-picker требует window-контекст — тот же вызов
                    folder = await picker.PickSingleFolderAsync();
                }
                if (folder == null) return;

                _settings.SelectedPaths.Add(folder.Path);
                await SettingsService.SaveAsync(_settings);
                HamburgerHost.IsPaneOpen = false;
                await LoadPhotosAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus("Ошибка выбора папки: " + ex.Message);
            }
        }

        // ─── Тема (логика = OnCycleTheme из оригинала) ────────────────────────────

        private void OnCycleTheme(object sender, RoutedEventArgs e)
        {
            // C#7.3 (LangVersion для UWP VS2017) — без switch-expression (net8-only)
            if (_settings.Theme == AppTheme.System)     _settings.Theme = AppTheme.Dark;
            else if (_settings.Theme == AppTheme.Dark)  _settings.Theme = AppTheme.Light;
            else                                        _settings.Theme = AppTheme.System;
            App.ApplyTheme(_settings.Theme);
            HamburgerHost.IsPaneOpen = false;
        }

        // ─── Гамбургер-меню и подокно настроек ────────────────────────────────────

        private void OnToggleMenu(object sender, RoutedEventArgs e)
            => HamburgerHost.IsPaneOpen = !HamburgerHost.IsPaneOpen;

        private void OnOpenSettings(object sender, RoutedEventArgs e)
        {
            HamburgerHost.IsPaneOpen = false;
            OpenSettingsPopup();
        }

        private void OpenSettingsPopup()
        {
            SettingsPopupLayer.Visibility = Visibility.Visible;
            SettingsFrame.Navigate(typeof(SettingsView), _settings);
        }

        /// <summary>Колбэк из SettingsView: применить/отменить.</summary>
        public void CloseSettingsPopup(AppSettings result)
        {
            SettingsPopupLayer.Visibility = Visibility.Collapsed;
            SettingsFrame.Content = null;
            if (result == null) return; // отмена

            bool pathsChanged = !_settings.SelectedPaths.SequenceEqual(result.SelectedPaths)
                             || _settings.IncludeSubdirectories != result.IncludeSubdirectories;

            _settings = App.Settings = result;
            SettingsService.Save(_settings);

            App.ApplyTheme(_settings.Theme);
            SystemIntegration.PreventSleep(_isPlaying && _settings.PreventSleep);
            SldInterval.Value = _settings.SlideshowIntervalSeconds;

            if (pathsChanged) _ = LoadPhotosAsync();
            else UpdateStatus();
        }

        // ─── Полный экран / выход (адаптация Window → ApplicationView) ────────────

        private void ToggleFullscreen()
        {
            var av = ApplicationView.GetForCurrentView();
            _isFullscreen = !_isFullscreen;
            if (_isFullscreen) av.TryEnterFullScreenMode();
            else av.ExitFullScreenMode();
        }

        private void OnGlobalKeyDown(CoreWindow sender, KeyEventArgs args)
        {
            switch (args.VirtualKey)
            {
                case VirtualKey.Space:
                    OnTogglePlay(this, null); args.Handled = true; break;
                case VirtualKey.Right:
                case VirtualKey.PageDown:
                    Advance(); args.Handled = true; break;
                case VirtualKey.Left:
                case VirtualKey.PageUp:
                    Back(); args.Handled = true; break;
                case VirtualKey.Escape:
                    if (SettingsPopupLayer.Visibility == Visibility.Visible)
                        CloseSettingsPopup(null);
                    else if (_isFullscreen) ToggleFullscreen();
                    args.Handled = true; break;
                case VirtualKey.F11:
                    ToggleFullscreen(); args.Handled = true; break;
            }
        }

        private void OnExitApp(object sender, RoutedEventArgs e)
        {
            try
            {
                SettingsService.Save(_settings);
                Application.Current.Exit();
            }
            catch { }
        }

        // ─── Жизненный цикл (Suspending/Resuming) ─────────────────────────────────

        public void PrepareForSuspend()
        {
            _timer?.Stop();
            SettingsService.Save(_settings);
        }

        public void ResumeAfterSuspend()
        {
            if (_isPlaying && _timer != null) _timer.Start();
        }
    }
}
