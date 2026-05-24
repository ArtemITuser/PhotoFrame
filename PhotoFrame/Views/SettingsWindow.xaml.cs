// Views/SettingsWindow.xaml.cs — v4.0 (build 43)
//
// UiMode live preview: CmbUiMode.SelectionChanged → App.ChangeUiMode().
// DynamicResource в XAML (AccentButton, SecondaryButton, NavItem, CheckBox, SectionHeader)
// реагирует мгновенно — никакого обхода дерева не нужно.
// Toolbar buttons и caption buttons переключает MainWindow.ApplyUiModeStyles().
//
// LiveTilesLargeEnabled + LiveTileCycleIntervalSeconds добавлены.

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
        // Remember UiMode at open so we can revert on Cancel
        private readonly UiMode _uiModeAtOpen;

        private static readonly JsonSerializerOptions _jo = new()
        { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        public SettingsWindow(AppSettings current)
        {
            InitializeComponent();
            _w           = Clone(current);
            Result       = current;
            _uiModeAtOpen = App.CurrentUiMode;
        }

        // ─── LOADED ──────────────────────────────────────────────────────────────

        private void OnLoaded(object s, RoutedEventArgs e)
        {
            Helpers.WindowHelper.SetTitleBarDarkMode(this, App.CurrentTheme == AppTheme.Dark);
            try { LoadGeneral(); }    catch { }
            try { LoadSources(); }    catch { }
            try { LoadOverlays(); }   catch { }
            try { LoadPlayback(); }   catch { }
            try { LoadAppearance(); } catch { }
            try { LoadSystem(); }     catch { }
            try { LoadPower(); }      catch { }

            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            if (TbFooterVer != null && ver != null)
                TbFooterVer.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";
            if (TbStorage != null)
                TbStorage.Text = $"Настройки: {SettingsService.StoragePath}";
        }

        private void LoadGeneral()
        {
            SldInterval.Value      = _w.SlideshowIntervalSeconds;
            ChkAutoStart.IsChecked = _w.AutoStart;
            ChkLoop.IsChecked      = _w.LoopSlideshow;
            ChkRecursive.IsChecked = _w.IncludeSubdirectories;
            FillCombo<CounterFormat>(CmbCounterFormat, new CounterFormatToStringConverter());
            SelectTag(CmbCounterFormat, _w.CounterDisplayFormat);
        }

        private void LoadSources()
        {
            foreach (var p in _w.SelectedPaths) _paths.Add(p);
            PathList.ItemsSource          = _paths;
            ChkWatchRemovable.IsChecked   = _w.WatchRemovableMedia;
            ChkSuggestRemovable.IsChecked = _w.SuggestRemovableMedia;
            FillDriveButtons();
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
            ChkMica.IsChecked      = _w.EnableMicaEffect;
            ChkLiveTiles.IsChecked = _w.LiveTilesEnabled;

            if (FindName("ChkLiveTilesLarge") is CheckBox chkL)
                chkL.IsChecked = _w.LiveTilesLargeEnabled;
            if (FindName("SldTileCycle") is Slider sldT)
                sldT.Value = _w.LiveTileCycleIntervalSeconds;

            FillCombo<UiMode>(CmbUiMode, new UiModeToStringConverter());
            SelectTag(CmbUiMode, _w.UiMode);

            // Wire SelectionChanged AFTER setting initial value
            CmbUiMode.SelectionChanged += OnUiModeComboChanged;

            if (TbEffectStatus != null)
                TbEffectStatus.Text = _w.EnableMicaEffect
                    ? "Включено. Win11 = Mica, Win10 = Acrylic blur."
                    : "Выключено — сплошной фон.";
        }

        private void LoadSystem()
        {
            ChkAutorun.IsChecked   = SystemIntegration.IsAutostartEnabled();
            ChkMinToTray.IsChecked = _w.MinimizeToTray;
            bool scr               = SystemIntegration.IsScreensaverRegistered();
            ChkScrReg.IsChecked    = scr;
            if (TbScrStatus != null) TbScrStatus.Text = scr ? "✔ Зарегистрирован" : "Не зарегистрирован";
            SldScrDelay.Value = _w.ScreensaverDelayMinutes;
        }

        private void LoadPower()
        {
            ChkNoSleep.IsChecked = _w.PreventSleep;
            SldMonOff.Value      = _w.MonitorOffAfterMinutes;
            SldSleep.Value       = _w.SleepAfterMinutes;
        }

        // ─── UiMode live preview ──────────────────────────────────────────────────
        // Вызывает App.ChangeUiMode() → добавляет/убирает AeroTheme.xaml из словарей.
        // DynamicResource во всех окнах реагирует немедленно — никакого обхода дерева.

        private void OnUiModeComboChanged(object s, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (SelectedTag<UiMode>(CmbUiMode, out var mode))
                App.ChangeUiMode(mode);   // fires UiModeChanged → MainWindow.ApplyUiModeStyles
        }

        // ─── NAVIGATION ──────────────────────────────────────────────────────────

        private void OnNavChanged(object s, SelectionChangedEventArgs e)
        {
            if (NavList?.SelectedItem is not ListBoxItem item) return;
            foreach (var p in new StackPanel?[]
                { PGeneral, PSources, POverlays, PPlayback,
                  PAppearance, PSystem, PPower, PAbout })
                if (p != null) p.Visibility = Visibility.Collapsed;

            string tag = item.Tag as string ?? "";
            StackPanel? target = tag switch
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
                    double gb   = d.TotalSize / (1024.0 * 1024 * 1024);
                    string root = d.RootDirectory.FullName;
                    // Style="SecondaryButton" → resolves via DynamicResource automatically
                    var btn = new Button
                    {
                        Content    = $"{d.Name} ({gb:F0} ГБ)",
                        Margin     = new Thickness(0,0,6,6),
                        Padding    = new Thickness(12,5,12,5),
                        Height     = 32, FontSize = 12,
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
#pragma warning disable CA1416
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description="Выберите папку с фотографиями",
                    ShowNewFolderButton=false, UseDescriptionForTitle=true
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                { AddPath(dlg.SelectedPath); _ = LoadPreviewAsync(); }
#pragma warning restore CA1416
            }
            catch (Exception ex)
            { MessageBox.Show($"Ошибка: {ex.Message}", "PhotoFrame",
                MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void OnAddDrive(object s, RoutedEventArgs e)
        {
            try
            {
                var drives = FileScanner.GetAvailableDrives();
                if (drives.Count == 0) return;

                Brush bg = TryFindResource("DialogBgBrush") as Brush
                        ?? new SolidColorBrush(Colors.WhiteSmoke);
                Brush fg = TryFindResource("TextPrimary") as Brush ?? Brushes.Black;

                var dlg = new Window
                {
                    Title="Выбор диска", Width=340, Height=165,
                    ResizeMode=ResizeMode.NoResize,
                    WindowStartupLocation=WindowStartupLocation.CenterOwner,
                    Owner=this, Background=bg
                };
                var cmb = new ComboBox
                {
                    Margin=new Thickness(16,8,16,8), FontSize=13,
                    FontFamily=new FontFamily("Segoe UI"),
                    Background=TryFindResource("InputBgBrush") as Brush,
                    Foreground=TryFindResource("InputFgBrush") as Brush
                };
                foreach (var d in drives)
                    cmb.Items.Add($"{d.Name}  [{d.VolumeLabel}]  " +
                                  $"({d.TotalSize/(1024L*1024*1024)} ГБ)");
                cmb.SelectedIndex = 0;

                var ok = new Button
                {
                    Content="Добавить", MinWidth=90, Height=32,
                    Margin=new Thickness(0,0,8,0),
                    Cursor=System.Windows.Input.Cursors.Hand,
                    Style=TryFindResource("AccentButton") as Style
                };
                var cn = new Button
                {
                    Content="Отмена", MinWidth=80, Height=32,
                    Cursor=System.Windows.Input.Cursors.Hand,
                    Style=TryFindResource("SecondaryButton") as Style
                };
                ok.Click += (_,__) => { dlg.DialogResult=true;  dlg.Close(); };
                cn.Click += (_,__) => { dlg.DialogResult=false; dlg.Close(); };

                var row = new StackPanel
                {
                    Orientation=Orientation.Horizontal,
                    HorizontalAlignment=HorizontalAlignment.Right,
                    Margin=new Thickness(16,4,16,8)
                };
                row.Children.Add(ok); row.Children.Add(cn);

                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                { Text="Выберите диск:", Margin=new Thickness(16,12,16,4),
                  FontSize=13, Foreground=fg });
                sp.Children.Add(cmb); sp.Children.Add(row);
                dlg.Content = sp;

                if (dlg.ShowDialog()==true && cmb.SelectedIndex>=0)
                { AddPath(drives[cmb.SelectedIndex].RootDirectory.FullName); _ = LoadPreviewAsync(); }
            }
            catch (Exception ex)
            { MessageBox.Show($"Ошибка: {ex.Message}", "PhotoFrame",
                MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void OnRemovePath(object s, RoutedEventArgs e)
        { if (PathList?.SelectedItem is string p) { _paths.Remove(p); _ = LoadPreviewAsync(); } }

        private void AddPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
                if (!_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) _paths.Add(path);
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
            if (_lastScan.Photos.Count == 0) { ShowPreviewMsg("Фотографии не найдены"); return; }

            var wrap = new WrapPanel { Orientation=Orientation.Horizontal };
            Brush bg = TryFindResource("Surface2Brush") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(0xEB,0xEB,0xEB));
            foreach (var photo in _lastScan.Photos.Take(12))
            {
                var thumb = await Task.Run(() => LoadThumbSafe(photo.FilePath));
                if (thumb == null) continue;
                var img = new Image { Source=thumb, Stretch=Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                wrap.Children.Add(new Border
                {
                    Width=86, Height=86, Margin=new Thickness(2),
                    CornerRadius=new CornerRadius(3), ClipToBounds=true,
                    ToolTip=photo.FilePath, Background=bg, Child=img
                });
            }
            PreviewPanel.Children.Add(wrap);
            if (_lastScan.Photos.Count > 12)
            {
                Brush sec = TryFindResource("TextSecondary") as Brush ?? Brushes.Gray;
                PreviewPanel.Children.Add(new TextBlock
                { Text=$"… ещё {_lastScan.Photos.Count-12} фото",
                  FontSize=11, Foreground=sec, Margin=new Thickness(0,4,0,0) });
            }
        }

        private void ShowPreviewMsg(string msg)
        {
            PreviewPanel?.Children.Clear();
            Brush c = TryFindResource("TextSecondary") as Brush ?? Brushes.Gray;
            PreviewPanel?.Children.Add(new TextBlock
            { Text=msg, FontSize=12, Foreground=c, Margin=new Thickness(0,8,0,0) });
        }

        private static BitmapImage? LoadThumbSafe(string path)
        {
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource=new Uri(path, UriKind.Absolute);
                b.DecodePixelWidth=86;
                b.CacheOption=BitmapCacheOption.OnLoad;
                b.CreateOptions=BitmapCreateOptions.IgnoreColorProfile;
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
            { MessageBox.Show("Нет данных. Перейдите в «Источники» и дождитесь сканирования.",
                "Выгрузка", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter     = csv ? "CSV|*.csv|Все файлы|*.*" : "TXT|*.txt|Все файлы|*.*",
                    FileName   = csv ? "photos.csv" : "photos.txt",
                    DefaultExt = csv ? ".csv" : ".txt"
                };
                if (dlg.ShowDialog() != true) return;
                FileScanner.ExportPhotoList(_lastScan.Photos, dlg.FileName, !csv);
                MessageBox.Show($"Сохранено {_lastScan.Photos.Count} фото.",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            { MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ─── SLIDERS ─────────────────────────────────────────────────────────────

        private void OnIntervalChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TbIntervalVal==null) return;
            int v = (int)Math.Round(e.NewValue);
            TbIntervalVal.Text = v>=60 ? $"{v/60}м {v%60:D2}с" : $"{v} с";
        }
        private void OnFontChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbFontVal!=null) TbFontVal.Text=$"{Math.Round(e.NewValue,1)} pt"; }
        private void OnDurChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbDurVal!=null) TbDurVal.Text=$"{e.NewValue:F2} с"; }
        private void OnScrDelayChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbScrDelayVal!=null) TbScrDelayVal.Text=$"{(int)e.NewValue} мин"; }
        private void OnMonOffChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbMonOffVal!=null) TbMonOffVal.Text=(int)e.NewValue==0?"Не управлять":$"{(int)e.NewValue} мин"; }
        private void OnSleepChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        { if (TbSleepVal!=null) TbSleepVal.Text=(int)e.NewValue==0?"Не управлять":$"{(int)e.NewValue} мин"; }
        private void OnTileCycleChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FindName("TbTileCycleVal") is TextBlock tb)
                tb.Text = (int)e.NewValue==0 ? "Как слайдшоу" : $"{(int)e.NewValue} с";
        }

        // ─── SCREENSAVER ─────────────────────────────────────────────────────────

        private void OnScrRegChanged(object s, RoutedEventArgs e)
        {
            try
            {
                if (ChkScrReg.IsChecked==true)
                {
                    bool ok = SystemIntegration.RegisterScreensaver(out string err);
                    if (!ok)
                    { MessageBox.Show($"Не удалось:\n{err}\n\nПодтвердите UAC-запрос.",
                        "Скринсейвер", MessageBoxButton.OK, MessageBoxImage.Warning);
                      ChkScrReg.IsChecked=false; }
                    if (TbScrStatus!=null) TbScrStatus.Text = ok ? "✔ Зарегистрирован" : "Ошибка";
                }
                else
                {
                    SystemIntegration.UnregisterScreensaver();
                    if (TbScrStatus!=null) TbScrStatus.Text="Не зарегистрирован";
                }
            }
            catch (Exception ex)
            { MessageBox.Show($"Ошибка: {ex.Message}", "PhotoFrame",
                MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void OnApplyPower(object s, RoutedEventArgs e)
        {
            bool ok = SystemIntegration.SetPowerTimeouts(
                (int)(SldMonOff?.Value??0)*60, (int)(SldSleep?.Value??0)*60);
            MessageBox.Show(ok?"Применено.":"Не удалось применить.", "Питание",
                MessageBoxButton.OK, ok?MessageBoxImage.Information:MessageBoxImage.Warning);
        }

        // ─── ABOUT ───────────────────────────────────────────────────────────────

        private void LoadAbout()
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            if (TbAboutVersion!=null && ver!=null)
                TbAboutVersion.Text =
                    $"PhotoFrame  v{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";
            if (TbClickOnceInfo!=null)
                TbClickOnceInfo.Text = SystemIntegration.GetClickOnceInfo();
            if (About3DIcon!=null)
            {
                try
                {
                    About3DIcon.Source =
                        new BitmapImage(new Uri("pack://application:,,,/Resources/Icons/AppIcon3D.png"));
                    About3DIcon.Visibility=Visibility.Visible;
                }
                catch { About3DIcon.Visibility=Visibility.Collapsed; }
            }
        }

        private async void OnCheckUpdates(object s, RoutedEventArgs e)
        {
            if (TbUpdateStatus==null) return;
            TbUpdateStatus.Text="Проверяем…";
            try
            {
                var cur = Assembly.GetExecutingAssembly().GetName().Version
                       ?? new Version(1,2,0,0);
                var (tag, isNewer) = await SystemIntegration.CheckGitHubUpdateAsync(cur);
                if (tag==null) { TbUpdateStatus.Text="Не удалось связаться с GitHub."; return; }
                string cs=$"v{cur.Major}.{cur.Minor}.{cur.Build}.{cur.Revision}";
                TbUpdateStatus.Text = isNewer
                    ? $"⬆ Доступна версия: {tag}  (у вас: {cs})"
                    : $"✔ Актуальная версия ({cs}).";
            }
            catch (Exception ex) { TbUpdateStatus.Text=$"Ошибка: {ex.Message}"; }
        }

        private void OnOpenReleases(object s, RoutedEventArgs e) => SystemIntegration.OpenGitHubReleases();
        private void OnOpenGitHub(object s, RoutedEventArgs e)   => SystemIntegration.OpenGitHub();

        // ─── OK / CANCEL ─────────────────────────────────────────────────────────

        private void OnApply(object s, RoutedEventArgs e)
        {
            try
            {
                Collect();
                SystemIntegration.SetAutostart(ChkAutorun?.IsChecked==true);
                if (_w.RegisterAsScreensaver)
                    SystemIntegration.SetScreensaverDelay(_w.ScreensaverDelayMinutes);
                Result = _w;
                DialogResult = true;
            }
            catch (Exception ex)
            { MessageBox.Show($"Ошибка сохранения:\n{ex.Message}", "PhotoFrame",
                MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OnCancel(object s, RoutedEventArgs e)
        {
            // Revert live UiMode preview if user cancels
            if (App.CurrentUiMode != _uiModeAtOpen)
                App.ChangeUiMode(_uiModeAtOpen);
            DialogResult = false;
        }

        // ─── COLLECT ─────────────────────────────────────────────────────────────

        private void Collect()
        {
            _w.SlideshowIntervalSeconds = (int)Math.Round(SldInterval?.Value??5);
            _w.AutoStart                = ChkAutoStart?.IsChecked == true;
            _w.LoopSlideshow            = ChkLoop?.IsChecked      == true;
            _w.IncludeSubdirectories    = ChkRecursive?.IsChecked == true;
            if (SelectedTag<CounterFormat>(CmbCounterFormat, out var cf)) _w.CounterDisplayFormat=cf;

            _w.SelectedPaths         = _paths.ToList();
            _w.WatchRemovableMedia   = ChkWatchRemovable?.IsChecked   == true;
            _w.SuggestRemovableMedia = ChkSuggestRemovable?.IsChecked == true;

            _w.ShowDirectoryOverlay = ChkDir?.IsChecked  == true;
            _w.ShowDateOverlay      = ChkDate?.IsChecked == true;
            _w.ShowLocationOverlay  = ChkLoc?.IsChecked  == true;
            _w.OverlayFontSize      = Math.Round(SldFont?.Value??15, 1);

            if (SelectedTag<PlayMode>(CmbPlayMode,         out var pm)) _w.PlayMode=pm;
            if (SelectedTag<TransitionType>(CmbTransition, out var tt)) _w.TransitionType=tt;
            _w.TransitionDurationSeconds = Math.Round(SldDuration?.Value??0.75, 2);

            if (SelectedTag<AppTheme>(CmbTheme, out var th)) _w.Theme=th;
            _w.EnableMicaEffect = ChkMica?.IsChecked      == true;
            _w.LiveTilesEnabled = ChkLiveTiles?.IsChecked == true;

            if (FindName("ChkLiveTilesLarge") is CheckBox chkL)
                _w.LiveTilesLargeEnabled = chkL.IsChecked==true;
            if (FindName("SldTileCycle") is Slider sldT)
                _w.LiveTileCycleIntervalSeconds = (int)(sldT.Value);

            if (SelectedTag<UiMode>(CmbUiMode, out var um)) _w.UiMode=um;

            _w.MinimizeToTray          = ChkMinToTray?.IsChecked == true;
            _w.RegisterAsScreensaver   = ChkScrReg?.IsChecked    == true;
            _w.ScreensaverDelayMinutes = (int)(SldScrDelay?.Value??5);
            _w.AutostartEnabled        = ChkAutorun?.IsChecked   == true;

            _w.PreventSleep           = ChkNoSleep?.IsChecked == true;
            _w.MonitorOffAfterMinutes = (int)(SldMonOff?.Value??0);
            _w.SleepAfterMinutes      = (int)(SldSleep?.Value??0);
        }

        // ─── HELPERS ─────────────────────────────────────────────────────────────

        private static void FillCombo<T>(ComboBox? cmb,
            System.Windows.Data.IValueConverter conv) where T : struct, Enum
        {
            if (cmb==null) return;
            cmb.Items.Clear();
            foreach (T val in Enum.GetValues(typeof(T)))
                cmb.Items.Add(new ComboBoxItem
                {
                    Content = conv.Convert(val, typeof(string), null,
                        System.Globalization.CultureInfo.InvariantCulture)
                              as string ?? val.ToString(),
                    Tag = val
                });
        }

        private static void SelectTag(ComboBox? cmb, object tag)
        {
            if (cmb==null) return;
            foreach (ComboBoxItem i in cmb.Items)
                if (Equals(i.Tag, tag)) { cmb.SelectedItem=i; return; }
            if (cmb.Items.Count>0) cmb.SelectedIndex=0;
        }

        private static bool SelectedTag<T>(ComboBox? cmb, out T val) where T : struct
        {
            if (cmb?.SelectedItem is ComboBoxItem ci && ci.Tag is T v)
            { val=v; return true; }
            val=default; return false;
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
