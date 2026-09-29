using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PhotoFrame.Models;
using PhotoFrame.Services;

namespace PhotoFrame.Views
{
    /// <summary>Независимый полноэкранный поток для дополнительного монитора.</summary>
    public partial class MonitorFrameWindow : Window
    {
        private readonly PhotoFrameProfile _profile;
        private readonly int _monitorIndex;
        private readonly DispatcherTimer _timer = new();
        private readonly PlaylistManager _playlist = new();
        private TransitionEngine? _engine;
        private CancellationTokenSource? _cts;
        private bool _loaded;

        public MonitorFrameWindow(PhotoFrameProfile profile, int monitorIndex)
        {
            _profile = profile;
            _monitorIndex = monitorIndex;
            InitializeComponent();
            _timer.Tick += async (_, __) => await NextAsync();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            MultiMonitorService.PlaceWindow(this, _monitorIndex);
            WindowState = WindowState.Maximized;
            _engine = new TransitionEngine(ImgA, ImgB, (FrameworkElement)Content);
            _cts = new CancellationTokenSource();
            await LoadAsync(_cts.Token);
            _loaded = true;
            _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(_profile.SlideshowIntervalSeconds, 1, 3600));
            _timer.Start();
        }

        private async Task LoadAsync(CancellationToken token)
        {
            var scan = await FileScanner.ScanAsync(_profile.SelectedPaths, true, cancellationToken: token);
            if (scan.Photos.Count == 0) return;
            if (_profile.PlayMode is PlayMode.DateAscending or PlayMode.DateDescending)
                await MetadataReader.PopulateDatesAsync(scan.Photos, null, token);
            _playlist.SetPhotos(scan.Photos, _profile.PlayMode);
            await ShowCurrentAsync(false, token);
        }

        private async Task NextAsync()
        {
            if (!_loaded || _engine?.IsTransitioning == true) return;
            var next = _playlist.Next(_profile.PlayMode, _profile.Loop);
            if (next == null) { _timer.Stop(); return; }
            await ShowCurrentAsync(true, CancellationToken.None);
        }

        private async Task ShowCurrentAsync(bool animate, CancellationToken token)
        {
            var photo = _playlist.Current;
            if (photo == null) return;
            if (!photo.MetadataLoaded) await Task.Run(() => MetadataReader.Populate(photo), token);
            var bmp = await Task.Run(() => LoadBitmap(photo.FilePath), token);
            if (bmp == null) return;
            ImgA.Stretch = _profile.ScalingMode == DisplayScalingMode.Fill || _profile.ScalingMode == DisplayScalingMode.SmartCrop
                ? System.Windows.Media.Stretch.UniformToFill : System.Windows.Media.Stretch.Uniform;
            if (animate && _engine != null)
                _engine.Transition(bmp, _profile.TransitionType, _profile.TransitionDurationSeconds,
                    allowAdvanced: true); // Free-версия: все переходы доступны без лицензионного гейта (Pro-гейт живёт в internal-сборке).
            else _engine?.ShowImmediate(bmp);
        }

        private static System.Windows.Media.Imaging.BitmapImage? LoadBitmap(string path)
        {
            try
            {
                var b = new System.Windows.Media.Imaging.BitmapImage();
                b.BeginInit(); b.UriSource = new Uri(path, UriKind.Absolute);
                b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                b.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat;
                b.EndInit(); b.Freeze(); return b;
            }
            catch { return null; }
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer.Stop(); _cts?.Cancel(); _cts?.Dispose();
        }
    }
}
