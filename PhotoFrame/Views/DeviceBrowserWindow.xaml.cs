// Views/DeviceBrowserWindow.xaml.cs — v1.0 (build 52)
//
// Показывает дерево подключённых USB-устройств (Lumia/Android/iOS) через
// DeviceBrowserService (Shell.Application COM). Пользователь выбирает
// папку — импорт копирует фото в локальный кеш, после чего этот путь
// можно добавить в источники PhotoFrame как обычную папку.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PhotoFrame.Services;

namespace PhotoFrame.Views
{
    public partial class DeviceBrowserWindow : Window
    {
        /// <summary>Локальный путь импортированных фото — устанавливается при успешном импорте.</summary>
        public string? ImportedPath { get; private set; }

        private DeviceNode? _selectedFolder;
        private CancellationTokenSource? _importCts;

        public DeviceBrowserWindow()
        {
            InitializeComponent();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Helpers.WindowHelper.SetTitleBarDarkMode(this, App.CurrentTheme == Models.AppTheme.Dark);
            await LoadDevicesAsync();
        }

        private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadDevicesAsync();

        private async Task LoadDevicesAsync()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            TbNoDevices.Visibility  = Visibility.Collapsed;
            DeviceTree.Items.Clear();
            BtnImport.IsEnabled = false;
            _selectedFolder = null;

            var devices = await Task.Run(() => DeviceBrowserService.GetConnectedDevices());

            LoadingPanel.Visibility = Visibility.Collapsed;

            if (devices.Count == 0)
            {
                TbNoDevices.Visibility = Visibility.Visible;
                return;
            }

            foreach (var device in devices)
            {
                var kind = DeviceBrowserService.GuessKind(device.Name);
                string icon = kind switch
                {
                    DeviceKind.WindowsMobile => "\uE8EA", // Phone
                    DeviceKind.Android       => "\uE8EA",
                    DeviceKind.Apple         => "\uE8EA",
                    _                        => "\uE88E", // USB generic
                };

                var item = new TreeViewItem
                {
                    Header = MakeHeader(icon, device.Name),
                    Tag    = device,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
                };
                // Placeholder — раскрывается лениво при первом Expand
                item.Items.Add(new TreeViewItem { Header = "Загрузка…" });
                item.Expanded += OnDeviceExpanded;
                DeviceTree.Items.Add(item);
            }
        }

        private async void OnDeviceExpanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem item || item.Tag is not DeviceNode device) return;
            if (item.Items.Count != 1 || item.Items[0] is not TreeViewItem ph
                || ph.Header?.ToString() != "Загрузка…") return; // уже раскрыто

            item.Items.Clear();
            var tree = await Task.Run(() => DeviceBrowserService.GetDeviceTree(device, maxDepth: 3));
            if (tree == null || tree.Children.Count == 0)
            {
                item.Items.Add(new TreeViewItem { Header = "(папки не найдены)", IsEnabled = false });
                return;
            }

            foreach (var child in tree.Children)
                item.Items.Add(BuildTreeItem(child));
        }

        private TreeViewItem BuildTreeItem(DeviceNode node)
        {
            var item = new TreeViewItem
            {
                Header = MakeHeader("\uE8B7", node.Name), // Folder icon
                Tag    = node
            };
            if (node.Children.Count > 0)
                foreach (var child in node.Children)
                    item.Items.Add(BuildTreeItem(child));
            else
                item.Items.Add(new TreeViewItem { Header = "" }); // разрешает раскрытие ленивого дерева
            return item;
        }

        private static StackPanel MakeHeader(string glyph, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        private async void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not TreeViewItem item || item.Tag is not DeviceNode node)
            { BtnImport.IsEnabled = false; TbSelectedInfo.Text = ""; return; }

            _selectedFolder = node;
            BtnImport.IsEnabled = true;
            TbSelectedInfo.Text = $"Выбрано: {node.Name}  (подсчёт фото…)";

            int count = await Task.Run(() => DeviceBrowserService.EstimatePhotoCount(node));
            if (!ReferenceEquals(_selectedFolder, node)) return; // выбор сменился пока считали
            TbSelectedInfo.Text = count >= 0
                ? $"Выбрано: {node.Name}  (найдено фото: {count})"
                : $"Выбрано: {node.Name}";
        }

        private async void OnImport(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder == null) return;

            BtnImport.IsEnabled       = false;
            ImportProgress.Visibility = Visibility.Visible;
            _importCts = new CancellationTokenSource();

            var progress = new Progress<string>(msg => TbImportStatus.Text = msg);

            try
            {
                string? path = await DeviceBrowserService.ImportPhotosAsync(
                    _selectedFolder, progress, _importCts.Token);

                if (path == null)
                {
                    MessageBox.Show("Не удалось импортировать фото с устройства.",
                        "Импорт", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ImportedPath = path;
                DialogResult = true;
            }
            catch (OperationCanceledException) { /* отменено пользователем */ }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка импорта:\n{ex.Message}",
                    "Импорт", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ImportProgress.Visibility = Visibility.Collapsed;
                BtnImport.IsEnabled = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _importCts?.Cancel();
            DialogResult = false;
        }
    }
}
