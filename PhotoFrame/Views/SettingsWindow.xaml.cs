// SettingsWindow.xaml.cs — v3.2
// TryFindResource везде. ClickOnce env vars (.NET 8). Photo count stats.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoFrame.Converters;
using PhotoFrame.Helpers;
using PhotoFrame.Models;
using PhotoFrame.Services;

namespace PhotoFrame.Views
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Result { get; private set; }
        private readonly AppSettings _w;
        private readonly ObservableCollection<string> _paths = new();
        private ScanResult? _lastScan;

        private static readonly JsonSerializerOptions _jo = new()
        { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        public SettingsWindow(AppSettings current)
        {
            InitializeComponent();
            _w     = Clone(current);
            Result = current;
        }

        // ─── Загрузка ─────────────────────────────────────────────────────────────

        private void OnLoaded(object s, RoutedEventArgs e)
        {
            try { LoadGeneral(); }    catch { }
            try { LoadSources(); }    catch { }
            try { LoadOverlays(); }   catch { }
            try { LoadPlayback(); }   catch { }
            try { LoadAppearance(); } catch { }
            try { LoadSystem(); }     catch { }
            try { LoadPower(); }      catch { }

            TbStorage.Text = $"Настройки: {SettingsService.StoragePath}";
        }

        private void LoadGeneral()
        {
            SldInterval.Value      = _w.SlideshowIntervalSeconds;
            ChkAutoStart.IsChecked = _w.AutoStart;
            ChkLoop.IsChecked      = _w.LoopSlideshow;
            ChkRecursive.IsChecked = _w.IncludeSubdirectories;
        }

        private void LoadSources()
        {
            foreach (var p in _w.SelectedPaths) _paths.Add(p);
            PathList.ItemsSource = _paths;
            FillDriveButtons();
            ChkWatchRemovable.IsChecked   = _w.WatchRemovableMedia;
            ChkSuggestRemovable.IsChecked = _w.SuggestRemovableMedia;
        }

        private void LoadOverlays()
        {
            ChkDir.IsChecked  = _w.ShowDirectoryOverlay;
            ChkDate.IsChecked = _w.ShowDateOverlay;
            ChkLoc.IsChecked  = _w.ShowLocationOverlay;
            SldFont.Value     = _w.OverlayFontSize;
        }

        private void LoadPlayback()
        {
            FillCombo<PlayMode>(CmbPlayMode,         new PlayModeToStringConverter());
            FillCombo<TransitionType>(CmbTransition, new TransitionTypeToStringConverter());
            SelectTag(CmbPlayMode,   _w.PlayMode);
            SelectTag(CmbTransition, _w.TransitionType);
            SldDuration.Value = _w.TransitionDurationSeconds;
        }

        private void LoadAppearance()
        {
            FillCombo<AppTheme>(CmbTheme, new AppThemeToStringConverter());
            SelectTag(CmbTheme, _w.Theme);
            ChkMica.IsChecked = _w.EnableMicaEffect;
            // Показываем текущий активный эффект прозрачности
            // (передаётся через AppSettings не хранится — берём из MainWindow если доступно)
            TbEffectStatus.Text = _w.EnableMicaEffect
                ? "Эффект прозрачности включён. Win11 = Mica, Win10 = Acrylic blur."
                : "Эффект прозрачности выключен.";
        }

        private void LoadSystem()
        {
            ChkAutorun.IsChecked   = SystemIntegration.IsAutostartEnabled();
            ChkMinToTray.IsChecked = _w.MinimizeToTray;
            bool scrReg = SystemIntegration.IsScreensaverRegistered();
            ChkScrReg.IsChecked = scrReg;
            TbScrStatus.Text    = scrReg ? "✔ Зарегистрирован" : "Не зарегистрирован";
            SldScrDelay.Value   = _w.ScreensaverDelayMinutes;
        }

        private void LoadPower()
        {
            ChkNoSleep.IsChecked = _w.PreventSleep;
            SldMonOff.Value      = _w.MonitorOffAfterMinutes;
            SldSleep.Value       = _w.SleepAfterMinutes;
        }

        // ─── О программе ─────────────────────────────────────────────────────────

        private void LoadAbout()
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            if (TbAboutVersion != null && ver != null)
                TbAboutVersion.Text =
                    $"PhotoFrame  v{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";

            // ClickOnce (.NET 8 — через переменные среды)
            if (TbClickOnceInfo != null)
                TbClickOnceInfo.Text = SystemIntegration.GetClickOnceInfo();
        }

        // ─── Навигация ────────────────────────────────────────────────────────────

        private void OnNavChanged(object s, SelectionChangedEventArgs e)
        {
            if (NavList?.SelectedItem is not ListBoxItem item) return;

            foreach (StackPanel p in new[] { PGeneral, PSources, POverlays,
                                              PPlayback, PAppearance, PSystem, PPower, PAbout })
                if (p != null) p.Visibility = Visibility.Collapsed;

            var tag = item.Tag as string ?? "";
            var target = tag switch
            {
                "General"    => PGeneral,
                "Sources"    => PSources,
                "Overlays"   => POverlays,
                "Playback"   => PPlayback,
                "Appearance" => PAppearance,
                "System"     => PSystem,
                "Power"      => PPower,
                "About"      => PAbout,
                _            => PGeneral
            };
            if (target != null) target.Visibility = Visibility.Visible;

            if (tag == "Sources") _ = LoadPreviewAsync();
            if (tag == "About")   LoadAbout();
        }

        // ─── Источники ────────────────────────────────────────────────────────────

        private void FillDriveButtons()
        {
            if (DriveBtns == null) return;
            DriveBtns.Children.Clear();
            foreach (var d in FileScanner.GetAvailableDrives())
            {
                try
                {
                    double gb   = d.TotalSize / (1024.0 * 1024 * 1024);
                    string root = d.RootDirectory.FullName;
                    var btn = new Button
                    {
                        Content    = $"{d.Name} ({gb:F0} ГБ)",
                        Margin     = new Thickness(0, 0, 6, 6),
                        Padding    = new Thickness(12, 5, 12, 5),
                        Height     = 30, FontSize = 12,
                        FontFamily = new FontFamily("Segoe UI"),
                        Cursor     = System.Windows.Input.Cursors.Hand,
                        Style      = TryFindResource("SecondaryButton") as Style
                    };
                    btn.Click += (_, __) => { AddPath(root); _ = LoadPreviewAsync(); };
                    DriveBtns.Children.Add(btn);
                }
                catch { }
            }
        }

        private void OnAddFolder(object s, RoutedEventArgs e)
        {
            try
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Выберите папку с фотографиями",
                    ShowNewFolderButton = false, UseDescriptionForTitle = true
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                { AddPath(dlg.SelectedPath); _ = LoadPreviewAsync(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "PhotoFrame",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnAddDrive(object s, RoutedEventArgs e)
        {
            try
            {
                var drives = FileScanner.GetAvailableDrives();
                if (drives.Count == 0) return;

                var dlg = new Window
                {
                    Title = "Выбор диска", Width = 340, Height = 165,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Background = TryFindResource("DialogBgBrush") as Brush
                               ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E))
                };

                var cmb = new ComboBox { Margin = new Thickness(16, 8, 16, 8), FontSize = 13,
                    FontFamily = new FontFamily("Segoe UI") };
                foreach (var d in drives)
                    cmb.Items.Add($"{d.Name}  [{d.VolumeLabel}]  ({d.TotalSize / (1024 * 1024 * 1024L)} ГБ)");
                cmb.SelectedIndex = 0;

                var row = new StackPanel { Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(16, 4, 16, 8) };

                Brush fgBrush = TryFindResource("TextPrimary") as Brush ?? Brushes.White;
                var btnOk = new Button { Content = "Добавить", MinWidth = 90, Height = 30,
                    Margin = new Thickness(0,0,8,0), FontSize = 13,
                    FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromRgb(0x00,0x78,0xD4)),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand };
                var btnCn = new Button { Content = "Отмена", MinWidth = 80, Height = 30,
                    FontSize = 13, FontFamily = new FontFamily("Segoe UI"),
                    Cursor = System.Windows.Input.Cursors.Hand };

                btnOk.Click += (_, __) => { dlg.DialogResult = true;  dlg.Close(); };
                btnCn.Click += (_, __) => { dlg.DialogResult = false; dlg.Close(); };
                row.Children.Add(btnOk); row.Children.Add(btnCn);

                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = "Выберите диск:",
                    Margin = new Thickness(16,12,16,4), FontSize = 13,
                    FontFamily = new FontFamily("Segoe UI"), Foreground = fgBrush });
                sp.Children.Add(cmb); sp.Children.Add(row);
                dlg.Content = sp;

                if (dlg.ShowDialog() == true && cmb.SelectedIndex >= 0)
                { AddPath(drives[cmb.SelectedIndex].RootDirectory.FullName); _ = LoadPreviewAsync(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "PhotoFrame",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnRemovePath(object s, RoutedEventArgs e)
        {
            if (PathList?.SelectedItem is string p)
            { _paths.Remove(p); _ = LoadPreviewAsync(); }
        }

        private void AddPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
                if (!_paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _paths.Add(path);
            }
            catch { }
        }

        // ─── Предпросмотр + статистика ────────────────────────────────────────────

        private async Task LoadPreviewAsync()
        {
            if (PreviewPanel == null) return;
            PreviewPanel.Children.Clear();

            if (_paths.Count == 0)
            {
                ShowPreviewMsg("Нет выбранных папок"); return;
            }
            ShowPreviewMsg("Сканирование…");

            var pathsCopy = _paths.ToList();
            _lastScan = await FileScanner.ScanAsync(pathsCopy, _w.IncludeSubdirectories);

            PreviewPanel.Children.Clear();

            // Обновляем статистику
            if (TbIndexStats != null)
                TbIndexStats.Text =
                    $"Проиндексировано: {_lastScan.Photos.Count} фото  " +
                    $"/ {_lastScan.TotalFilesScanned} файлов всего  " +
                    $"/ {_lastScan.DirectoriesScanned} папок";

            if (_lastScan.Photos.Count == 0)
            { ShowPreviewMsg("Фотографии не найдены"); return; }

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

            foreach (var photo in _lastScan.Photos.Take(12))
            {
                var thumb = await Task.Run(() => LoadThumb(photo.FilePath));
                if (thumb == null) continue;

                var img = new Image { Source = thumb, Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                Brush bg = TryFindResource("Surface2Brush") as Brush
                        ?? new SolidColorBrush(Color.FromRgb(0x2A,0x2A,0x2A));

                wrap.Children.Add(new Border
                {
                    Width = 86, Height = 86, Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(3), ClipToBounds = true,
                    ToolTip = photo.FilePath, Background = bg, Child = img
                });
            }
            PreviewPanel.Children.Add(wrap);

            if (_lastScan.Photos.Count > 12)
                PreviewPanel.Children.Add(new TextBlock
                {
                    Text = $"… ещё {_lastScan.Photos.Count - 12} фото",
                    FontSize = 11, FontFamily = new FontFamily("Segoe UI"),
                    Foreground = TryFindResource("TextSecondary") as Brush ?? Brushes.Gray,
                    Margin = new Thickness(0, 4, 0, 0)
                });
        }

        private void ShowPreviewMsg(string msg)
        {
            PreviewPanel?.Children.Clear();
            PreviewPanel?.Children.Add(new TextBlock
            {
                Text = msg, FontSize = 12, FontFamily = new FontFamily("Segoe UI"),
                Foreground = TryFindResource("TextSecondary") as Brush ?? Brushes.Gray,
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        private static BitmapImage? LoadThumb(string path)
        {
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(path, UriKind.Absolute);
                b.DecodePixelWidth = 86;
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                b.EndInit(); b.Freeze();
                return b;
            }
            catch { return null; }
        }

        // ─── Слайдеры ─────────────────────────────────────────────────────────────

        private void OnIntervalChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TbIntervalVal == null) return;
            int v = (int)Math.Round(e.NewValue);
            TbIntervalVal.Text = v >= 60 ? $"{v/60}м {v%60:D2}с" : $"{v} с";
        }
        private void OnFontChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbFontVal != null) TbFontVal.Text = $"{Math.Round(e.NewValue,1)} pt"; }
        private void OnDurChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbDurVal != null) TbDurVal.Text = $"{e.NewValue:F2} с"; }
        private void OnScrDelayChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbScrDelayVal != null) TbScrDelayVal.Text = $"{(int)e.NewValue} мин"; }
        private void OnMonOffChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbMonOffVal != null) TbMonOffVal.Text = (int)e.NewValue == 0 ? "Не управлять" : $"{(int)e.NewValue} мин"; }
        private void OnSleepChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbSleepVal != null) TbSleepVal.Text = (int)e.NewValue == 0 ? "Не управлять" : $"{(int)e.NewValue} мин"; }

        // ─── Скринсейвер ──────────────────────────────────────────────────────────

        private void OnScrRegChanged(object s, RoutedEventArgs e)
        {
            try
            {
                if (ChkScrReg.IsChecked == true)
                {
                    bool ok = SystemIntegration.RegisterScreensaver(out string err);
                    if (!ok)
                    {
                        MessageBox.Show(
                            $"Не удалось зарегистрировать скринсейвер:\n{err}\n\n" +
                            "Диалог UAC должен был появиться — убедитесь что разрешили действие.",
                            "Скринсейвер", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ChkScrReg.IsChecked = false;
                    }
                    TbScrStatus.Text = ok ? "✔ Зарегистрирован" : "Ошибка";
                }
                else
                {
                    SystemIntegration.UnregisterScreensaver();
                    TbScrStatus.Text = "Не зарегистрирован";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "PhotoFrame",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnApplyPower(object s, RoutedEventArgs e)
        {
            bool ok = SystemIntegration.SetPowerTimeouts(
                (int)SldMonOff.Value * 60, (int)SldSleep.Value * 60);
            MessageBox.Show(ok ? "Применено." : "Не удалось применить.",
                "Питание", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        // ─── О программе / обновления ─────────────────────────────────────────────
        // v3.3: полноценный цикл — проверка → ПРЯМОЕ скачивание с GitHub Release
        // (с проверкой SHA-256, если CI выложил sidecar) → запуск установщика
        // с корректным UAC. Контур обновлений НЕ зависит от лицензирования Pro.

        private Services.UpdateInfo? _pendingUpdate;

        private async void OnCheckUpdates(object s, RoutedEventArgs e)
        {
            if (TbUpdateStatus == null) return;
            TbUpdateStatus.Text   = "Проверяем…";
            BtnCheckUpdates.IsEnabled  = false;
            BtnDownloadUpdate.Visibility = Visibility.Collapsed;
            try
            {
                var cur = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
                var res = await Services.UpdateService.CheckAsync(cur);

                string curStr = $"v{cur.Major}.{cur.Minor}.{cur.Build}.{cur.Revision}";
                if (!res.Ok)
                { TbUpdateStatus.Text = $"Не удалось связаться с GitHub: {res.Error}"; return; }

                if (!res.IsNewer || res.Update == null)
                { TbUpdateStatus.Text = $"✔ Актуальная версия установлена ({curStr})."; return; }

                _pendingUpdate = res.Update;
                if (string.IsNullOrEmpty(res.Update.AssetUrl))
                {
                    TbUpdateStatus.Text = $"⬆ Доступна {_pendingUpdate.Tag} (у вас {curStr}). {res.Error}";
                    return;
                }
                TbUpdateStatus.Text =
                    $"⬆ Доступна версия {_pendingUpdate.Tag} (у вас: {curStr}), " +
                    $"{_pendingUpdate.AssetSizeBytes / 1024 / 1024:F1} МБ.\n" +
                    "«Скачать и установить» — установщик запустится сам; при установке " +
                    "для всех пользователей Windows покажет запрос UAC.";
                BtnDownloadUpdate.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { TbUpdateStatus.Text = $"Ошибка: {ex.Message}"; }
            finally { BtnCheckUpdates.IsEnabled = true; }
        }

        private async void OnDownloadUpdate(object s, RoutedEventArgs e)
        {
            var info = _pendingUpdate;
            if (info == null || string.IsNullOrEmpty(info.AssetUrl)) return;

            BtnDownloadUpdate.IsEnabled = false;
            BtnCheckUpdates.IsEnabled   = false;
            PgUpdate.Visibility         = Visibility.Visible;
            PgUpdate.Value              = 0;
            try
            {
                var progress = new Progress<double>(p => Dispatcher.Invoke(() =>
                {
                    if (p >= 0) { PgUpdate.IsIndeterminate = false; PgUpdate.Value = p * 100; }
                    else        { PgUpdate.IsIndeterminate = true; }
                }));

                TbUpdateStatus.Text = "Скачиваем установщик…";
                string path = await Services.UpdateService.DownloadAsync(info, progress);
                PgUpdate.IsIndeterminate = false; PgUpdate.Value = 100;

                TbUpdateStatus.Text = info.Sha256AssetUrl != null
                    ? "SHA-256 совпал. Запускаем установщик…"
                    : "Запускаем установщик… (sidecar-чексумма не найдена — пропущено)";

                bool started = Services.UpdateService.Install(path);
                if (!started)
                {
                    TbUpdateStatus.Text =
                        "Не удалось запустить установщик (UAC отменён?).\n" +
                        "Откройте «Страница релизов» и запустите Setup вручную.";
                    return;
                }
                // Установщик пошёл — закрываем приложение, чтобы InnoSetup мог
                // перезаписать файлы без блокировки.
                Application.Current.Shutdown();
            }
            catch (InvalidDataException ex)
            { TbUpdateStatus.Text = "⛔ " + ex.Message; }
            catch (Exception ex)
            { TbUpdateStatus.Text = $"Ошибка загрузки: {ex.Message}"; }
            finally
            {
                BtnDownloadUpdate.IsEnabled = true;
                BtnCheckUpdates.IsEnabled   = true;
                PgUpdate.Visibility         = Visibility.Collapsed;
            }
        }

        private void OnOpenReleases(object s, RoutedEventArgs e) => Services.UpdateService.OpenReleasesPage();
        private void OnOpenGitHub(object s, RoutedEventArgs e)   => SystemIntegration.OpenGitHub();

        // ─── OK / Отмена ──────────────────────────────────────────────────────────

        private void OnApply(object s, RoutedEventArgs e)
        {
            try
            {
                Collect();
                SystemIntegration.SetAutostart(ChkAutorun?.IsChecked == true);
                if (_w.RegisterAsScreensaver)
                    SystemIntegration.SetScreensaverDelay(_w.ScreensaverDelayMinutes);
                Result = _w; DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения:\n{ex.Message}", "PhotoFrame",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;

        // ─── Сбор значений ────────────────────────────────────────────────────────

        private void Collect()
        {
            _w.SlideshowIntervalSeconds = (int)Math.Round(SldInterval?.Value ?? 5);
            _w.AutoStart                = ChkAutoStart?.IsChecked == true;
            _w.LoopSlideshow            = ChkLoop?.IsChecked      == true;
            _w.IncludeSubdirectories    = ChkRecursive?.IsChecked == true;

            _w.SelectedPaths = _paths.ToList();
            _w.WatchRemovableMedia   = ChkWatchRemovable?.IsChecked   == true;
            _w.SuggestRemovableMedia = ChkSuggestRemovable?.IsChecked == true;

            _w.ShowDirectoryOverlay = ChkDir?.IsChecked  == true;
            _w.ShowDateOverlay      = ChkDate?.IsChecked == true;
            _w.ShowLocationOverlay  = ChkLoc?.IsChecked  == true;
            _w.OverlayFontSize      = Math.Round(SldFont?.Value ?? 15, 1);

            if (SelectedTag<PlayMode>(CmbPlayMode, out var pm))       _w.PlayMode = pm;
            if (SelectedTag<TransitionType>(CmbTransition, out var tt)) _w.TransitionType = tt;
            _w.TransitionDurationSeconds = Math.Round(SldDuration?.Value ?? 0.75, 2);

            if (SelectedTag<AppTheme>(CmbTheme, out var th)) _w.Theme = th;
            _w.EnableMicaEffect = ChkMica?.IsChecked == true;

            _w.MinimizeToTray        = ChkMinToTray?.IsChecked == true;
            _w.RegisterAsScreensaver = ChkScrReg?.IsChecked    == true;
            _w.ScreensaverDelayMinutes = (int)(SldScrDelay?.Value ?? 5);

            _w.PreventSleep            = ChkNoSleep?.IsChecked == true;
            _w.MonitorOffAfterMinutes  = (int)(SldMonOff?.Value ?? 0);
            _w.SleepAfterMinutes       = (int)(SldSleep?.Value  ?? 0);
        }

        // ─── Вспомогательные ──────────────────────────────────────────────────────

        private static void FillCombo<T>(ComboBox cmb, System.Windows.Data.IValueConverter conv)
            where T : struct, Enum
        {
            if (cmb == null) return;
            cmb.Items.Clear();
            foreach (T val in Enum.GetValues(typeof(T)))
                cmb.Items.Add(new ComboBoxItem
                {
                    Content = conv.Convert(val, typeof(string), null,
                        System.Globalization.CultureInfo.InvariantCulture) as string ?? val.ToString(),
                    Tag = val
                });
        }

        private static void SelectTag(ComboBox? cmb, object tag)
        {
            if (cmb == null) return;
            foreach (ComboBoxItem i in cmb.Items)
                if (Equals(i.Tag, tag)) { cmb.SelectedItem = i; return; }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private static bool SelectedTag<T>(ComboBox? cmb, out T val) where T : struct
        {
            if (cmb?.SelectedItem is ComboBoxItem ci && ci.Tag is T v) { val = v; return true; }
            val = default; return false;
        }

        private static AppSettings Clone(AppSettings src)
        {
            try
            {
                string j = JsonSerializer.Serialize(src, _jo);
                return JsonSerializer.Deserialize<AppSettings>(j, _jo) ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }
    }
}
