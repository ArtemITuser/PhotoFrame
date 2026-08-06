// Views/SettingsWindow.xaml.cs — v3.7 (build 53)
// + Импорт списка папок из .txt/.csv (симметрично экспорту), корректные
//   MDL2-глифы E896/E8A1 (Download/OpenFile — семантика "получить/открыть")
// TryFindResource everywhere. Per-section try/catch in OnLoaded.
// UiMode (Modern/Aero7). CounterFormat. LiveTiles. About/GitHub/UpdateCheck.
//
// build 53: LiveTilesLargeEnabled и RestoreLastSessionState подключены к UI
// (были/были бы мёртвыми настройками); слайдеры Font/Duration/TileInterval
// получили пару Slider+TextBox (touch-friendly аудит); теги съёмных
// носителей (_removablePaths) для FillDriveButtons/OnAddDrive — см.
// AppSettings.RemovableSourcePaths; исправлена регрессия иконок импорта
// (txt/CSV делили один и тот же глиф); удалены 2 пустых
// автосгенерированных обработчика ListBoxItem_Selected(_1).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoFrame.Converters;
using PhotoFrame.Models;
using PhotoFrame.Services;

namespace PhotoFrame.Views
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Result { get; private set; }
        private readonly AppSettings _w;
        private readonly ObservableCollection<string> _paths = new();
        /// <summary>Подмножество _paths, добавленное со съёмных носителей
        /// (build 53) — см. AppSettings.RemovableSourcePaths. Отдельный
        /// набор, а не флаг на элементе _paths, чтобы не менять тип
        /// коллекции, к которой уже привязан PathList.</summary>
        private readonly HashSet<string> _removablePaths = new(StringComparer.OrdinalIgnoreCase);
        private ScanResult? _lastScan;

        private static readonly JsonSerializerOptions _jo = new()
        { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        public SettingsWindow(AppSettings current)
        {
            InitializeComponent();
            _w     = Clone(current);
            Result = current;
        }

        // ─── LOADED ──────────────────────────────────────────────────────────────

        private void OnLoaded(object s, RoutedEventArgs e)
        {
            // Apply DWM dark title for settings window too
            Helpers.WindowHelper.SetTitleBarDarkMode(this,
                App.CurrentTheme == AppTheme.Dark);

            try { LoadGeneral(); }    catch { }
            try { LoadSources(); }    catch { }
            try { LoadOverlays(); }   catch { }
            try { LoadPlayback(); }   catch { }
            try { LoadAppearance(); } catch { }
            try { LoadSystem(); }     catch { }
            try { LoadPower(); }      catch { }

            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            if (TbFooterVer != null && ver != null)
                TbFooterVer.Text =
                    $"v{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";
            if (TbStorage != null)
                TbStorage.Text = $"Настройки: {SettingsService.StoragePath}";
        }

        private void LoadGeneral()
        {
            SldInterval.Value      = _w.SlideshowIntervalSeconds;
            ChkAutoStart.IsChecked = _w.AutoStart;
            ChkLoop.IsChecked      = _w.LoopSlideshow;
            ChkRecursive.IsChecked = _w.IncludeSubdirectories;
            ChkRestoreSession.IsChecked = _w.RestoreLastSessionState;
            FillCombo<CounterFormat>(CmbCounterFormat, new CounterFormatToStringConverter());
            SelectTag(CmbCounterFormat, _w.CounterDisplayFormat);
        }

        private void LoadSources()
        {
            foreach (var p in _w.SelectedPaths) _paths.Add(p);
            foreach (var p in _w.RemovableSourcePaths) _removablePaths.Add(p);
            PathList.ItemsSource            = _paths;
            ChkWatchRemovable.IsChecked     = _w.WatchRemovableMedia;
            ChkSuggestRemovable.IsChecked   = _w.SuggestRemovableMedia;
            FillDriveButtons();
        }

        private void LoadOverlays()
        {
            ChkDir.IsChecked     = _w.ShowDirectoryOverlay;
            ChkDate.IsChecked    = _w.ShowDateOverlay;
            ChkLoc.IsChecked     = _w.ShowLocationOverlay;
            ChkGeocode.IsChecked = _w.GpsReverseGeocodeEnabled;
            SldFont.Value        = _w.OverlayFontSize;
        }

        private void LoadPlayback()
        {
            FillCombo<PlayMode>(CmbPlayMode,         new PlayModeToStringConverter());
            FillCombo<TransitionType>(CmbTransition, new TransitionTypeToStringConverter());
            SelectTag(CmbPlayMode,   _w.PlayMode);
            SelectTag(CmbTransition, _w.TransitionType);
            SldDuration.Value = _w.TransitionDurationSeconds;
        }

        private bool _loadingAppearance;

        private void LoadAppearance()
        {
            _loadingAppearance = true;
            FillCombo<AppTheme>(CmbTheme, new AppThemeToStringConverter());
            SelectTag(CmbTheme, _w.Theme);
            ChkMica.IsChecked        = _w.EnableMicaEffect;
            ChkLiveTiles.IsChecked   = _w.LiveTilesEnabled;
            ChkLiveTilesLarge.IsChecked = _w.LiveTilesLargeEnabled;
            SldTileInterval.Value    = Math.Max(0, Math.Min(120, _w.LiveTileCycleIntervalSeconds));
            FillCombo<TilePhotoDistance>(CmbTileDistance, new TilePhotoDistanceToStringConverter());
            SelectTag(CmbTileDistance, _w.TilePhotoDistance);

            FillCombo<UiMode>(CmbUiMode, new UiModeToStringConverter());
            SelectTag(CmbUiMode, _w.UiMode);
            _loadingAppearance = false;

            if (TbEffectStatus != null)
                TbEffectStatus.Text = _w.EnableMicaEffect
                    ? "Включено. Win11 = Mica, Win10 = Acrylic blur."
                    : "Выключено — сплошной фон.";

            // build 53: кнопка закрепления плитки видна только когда пиннинг
            // реально может сработать — т.е. процесс запущен из MSIX-пакета
            // (package identity), а не просто на Windows 10/11 (WinRT-типы
            // резолвятся в ОС независимо от identity, поэтому раньше кнопка
            // показывалась и там, где закрепить было физически невозможно).
            if (BtnPinTile != null)
                BtnPinTile.Visibility = LiveTileService.IsPinningSupported()
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool _loadingAutoOff;

        private void LoadSystem()
        {
            ChkAutorun.IsChecked   = SystemIntegration.IsAutostartEnabled();
            ChkMinToTray.IsChecked = _w.MinimizeToTray;
            bool scr = SystemIntegration.IsScreensaverRegistered();
            ChkScrReg.IsChecked = scr;
            if (TbScrStatus != null)
                TbScrStatus.Text = scr ? "✔ Зарегистрирован" : "Не зарегистрирован";
            SldScrDelay.Value = _w.ScreensaverDelayMinutes;

            _loadingAutoOff = true;
            FillCombo<AutoOffMode>(CmbAutoOffMode, new AutoOffModeToStringConverter());
            SelectTag(CmbAutoOffMode, _w.AutoOffMode);
            SldAutoOffFrom.Value = _w.AutoOffFromMinutes;
            SldAutoOffTo.Value   = _w.AutoOffToMinutes;
            ChkAutoOffManualCoords.IsChecked = _w.AutoOffUseManualCoords;
            TbAutoOffLat.Text = _w.AutoOffLatitude?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "";
            TbAutoOffLon.Text = _w.AutoOffLongitude?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "";
            PAutoOffCoords.Visibility = _w.AutoOffUseManualCoords
                ? Visibility.Visible : Visibility.Collapsed;
            _loadingAutoOff = false;
            UpdateAutoOffPanelVisibility();
        }

        private void UpdateAutoOffPanelVisibility()
        {
            if (!SelectedTag<AutoOffMode>(CmbAutoOffMode, out var mode)) return;
            PAutoOffManual.Visibility     = mode == AutoOffMode.ManualSchedule    ? Visibility.Visible : Visibility.Collapsed;
            PAutoOffSun.Visibility        = mode == AutoOffMode.SunsetToSunrise   ? Visibility.Visible : Visibility.Collapsed;
            TbAutoOffSmartInfo.Visibility = mode == AutoOffMode.SmartUsage        ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadPower()
        {
            ChkNoSleep.IsChecked = _w.PreventSleep;
            SldMonOff.Value      = _w.MonitorOffAfterMinutes;
            SldSleep.Value       = _w.SleepAfterMinutes;
        }

        // ─── NAVIGATION ──────────────────────────────────────────────────────────

        /// <summary>
        /// Live preview: applies AeroTheme.xaml immediately so the Settings
        /// window itself (and MainWindow, via App-level resources) reflects
        /// the chosen UI mode before the user presses "Применить".
        /// The final mode is also persisted into _w on Apply via Collect().
        /// </summary>
        private void OnUiModeChanged(object s, SelectionChangedEventArgs e)
        {
            if (_loadingAppearance) return;
            if (SelectedTag<UiMode>(CmbUiMode, out var mode))
            {
                App.ApplyUiMode(mode);
                Helpers.WindowHelper.SetTitleBarDarkMode(this,
                    App.CurrentTheme == AppTheme.Dark);
            }
        }

        private void OnNavChanged(object s, SelectionChangedEventArgs e)
        {
            if (NavList?.SelectedItem is not ListBoxItem item) return;

            foreach (var p in new StackPanel?[]
                { PGeneral, PSources, POverlays, PPlayback,
                  PAppearance, PSystem, PPower, PAbout })
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

        // ─── SOURCES ─────────────────────────────────────────────────────────────

        private void FillDriveButtons()
        {
            if (DriveBtns == null) return;
            DriveBtns.Children.Clear();
            foreach (var d in FileScanner.GetAvailableDrives())
            {
                try
                {
                    double gb = d.TotalSize / (1024.0 * 1024 * 1024);
                    string root = d.RootDirectory.FullName;
                    bool removable = d.DriveType == DriveType.Removable;
                    var btn = new Button
                    {
                        Content    = $"{d.Name} ({gb:F0} ГБ)",
                        Margin     = new Thickness(0, 0, 6, 6),
                        Padding    = new Thickness(12, 5, 12, 5),
                        Height     = 32, FontSize = 12,
                        FontFamily = new FontFamily("Segoe UI"),
                        Cursor     = System.Windows.Input.Cursors.Hand,
                        Style      = TryFindResource("SecondaryButton") as Style
                    };
                    btn.Click += (_, __) => { AddPath(root, removable); _ = LoadPreviewAsync(); };
                    DriveBtns.Children.Add(btn);
                }
                catch { }
            }
        }

        private void OnAddFolder(object s, RoutedEventArgs e)
        {
            try
            {
#pragma warning disable CA1416
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Выберите папку с фотографиями",
                    ShowNewFolderButton = false, UseDescriptionForTitle = true
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                { AddPath(dlg.SelectedPath); _ = LoadPreviewAsync(); }
#pragma warning restore CA1416
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "PhotoFrame",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Открывает браузер подключённых USB-устройств (Lumia/Android/iOS),
        /// импортирует выбранную папку в локальный кеш и добавляет её как
        /// обычный источник (FileScanner работает с ней как с любой папкой).
        /// </summary>
        private void OnAddDevice(object s, RoutedEventArgs e)
        {
            try
            {
                var dlg = new DeviceBrowserWindow { Owner = this };
                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.ImportedPath))
                { AddPath(dlg.ImportedPath); _ = LoadPreviewAsync(); }
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

                Brush bg = TryFindResource("DialogBgBrush") as Brush
                        ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
                Brush fg = TryFindResource("TextPrimary") as Brush ?? Brushes.White;

                var dlg = new Window
                {
                    Title = "Выбор диска", Width = 340, Height = 165,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this, Background = bg
                };

                var cmb = new ComboBox
                {
                    Margin = new Thickness(16, 8, 16, 8), FontSize = 13,
                    FontFamily = new FontFamily("Segoe UI"),
                    Background = TryFindResource("InputBgBrush") as Brush,
                    Foreground = TryFindResource("InputFgBrush") as Brush
                };
                foreach (var d in drives)
                    cmb.Items.Add($"{d.Name}  [{d.VolumeLabel}]  ({d.TotalSize/(1024L*1024*1024)} ГБ)");
                cmb.SelectedIndex = 0;

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(16, 4, 16, 8)
                };
                var ok = new Button
                {
                    Content = "Добавить", MinWidth = 90, Height = 32, Margin = new Thickness(0,0,8,0),
                    FontSize = 13, Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    Background = new SolidColorBrush(Color.FromRgb(0,0x78,0xD4)),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var cn = new Button
                {
                    Content = "Отмена", MinWidth = 80, Height = 32, FontSize = 13,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                ok.Click += (_, __) => { dlg.DialogResult = true;  dlg.Close(); };
                cn.Click += (_, __) => { dlg.DialogResult = false; dlg.Close(); };
                row.Children.Add(ok); row.Children.Add(cn);

                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text = "Выберите диск:", Margin = new Thickness(16,12,16,4),
                    FontSize = 13, Foreground = fg
                });
                sp.Children.Add(cmb); sp.Children.Add(row);
                dlg.Content = sp;

                if (dlg.ShowDialog() == true && cmb.SelectedIndex >= 0)
                {
                    var chosen = drives[cmb.SelectedIndex];
                    AddPath(chosen.RootDirectory.FullName, chosen.DriveType == DriveType.Removable);
                    _ = LoadPreviewAsync();
                }
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
            { _paths.Remove(p); _removablePaths.Remove(p); _ = LoadPreviewAsync(); }
        }

        private void AddPath(string path, bool isRemovable = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
                if (!_paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _paths.Add(path);
                // build 53: помечаем как съёмный источник, чтобы его временное
                // отсутствие (флешка не воткнута) не показывалось как ошибка —
                // см. AppSettings.RemovableSourcePaths / FileScanner.ScanAsync.
                // Снимается тег только полным удалением пути (OnRemovePath) —
                // не сбрасываем его на false здесь, чтобы случайное повторное
                // добавление того же пути другим способом (например, через
                // обычный выбор папки) не вернуло докучливый баннер ошибки.
                if (isRemovable) _removablePaths.Add(path);
            }
            catch { }
        }

        // ─── PREVIEW ─────────────────────────────────────────────────────────────

        private async Task LoadPreviewAsync()
        {
            if (PreviewPanel == null) return;
            PreviewPanel.Children.Clear();

            if (_paths.Count == 0) { ShowPreviewMsg("Нет выбранных папок"); return; }
            ShowPreviewMsg("Сканирование…");

            _lastScan = await FileScanner.ScanAsync(_paths.ToList(), _w.IncludeSubdirectories);
            PreviewPanel.Children.Clear();

            if (TbIndexStats != null)
                TbIndexStats.Text =
                    $"Фотографий: {_lastScan.Photos.Count}" +
                    $"   |   Папок: {_lastScan.DirectoriesScanned}" +
                    $"   |   Файлов: {_lastScan.TotalFilesScanned}";

            if (_lastScan.Photos.Count == 0)
            { ShowPreviewMsg("Фотографии не найдены"); return; }

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            Brush bg = TryFindResource("Surface2Brush") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(0x2A,0x2A,0x2A));

            foreach (var photo in _lastScan.Photos.Take(12))
            {
                var thumb = await Task.Run(() => LoadThumbSafe(photo.FilePath));
                if (thumb == null) continue;
                var img = new Image { Source = thumb, Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                wrap.Children.Add(new Border
                {
                    Width = 86, Height = 86, Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(3), ClipToBounds = true,
                    ToolTip = photo.FilePath, Background = bg, Child = img
                });
            }
            PreviewPanel.Children.Add(wrap);

            if (_lastScan.Photos.Count > 12)
            {
                Brush sec = TryFindResource("TextSecondary") as Brush ?? Brushes.Gray;
                PreviewPanel.Children.Add(new TextBlock
                {
                    Text = $"… ещё {_lastScan.Photos.Count - 12} фото",
                    FontSize = 11, Foreground = sec, Margin = new Thickness(0,4,0,0)
                });
            }
        }

        private void ShowPreviewMsg(string msg)
        {
            PreviewPanel?.Children.Clear();
            Brush c = TryFindResource("TextSecondary") as Brush ?? Brushes.Gray;
            PreviewPanel?.Children.Add(new TextBlock
            {
                Text = msg, FontSize = 12, Foreground = c, Margin = new Thickness(0,8,0,0)
            });
        }

        private static BitmapImage? LoadThumbSafe(string path)
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

        // ─── EXPORT ──────────────────────────────────────────────────────────────

        private void OnExportTxt(object s, RoutedEventArgs e) => ExportList(false);
        private void OnExportCsv(object s, RoutedEventArgs e) => ExportList(true);

        private void ExportList(bool csv)
        {
            if (_lastScan == null || _lastScan.Photos.Count == 0)
            {
                MessageBox.Show("Нет данных. Перейдите в «Источники» и подождите сканирования.",
                    "Выгрузка", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = csv ? "CSV|*.csv|Все файлы|*.*" : "TXT|*.txt|Все файлы|*.*",
                    FileName = csv ? "photos.csv" : "photos.txt",
                    DefaultExt = csv ? ".csv" : ".txt"
                };
                if (dlg.ShowDialog() != true) return;
                FileScanner.ExportPhotoList(_lastScan.Photos, dlg.FileName, !csv);
                MessageBox.Show($"Сохранено {_lastScan.Photos.Count} фото.",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── IMPORT ──────────────────────────────────────────────────────────────

        private void OnImportTxt(object s, RoutedEventArgs e) => ImportList(false);
        private void OnImportCsv(object s, RoutedEventArgs e) => ImportList(true);

        /// <summary>
        /// Читает список папок из ранее сохранённого файла (см. <see cref="ExportList"/>)
        /// и добавляет их в источники. Понимает оба формата TXT: дерево вида
        /// "📁 путь" + "   • имя.jpg" (по умолчанию при выгрузке) и плоский список
        /// путей по одному на строку. Для CSV использует колонку "Папка"
        /// (или, если её нет, извлекает каталог из колонки "Путь").
        /// </summary>
        private void ImportList(bool csv)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = csv ? "CSV|*.csv|Все файлы|*.*" : "TXT|*.txt|Все файлы|*.*",
                    Multiselect = false
                };
                if (dlg.ShowDialog() != true) return;

                var lines = File.ReadAllLines(dlg.FileName);
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (csv)
                {
                    foreach (var raw in lines.Skip(1)) // пропускаем заголовок "Путь;Папка;..."
                    {
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var cols = raw.Split(';');
                        // cols[1] = "Папка" если строка соответствует формату экспорта;
                        // иначе берём каталог из cols[0] = "Путь".
                        string? candidate = cols.Length > 1 && !string.IsNullOrWhiteSpace(cols[1])
                            ? cols[1]
                            : (cols.Length > 0 ? SafeGetDirectory(cols[0]) : null);
                        if (!string.IsNullOrWhiteSpace(candidate)) found.Add(candidate);
                    }
                }
                else
                {
                    foreach (var raw in lines)
                    {
                        var line = raw.TrimEnd();
                        if (line.StartsWith("📁 ", StringComparison.Ordinal))
                            found.Add(line[2..].Trim());
                        else if (line.Length > 3 && !line.StartsWith("   •", StringComparison.Ordinal)
                                 && !line.StartsWith("─", StringComparison.Ordinal)
                                 && !line.StartsWith("PhotoFrame", StringComparison.Ordinal)
                                 && !line.StartsWith("Итого", StringComparison.Ordinal)
                                 && (line.Contains(":\\") || line.StartsWith("\\\\", StringComparison.Ordinal)))
                        {
                            // Плоский список путей (файл или папка) — добавляем каталог.
                            var candidate = Directory.Exists(line) ? line : SafeGetDirectory(line);
                            if (!string.IsNullOrWhiteSpace(candidate)) found.Add(candidate!);
                        }
                    }
                }

                int added = 0, skipped = 0;
                foreach (var folder in found)
                {
                    if (Directory.Exists(folder)) { AddPath(folder); added++; }
                    else skipped++;
                }

                if (added > 0) _ = LoadPreviewAsync();
                MessageBox.Show(
                    skipped == 0
                        ? $"Добавлено папок: {added}."
                        : $"Добавлено папок: {added}. Пропущено (не найдено на диске): {skipped}.",
                    "Импорт списка", MessageBoxButton.OK,
                    added > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка импорта: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string? SafeGetDirectory(string path)
        {
            try { return Path.GetDirectoryName(path.Trim()); }
            catch { return null; }
        }

        // ─── SLIDERS ─────────────────────────────────────────────────────────────

        private void OnIntervalChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TbIntervalVal == null) return;
            int v = (int)Math.Round(e.NewValue);
            TbIntervalVal.Text = v >= 60 ? $"{v/60}м {v%60:D2}с" : $"{v} с";
        }
        private void OnFontChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbFontVal != null) TbFontVal.Text = $"{Math.Round(e.NewValue, 1)}"; }
        private void OnFontTextCommit(object s, RoutedEventArgs e)
        {
            if (SldFont == null || TbFontVal == null) return;
            if (TryParseClampedDouble(TbFontVal.Text, SldFont, out double v)) SldFont.Value = v;
            else TbFontVal.Text = $"{Math.Round(SldFont.Value, 1)}";
        }
        private void OnDurChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbDurVal != null) TbDurVal.Text = $"{e.NewValue:F2}"; }
        private void OnDurTextCommit(object s, RoutedEventArgs e)
        {
            if (SldDuration == null || TbDurVal == null) return;
            if (TryParseClampedDouble(TbDurVal.Text, SldDuration, out double v)) SldDuration.Value = v;
            else TbDurVal.Text = $"{SldDuration.Value:F2}";
        }
        // ─── Редактируемые числовые поля (слайдер ↔ текстовое поле) ────────────────
        // build 52: значения теперь можно не только тянуть слайдером, но и
        // вписать вручную. Общий парсер/коммит переиспользуется для всех пар
        // слайдер+TextBox (экономит дублирование, единая логика валидации).

        /// <summary>Enter в редактируемом поле переносит фокус — это штатно
        /// вызывает LostFocus и тем самым фиксирует введённое значение.</summary>
        private void OnEditableFieldKeyDown(object s, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || s is not TextBox tb) return;
            tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }

        /// <summary>Извлекает первое целое число из произвольного текста
        /// ("42 мин", "  17", "не управлять" → null) и ограничивает диапазоном слайдера.</summary>
        private static bool TryParseClampedInt(string? text, Slider slider, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"-?\d+");
            if (!m.Success || !int.TryParse(m.Value, out int v)) return false;
            minutes = Math.Clamp(v, (int)slider.Minimum, (int)slider.Maximum);
            return true;
        }

        /// <summary>Как TryParseClampedInt, но для дробных значений (размер
        /// шрифта, длительность перехода) — build 53.</summary>
        private static bool TryParseClampedDouble(string? text, Slider slider, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var m = System.Text.RegularExpressions.Regex.Match(
                text.Replace(',', '.'), @"-?\d+(\.\d+)?");
            if (!m.Success || !double.TryParse(m.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v)) return false;
            value = Math.Clamp(v, slider.Minimum, slider.Maximum);
            return true;
        }

        private void OnScrDelayChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbScrDelayVal != null) TbScrDelayVal.Text = $"{(int)e.NewValue}"; }
        private void OnScrDelayTextCommit(object s, RoutedEventArgs e)
        {
            if (SldScrDelay == null || TbScrDelayVal == null) return;
            if (TryParseClampedInt(TbScrDelayVal.Text, SldScrDelay, out int v)) SldScrDelay.Value = v;
            else TbScrDelayVal.Text = $"{(int)SldScrDelay.Value}";
        }

        private void OnMonOffChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbMonOffVal != null) TbMonOffVal.Text = $"{(int)e.NewValue}"; }
        private void OnMonOffTextCommit(object s, RoutedEventArgs e)
        {
            if (SldMonOff == null || TbMonOffVal == null) return;
            if (TryParseClampedInt(TbMonOffVal.Text, SldMonOff, out int v)) SldMonOff.Value = v;
            else TbMonOffVal.Text = $"{(int)SldMonOff.Value}";
        }

        private void OnSleepChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbSleepVal != null) TbSleepVal.Text = $"{(int)e.NewValue}"; }
        private void OnSleepTextCommit(object s, RoutedEventArgs e)
        {
            if (SldSleep == null || TbSleepVal == null) return;
            if (TryParseClampedInt(TbSleepVal.Text, SldSleep, out int v)) SldSleep.Value = v;
            else TbSleepVal.Text = $"{(int)SldSleep.Value}";
        }

        private void OnUpdatePeriodChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbUpdatePeriodVal != null) TbUpdatePeriodVal.Text = $"{(int)e.NewValue}"; }
        private void OnUpdatePeriodTextCommit(object s, RoutedEventArgs e)
        {
            if (SldUpdatePeriod == null || TbUpdatePeriodVal == null) return;
            if (TryParseClampedInt(TbUpdatePeriodVal.Text, SldUpdatePeriod, out int v)) SldUpdatePeriod.Value = v;
            else TbUpdatePeriodVal.Text = $"{(int)SldUpdatePeriod.Value}";
        }

        private void OnTileIntervalChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbTileIntervalVal != null) TbTileIntervalVal.Text = $"{(int)e.NewValue}"; }
        private void OnTileIntervalTextCommit(object s, RoutedEventArgs e)
        {
            if (SldTileInterval == null || TbTileIntervalVal == null) return;
            if (TryParseClampedInt(TbTileIntervalVal.Text, SldTileInterval, out int v)) SldTileInterval.Value = v;
            else TbTileIntervalVal.Text = $"{(int)SldTileInterval.Value}";
        }

        private async void OnPinTile(object s, RoutedEventArgs e)
        {
            if (BtnPinTile != null) BtnPinTile.IsEnabled = false;
            bool ok = await LiveTileService.TryPinTileAsync();
            MessageBox.Show(
                ok ? "Плитка PhotoFrame закреплена на начальном экране."
                   : "Не удалось закрепить плитку. Возможно, она уже закреплена.",
                "Плитка", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (BtnPinTile != null) BtnPinTile.IsEnabled = true;
        }

        private void OnSafeCleanup(object s, RoutedEventArgs e)
        {
            var (ok, msg) = SystemIntegration.SafeCleanup();
            if (TbCleanupStatus != null) TbCleanupStatus.Text = msg;
            RefreshMemoryDisplay();
        }

        private void RefreshMemoryDisplay()
        {
            try
            {
                var mi = SystemIntegration.GetMemoryUsageInfo();
                if (TbMemWorkingSet != null)
                    TbMemWorkingSet.Text = $"Рабочий набор: {mi.WorkingSetBytes/1048576} МБ  " +
                                          $"(приватный: {mi.PrivateBytes/1048576} МБ)";
                if (TbMemManaged != null)
                    TbMemManaged.Text = $"Управляемая куча GC: {mi.GcHeapBytes/1048576} МБ";
                if (TbMemCache != null)
                    TbMemCache.Text = mi.ThumbCacheCount > 0
                        ? $"Кеш превью: {mi.ThumbCacheCount} файлов  ({mi.ThumbCacheBytes/1024} КБ)"
                        : "Кеш превью: пуст";
            }
            catch { }
        }

        // ─── АВТООТКЛЮЧЕНИЕ РАМКИ ────────────────────────────────────────────────

        private void OnAutoOffModeChanged(object s, SelectionChangedEventArgs e)
        {
            if (_loadingAutoOff) return;
            UpdateAutoOffPanelVisibility();
        }

        private void OnAutoOffFromChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbAutoOffFromVal != null) TbAutoOffFromVal.Text = FormatMinutesOfDay((int)e.NewValue); }

        private void OnAutoOffToChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbAutoOffToVal != null) TbAutoOffToVal.Text = FormatMinutesOfDay((int)e.NewValue); }

        private void OnAutoOffFromTextCommit(object s, RoutedEventArgs e)
        {
            if (SldAutoOffFrom == null || TbAutoOffFromVal == null) return;
            if (TryParseTimeOfDay(TbAutoOffFromVal.Text, out int mins)) SldAutoOffFrom.Value = mins;
            else TbAutoOffFromVal.Text = FormatMinutesOfDay((int)SldAutoOffFrom.Value);
        }

        private void OnAutoOffToTextCommit(object s, RoutedEventArgs e)
        {
            if (SldAutoOffTo == null || TbAutoOffToVal == null) return;
            if (TryParseTimeOfDay(TbAutoOffToVal.Text, out int mins)) SldAutoOffTo.Value = mins;
            else TbAutoOffToVal.Text = FormatMinutesOfDay((int)SldAutoOffTo.Value);
        }

        private static string FormatMinutesOfDay(int totalMinutes)
            => $"{totalMinutes/60:D2}:{totalMinutes%60:D2}";

        /// <summary>Понимает "23:00", "23.00", "2300" и просто "23" (→ 23:00).</summary>
        private static bool TryParseTimeOfDay(string? text, out int totalMinutes)
        {
            totalMinutes = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim().Replace('.', ':');

            int hh, mm = 0;
            if (text.Contains(':'))
            {
                var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 1 || !int.TryParse(parts[0], out hh)) return false;
                if (parts.Length >= 2 && !int.TryParse(parts[1], out mm)) return false;
            }
            else if (text.Length == 4 && int.TryParse(text, out int packed))
            {
                hh = packed / 100; mm = packed % 100;
            }
            else if (int.TryParse(text, out hh))
            {
                mm = 0;
            }
            else return false;

            if (hh < 0 || hh > 23 || mm < 0 || mm > 59) return false;
            totalMinutes = hh * 60 + mm;
            return true;
        }

        private void OnAutoOffCoordsToggled(object s, RoutedEventArgs e)
        {
            if (PAutoOffCoords != null)
                PAutoOffCoords.Visibility = ChkAutoOffManualCoords.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─── SCREENSAVER ─────────────────────────────────────────────────────────

        private void OnScrRegChanged(object s, RoutedEventArgs e)
        {
            try
            {
                if (ChkScrReg.IsChecked == true)
                {
                    bool ok = SystemIntegration.RegisterScreensaver(out string err);
                    if (!ok)
                    {
                        MessageBox.Show($"Не удалось:\n{err}\n\nПодтвердите UAC-запрос.",
                            "Скринсейвер", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ChkScrReg.IsChecked = false;
                    }
                    if (TbScrStatus != null)
                        TbScrStatus.Text = ok ? "✔ Зарегистрирован" : "Ошибка";
                }
                else
                {
                    SystemIntegration.UnregisterScreensaver();
                    if (TbScrStatus != null) TbScrStatus.Text = "Не зарегистрирован";
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
                (int)(SldMonOff?.Value ?? 0) * 60,
                (int)(SldSleep?.Value  ?? 0) * 60);
            MessageBox.Show(ok ? "Применено." : "Не удалось применить.",
                "Питание", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        // ─── ABOUT ───────────────────────────────────────────────────────────────

        private void LoadAbout()
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            if (TbAboutVersion != null && ver != null)
                TbAboutVersion.Text =
                    $"PhotoFrame  v{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";
            if (TbClickOnceInfo != null)
                TbClickOnceInfo.Text = SystemIntegration.GetClickOnceInfo();
            if (TbUpdateMirror != null)
                TbUpdateMirror.Text = _w.UpdateMirrorUrl ?? "";
            if (ChkAutoCheckUpdates != null)
                ChkAutoCheckUpdates.IsChecked = _w.AutoCheckUpdatesEnabled;
            if (SldUpdatePeriod != null)
                SldUpdatePeriod.Value = Math.Clamp(_w.UpdateCheckPeriodDays, 1, 30);

            RefreshMemoryDisplay();

            // Show 3D app icon in About section
            if (About3DIcon != null)
            {
                try
                {
                    var uri = new Uri("pack://application:,,,/Resources/Icons/AppIcon3D.png");
                    var bmp = new System.Windows.Media.Imaging.BitmapImage(uri);
                    About3DIcon.Source = bmp;
                    About3DIcon.Visibility = System.Windows.Visibility.Visible;
                }
                catch { About3DIcon.Visibility = System.Windows.Visibility.Collapsed; }
            }
        }

        private ReleaseAsset? _pendingUpdateAsset;
        private InstallChannel _installChannel;

        private async void OnCheckUpdates(object s, RoutedEventArgs e)
        {
            if (TbUpdateStatus == null) return;
            TbUpdateStatus.Text = "Проверяем…";
            if (BtnInstallUpdate != null) BtnInstallUpdate.Visibility = Visibility.Collapsed;
            _pendingUpdateAsset = null;
            try
            {
                var cur = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1,0,0,0);
                string? mirror = string.IsNullOrWhiteSpace(TbUpdateMirror?.Text) ? null : TbUpdateMirror.Text.Trim();
                _installChannel = SystemIntegration.DetectInstallChannel();
                var info = await SystemIntegration.CheckUpdateWithAssetAsync(cur, _installChannel, mirror);

                if (string.IsNullOrEmpty(info.Tag))
                {
                    TbUpdateStatus.Text = mirror != null
                        ? "Не удалось связаться с указанным зеркалом."
                        : "Не удалось связаться с GitHub.";
                    return;
                }
                string cs = $"v{cur.Major}.{cur.Minor}.{cur.Build}.{cur.Revision}";
                string channelName = _installChannel switch
                {
                    InstallChannel.Msix      => "MSIX",
                    InstallChannel.ClickOnce => "ClickOnce",
                    InstallChannel.InnoSetup => "InnoSetup",
                    _                        => "переносная/неизвестная установка"
                };

                if (!info.IsNewer)
                {
                    TbUpdateStatus.Text = $"✔ Актуальная версия ({cs}). Канал: {channelName}.";
                }
                else if (_installChannel == InstallChannel.ClickOnce)
                {
                    // ClickOnce обновляется сам — просто "подталкиваем" проверку
                    // на его стороне (см. SystemIntegration.Updates.cs) и не
                    // предлагаем ручное скачивание/установку.
                    SystemIntegration.TryNudgeClickOnceUpdateCheck();
                    TbUpdateStatus.Text = $"⬆ Доступна версия {info.Tag} (у вас {cs}). " +
                        "Обновление ClickOnce происходит автоматически — при следующем запуске.";
                }
                else if (info.Asset != null)
                {
                    _pendingUpdateAsset = info.Asset;
                    TbUpdateStatus.Text = $"⬆ Доступна версия {info.Tag} (у вас {cs}). " +
                        $"Канал: {channelName}. Найден файл: {info.Asset.Name}.";
                    if (BtnInstallUpdate != null) BtnInstallUpdate.Visibility = Visibility.Visible;
                }
                else
                {
                    TbUpdateStatus.Text = $"⬆ Доступна версия {info.Tag} (у вас {cs}), но подходящий " +
                        "под вашу архитектуру файл не найден среди файлов релиза. Откройте страницу релизов вручную.";
                }
                _w.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
            }
            catch (Exception ex) { TbUpdateStatus.Text = $"Ошибка: {ex.Message}"; }
        }

        /// <summary>
        /// build 55: скачивает и ЗАПУСКАЕТ (не молча ставит) найденный
        /// установщик/пакет — см. заголовок SystemIntegration.Updates.cs.
        /// Для InnoSetup это открывает окно Setup.exe, для MSIX — системный
        /// App Installer; дальше пользователь ведёт диалог сам.
        /// </summary>
        private async void OnInstallUpdate(object s, RoutedEventArgs e)
        {
            if (_pendingUpdateAsset == null || TbUpdateStatus == null) return;

            string action = _installChannel == InstallChannel.Msix
                ? "скачать и открыть MSIX-пакет"
                : "скачать и запустить установщик";
            var confirm = MessageBox.Show(
                $"Сейчас будет: {action} «{_pendingUpdateAsset.Name}».\n\n" +
                "Дальше установка/обновление продолжится в его собственном окне — " +
                "программа ничего не делает автоматически без вашего участия.\n\nПродолжить?",
                "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            if (BtnInstallUpdate != null) BtnInstallUpdate.IsEnabled = false;
            var progress = new Progress<string>(msg => TbUpdateStatus.Text = msg);
            var (ok, message) = await SystemIntegration.DownloadAndLaunchUpdateAsync(_pendingUpdateAsset, progress);
            TbUpdateStatus.Text = message;
            if (BtnInstallUpdate != null)
            {
                BtnInstallUpdate.IsEnabled = true;
                if (ok) BtnInstallUpdate.Visibility = Visibility.Collapsed;
            }
        }

        private void OnOpenReleases(object s, RoutedEventArgs e)
            => SystemIntegration.OpenGitHubReleases();
        private void OnOpenGitHub(object s, RoutedEventArgs e)
            => SystemIntegration.OpenGitHub();

        // ─── OK / CANCEL ─────────────────────────────────────────────────────────

        private void OnApply(object s, RoutedEventArgs e)
        {
            try
            {
                Collect();
                if (!SystemIntegration.SetAutostart(ChkAutorun?.IsChecked == true, out string autostartErr))
                {
                    MessageBox.Show($"Автозагрузка: {autostartErr}", "PhotoFrame",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                if (_w.RegisterAsScreensaver)
                    SystemIntegration.SetScreensaverDelay(_w.ScreensaverDelayMinutes);
                Result = _w;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения:\n{ex.Message}", "PhotoFrame",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;

        // ─── COLLECT ─────────────────────────────────────────────────────────────

        private void Collect()
        {
            _w.SlideshowIntervalSeconds = (int)Math.Round(SldInterval?.Value ?? 5);
            _w.AutoStart                = ChkAutoStart?.IsChecked == true;
            _w.LoopSlideshow            = ChkLoop?.IsChecked      == true;
            _w.IncludeSubdirectories    = ChkRecursive?.IsChecked == true;
            _w.RestoreLastSessionState  = ChkRestoreSession?.IsChecked == true;
            if (SelectedTag<CounterFormat>(CmbCounterFormat, out var cf)) _w.CounterDisplayFormat = cf;

            _w.SelectedPaths         = _paths.ToList();
            // build 53: сохраняем только теги для путей, что реально ещё в
            // списке — на случай, если путь был удалён через OnRemovePath.
            _w.RemovableSourcePaths  = _removablePaths.Intersect(_paths, StringComparer.OrdinalIgnoreCase).ToList();
            _w.WatchRemovableMedia   = ChkWatchRemovable?.IsChecked   == true;
            _w.SuggestRemovableMedia = ChkSuggestRemovable?.IsChecked == true;

            _w.ShowDirectoryOverlay     = ChkDir?.IsChecked     == true;
            _w.ShowDateOverlay          = ChkDate?.IsChecked    == true;
            _w.ShowLocationOverlay      = ChkLoc?.IsChecked     == true;
            _w.GpsReverseGeocodeEnabled = ChkGeocode?.IsChecked == true;
            _w.OverlayFontSize          = Math.Round(SldFont?.Value ?? 15, 1);

            if (SelectedTag<PlayMode>(CmbPlayMode, out var pm))         _w.PlayMode = pm;
            if (SelectedTag<TransitionType>(CmbTransition, out var tt)) _w.TransitionType = tt;
            _w.TransitionDurationSeconds = Math.Round(SldDuration?.Value ?? 0.75, 2);

            if (SelectedTag<AppTheme>(CmbTheme, out var th)) _w.Theme = th;
            _w.EnableMicaEffect             = ChkMica?.IsChecked     == true;
            _w.LiveTilesEnabled             = ChkLiveTiles?.IsChecked == true;
            _w.LiveTilesLargeEnabled        = ChkLiveTilesLarge?.IsChecked == true;
            _w.LiveTileCycleIntervalSeconds = (int)(SldTileInterval?.Value ?? 0);
            if (SelectedTag<TilePhotoDistance>(CmbTileDistance, out var tpd)) _w.TilePhotoDistance = tpd;
            if (SelectedTag<UiMode>(CmbUiMode, out var um)) _w.UiMode = um;

            _w.MinimizeToTray          = ChkMinToTray?.IsChecked == true;
            _w.RegisterAsScreensaver   = ChkScrReg?.IsChecked    == true;
            _w.ScreensaverDelayMinutes = (int)(SldScrDelay?.Value ?? 5);

            if (SelectedTag<AutoOffMode>(CmbAutoOffMode, out var aom)) _w.AutoOffMode = aom;
            _w.AutoOffFromMinutes      = (int)(SldAutoOffFrom?.Value ?? 23*60);
            _w.AutoOffToMinutes        = (int)(SldAutoOffTo?.Value   ?? 7*60);
            _w.AutoOffUseManualCoords  = ChkAutoOffManualCoords?.IsChecked == true;
            _w.AutoOffLatitude  = double.TryParse(TbAutoOffLat?.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lat) ? lat : null;
            _w.AutoOffLongitude = double.TryParse(TbAutoOffLon?.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lon) ? lon : null;

            _w.UpdateMirrorUrl = string.IsNullOrWhiteSpace(TbUpdateMirror?.Text)
                ? null : TbUpdateMirror.Text.Trim();
            _w.AutoCheckUpdatesEnabled = ChkAutoCheckUpdates?.IsChecked == true;
            _w.UpdateCheckPeriodDays   = (int)(SldUpdatePeriod?.Value ?? 3);

            _w.PreventSleep            = ChkNoSleep?.IsChecked == true;
            _w.MonitorOffAfterMinutes  = (int)(SldMonOff?.Value ?? 0);
            _w.SleepAfterMinutes       = (int)(SldSleep?.Value  ?? 0);
        }

        // ─── HELPERS ─────────────────────────────────────────────────────────────

        private static void FillCombo<T>(ComboBox? cmb,
            System.Windows.Data.IValueConverter conv) where T : struct, Enum
        {
            if (cmb == null) return;
            cmb.Items.Clear();
            foreach (T val in Enum.GetValues(typeof(T)))
                cmb.Items.Add(new ComboBoxItem
                {
                    Content = conv.Convert(val, typeof(string), null,
                        System.Globalization.CultureInfo.InvariantCulture) as string
                              ?? val.ToString(),
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
            if (cmb?.SelectedItem is ComboBoxItem ci && ci.Tag is T v)
            { val = v; return true; }
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
