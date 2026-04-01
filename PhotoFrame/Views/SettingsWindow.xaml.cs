// Views/SettingsWindow.xaml.cs — v3
// TryFindResource вместо FindResource (не бросает исключение если ресурс не найден).
// FolderBrowserDialog в try-catch.
// Нет unsafe resource-lookup в конструкторе.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        // ─── Публичный результат ──────────────────────────────────────────────────
        public AppSettings Result { get; private set; }

        // ─── Рабочая копия ────────────────────────────────────────────────────────
        private readonly AppSettings _w;
        private readonly ObservableCollection<string> _paths = new();

        private static readonly JsonSerializerOptions _jo = new()
        {
            WriteIndented = true,
            Converters    = { new JsonStringEnumConverter() }
        };

        // ─────────────────────────────────────────────────────────────────────────

        public SettingsWindow(AppSettings current)
        {
            // Никаких resource-lookups в конструкторе!
            InitializeComponent();
            _w     = Clone(current);
            Result = current;
        }

        // ─── Загрузка (безопасная — try-catch на каждую секцию) ──────────────────

        private void OnLoaded(object s, RoutedEventArgs e)
        {
            try { LoadGeneral(); }    catch { /* продолжаем */ }
            try { LoadSources(); }    catch { /* продолжаем */ }
            try { LoadOverlays(); }   catch { /* продолжаем */ }
            try { LoadPlayback(); }   catch { /* продолжаем */ }
            try { LoadAppearance(); } catch { /* продолжаем */ }
            try { LoadSystem(); }     catch { /* продолжаем */ }
            try { LoadPower(); }      catch { /* продолжаем */ }

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

        // ─── Навигация по разделам ────────────────────────────────────────────────

        private void OnNavChanged(object s, SelectionChangedEventArgs e)
        {
            if (NavList?.SelectedItem is not ListBoxItem item) return;

            // Скрываем все панели
            foreach (StackPanel p in new[] { PGeneral, PSources, POverlays,
                                              PPlayback, PAppearance, PSystem, PPower })
                if (p != null) p.Visibility = Visibility.Collapsed;

            string tag = item.Tag as string ?? "";
            var target = tag switch
            {
                "General"    => PGeneral,
                "Sources"    => PSources,
                "Overlays"   => POverlays,
                "Playback"   => PPlayback,
                "Appearance" => PAppearance,
                "System"     => PSystem,
                "Power"      => PPower,
                _            => PGeneral
            };

            if (target != null) target.Visibility = Visibility.Visible;

            // Загружаем превью при переходе в Sources
            if (tag == "Sources") _ = LoadPreviewAsync();
        }

        // ─── Управление путями ────────────────────────────────────────────────────

        private void FillDriveButtons()
        {
            if (DriveBtns == null) return;
            DriveBtns.Children.Clear();

            foreach (var d in FileScanner.GetAvailableDrives())
            {
                try
                {
                    double gb    = d.TotalSize / (1024.0 * 1024 * 1024);
                    string label = $"{d.Name} ({gb:F0} ГБ)";
                    string root  = d.RootDirectory.FullName;

                    var btn = new Button
                    {
                        Content      = label,
                        Margin       = new Thickness(0, 0, 6, 6),
                        Padding      = new Thickness(12, 5, 12, 5),
                        Height       = 30,
                        FontSize     = 12,
                        FontFamily   = new System.Windows.Media.FontFamily("Segoe UI"),
                        Cursor       = System.Windows.Input.Cursors.Hand,
                        // Используем TryFindResource — не бросает исключение
                        Style = TryFindResource("SecondaryButton") as Style
                    };
                    btn.Click += (_, __) => { AddPath(root); _ = LoadPreviewAsync(); };
                    DriveBtns.Children.Add(btn);
                }
                catch { /* пропускаем диск с ошибкой */ }
            }
        }

        private void OnAddFolder(object s, RoutedEventArgs e)
        {
            try
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description            = "Выберите папку с фотографиями",
                    ShowNewFolderButton    = false,
                    UseDescriptionForTitle = true
                };

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    AddPath(dlg.SelectedPath);
                    _ = LoadPreviewAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе папки:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnAddDrive(object s, RoutedEventArgs e)
        {
            try
            {
                var drives = FileScanner.GetAvailableDrives();
                if (drives.Count == 0)
                {
                    MessageBox.Show("Нет доступных дисков.",
                        "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Простой диалог выбора диска без resource-lookup
                var dlg = new Window
                {
                    Title                 = "Выбор диска",
                    Width                 = 340,
                    Height                = 165,
                    ResizeMode            = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner                 = this,
                    Background            = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25))
                };

                var cmb = new ComboBox
                {
                    Margin    = new Thickness(16, 8, 16, 8),
                    FontSize  = 13,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
                };
                foreach (var d in drives)
                    cmb.Items.Add(
                        $"{d.Name}  [{d.VolumeLabel}]  " +
                        $"({d.TotalSize / (1024 * 1024 * 1024L)} ГБ)");
                cmb.SelectedIndex = 0;

                var btnRow = new StackPanel
                {
                    Orientation         = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin              = new Thickness(16, 4, 16, 8)
                };
                var btnOk = new Button
                {
                    Content   = "Добавить",
                    MinWidth  = 90,
                    Height    = 30,
                    Margin    = new Thickness(0, 0, 8, 0),
                    FontSize  = 13,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var btnCn = new Button
                {
                    Content   = "Отмена",
                    MinWidth  = 80,
                    Height    = 30,
                    FontSize  = 13,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                btnOk.Click += (_, __) => { dlg.DialogResult = true;  dlg.Close(); };
                btnCn.Click += (_, __) => { dlg.DialogResult = false; dlg.Close(); };
                btnRow.Children.Add(btnOk);
                btnRow.Children.Add(btnCn);

                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text       = "Выберите диск для добавления:",
                    Margin     = new Thickness(16, 12, 16, 4),
                    FontSize   = 13,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = Brushes.White
                });
                sp.Children.Add(cmb);
                sp.Children.Add(btnRow);
                dlg.Content = sp;

                if (dlg.ShowDialog() == true && cmb.SelectedIndex >= 0)
                {
                    AddPath(drives[cmb.SelectedIndex].RootDirectory.FullName);
                    _ = LoadPreviewAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе диска:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnRemovePath(object s, RoutedEventArgs e)
        {
            if (PathList?.SelectedItem is string p)
            {
                _paths.Remove(p);
                _ = LoadPreviewAsync();
            }
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
            catch { /* некорректный путь */ }
        }

        // ─── Предпросмотр миниатюр ────────────────────────────────────────────────

        private async Task LoadPreviewAsync()
        {
            if (PreviewPanel == null) return;
            PreviewPanel.Children.Clear();

            if (_paths.Count == 0)
            {
                PreviewPanel.Children.Add(new TextBlock
                {
                    Text       = "Нет выбранных папок",
                    FontSize   = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = Brushes.Gray,
                    Margin     = new Thickness(0, 8, 0, 0)
                });
                return;
            }

            PreviewPanel.Children.Add(new TextBlock
            {
                Text       = "Загрузка превью…",
                FontSize   = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                Foreground = Brushes.Gray
            });

            // Собираем до 12 файлов асинхронно
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };

            var pathsCopy = _paths.ToList(); // копия для фонового потока
            var files = await Task.Run(() =>
            {
                var result = new List<string>();
                foreach (var root in pathsCopy)
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(
                            root, "*.*", SearchOption.AllDirectories))
                        {
                            if (exts.Contains(Path.GetExtension(f)))
                            {
                                result.Add(f);
                                if (result.Count >= 12) return result;
                            }
                        }
                    }
                    catch { /* нет доступа — пропускаем */ }
                }
                return result;
            });

            PreviewPanel.Children.Clear();

            if (files.Count == 0)
            {
                PreviewPanel.Children.Add(new TextBlock
                {
                    Text       = "Фотографии не найдены в выбранных папках",
                    FontSize   = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = Brushes.Gray,
                    Margin     = new Thickness(0, 8, 0, 0)
                });
                return;
            }

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

            foreach (var file in files)
            {
                var thumb = await Task.Run(() => LoadThumb(file));
                if (thumb == null) continue;

                var img = new Image { Source = thumb, Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                // Цвет фона для превью — используем TryFindResource
                Brush bgBrush = TryFindResource("Surface2Brush") as Brush
                             ?? new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));

                wrap.Children.Add(new Border
                {
                    Width        = 86,
                    Height       = 86,
                    Margin       = new Thickness(2),
                    CornerRadius = new CornerRadius(3),
                    ClipToBounds = true,
                    ToolTip      = file,
                    Background   = bgBrush,
                    Child        = img
                });
            }

            PreviewPanel.Children.Add(wrap);

            if (files.Count == 12)
            {
                PreviewPanel.Children.Add(new TextBlock
                {
                    Text       = $"… ещё больше фото в выбранных папках",
                    FontSize   = 11,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    Foreground = Brushes.Gray,
                    Margin     = new Thickness(0, 4, 0, 0)
                });
            }
        }

        private static BitmapImage? LoadThumb(string path)
        {
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource        = new Uri(path, UriKind.Absolute);
                b.DecodePixelWidth = 86;
                b.CacheOption      = BitmapCacheOption.OnLoad;
                b.CreateOptions    = BitmapCreateOptions.IgnoreColorProfile;
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch { return null; }
        }

        // ─── Слайдеры ─────────────────────────────────────────────────────────────

        private void OnIntervalChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TbIntervalVal == null) return;
            int v = (int)Math.Round(e.NewValue);
            TbIntervalVal.Text = v >= 60 ? $"{v / 60}м {v % 60:D2}с" : $"{v} с";
        }

        private void OnFontChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TbFontVal != null)
                TbFontVal.Text = $"{Math.Round(e.NewValue, 1)} pt";
        }

        private void OnDurChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TbDurVal != null)
                TbDurVal.Text = $"{e.NewValue:F2} с";
        }

        private void OnScrDelayChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TbScrDelayVal != null)
                TbScrDelayVal.Text = $"{(int)e.NewValue} мин";
        }

        private void OnMonOffChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TbMonOffVal == null) return;
            int v = (int)e.NewValue;
            TbMonOffVal.Text = v == 0 ? "Не управлять" : $"{v} мин";
        }

        private void OnSleepChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TbSleepVal == null) return;
            int v = (int)e.NewValue;
            TbSleepVal.Text = v == 0 ? "Не управлять" : $"{v} мин";
        }

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
                            "Попробуйте запустить приложение от имени администратора.",
                            "Скринсейвер", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ChkScrReg.IsChecked = false;
                    }
                    TbScrStatus.Text = ok ? "✔ Зарегистрирован" : "Ошибка регистрации";
                }
                else
                {
                    SystemIntegration.UnregisterScreensaver();
                    TbScrStatus.Text = "Не зарегистрирован";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка скринсейвера:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnApplyPower(object s, RoutedEventArgs e)
        {
            try
            {
                bool ok = SystemIntegration.SetPowerTimeouts(
                    (int)SldMonOff.Value * 60,
                    (int)SldSleep.Value  * 60);

                MessageBox.Show(
                    ok ? "Параметры питания применены."
                       : "Не удалось применить. Попробуйте запустить от администратора.",
                    "Электропитание", MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка питания:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ─── OK / Отмена ──────────────────────────────────────────────────────────

        private void OnApply(object s, RoutedEventArgs e)
        {
            try
            {
                Collect();

                // Автозагрузка — немедленно
                SystemIntegration.SetAutostart(ChkAutorun?.IsChecked == true);

                // Задержка скринсейвера
                if (_w.RegisterAsScreensaver)
                    SystemIntegration.SetScreensaverDelay(_w.ScreensaverDelayMinutes);

                Result       = _w;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении:\n{ex.Message}",
                    "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancel(object s, RoutedEventArgs e)
            => DialogResult = false;

        // ─── Сбор значений из контролов ──────────────────────────────────────────

        private void Collect()
        {
            _w.SlideshowIntervalSeconds = (int)Math.Round(SldInterval?.Value ?? 5);
            _w.AutoStart                = ChkAutoStart?.IsChecked == true;
            _w.LoopSlideshow            = ChkLoop?.IsChecked      == true;
            _w.IncludeSubdirectories    = ChkRecursive?.IsChecked == true;

            _w.SelectedPaths = _paths.ToList();

            _w.ShowDirectoryOverlay = ChkDir?.IsChecked  == true;
            _w.ShowDateOverlay      = ChkDate?.IsChecked == true;
            _w.ShowLocationOverlay  = ChkLoc?.IsChecked  == true;
            _w.OverlayFontSize      = Math.Round(SldFont?.Value ?? 15, 1);

            if (SelectedTag<PlayMode>(CmbPlayMode, out var pm))
                _w.PlayMode = pm;
            if (SelectedTag<TransitionType>(CmbTransition, out var tt))
                _w.TransitionType = tt;
            _w.TransitionDurationSeconds = Math.Round(SldDuration?.Value ?? 0.75, 2);

            if (SelectedTag<AppTheme>(CmbTheme, out var th))
                _w.Theme = th;
            _w.EnableMicaEffect = ChkMica?.IsChecked == true;

            _w.MinimizeToTray          = ChkMinToTray?.IsChecked == true;
            _w.RegisterAsScreensaver   = ChkScrReg?.IsChecked    == true;
            _w.ScreensaverDelayMinutes = (int)(SldScrDelay?.Value ?? 5);

            _w.PreventSleep            = ChkNoSleep?.IsChecked == true;
            _w.MonitorOffAfterMinutes  = (int)(SldMonOff?.Value ?? 0);
            _w.SleepAfterMinutes       = (int)(SldSleep?.Value  ?? 0);
        }

        // ─── Вспомогательные ──────────────────────────────────────────────────────

        private static void FillCombo<T>(
            ComboBox cmb,
            System.Windows.Data.IValueConverter conv) where T : struct, Enum
        {
            if (cmb == null) return;
            cmb.Items.Clear();
            foreach (T val in Enum.GetValues(typeof(T)))
                cmb.Items.Add(new ComboBoxItem
                {
                    Content = conv.Convert(val, typeof(string), null,
                        System.Globalization.CultureInfo.InvariantCulture) as string ?? val.ToString(),
                    Tag     = val
                });
        }

        private static void SelectTag(ComboBox? cmb, object tagValue)
        {
            if (cmb == null) return;
            foreach (ComboBoxItem item in cmb.Items)
                if (Equals(item.Tag, tagValue)) { cmb.SelectedItem = item; return; }
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private static bool SelectedTag<T>(ComboBox? cmb, out T val) where T : struct
        {
            if (cmb?.SelectedItem is ComboBoxItem ci && ci.Tag is T v)
            { val = v; return true; }
            val = default;
            return false;
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
