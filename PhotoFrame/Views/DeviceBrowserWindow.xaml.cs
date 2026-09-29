// Views/DeviceBrowserWindow.xaml.cs — v1.2 (build 57)
//
// build 57: путь назначения импорта теперь явный и редактируемый — по
// умолчанию Pictures\{устройство} (см. DeviceBrowserService.
// GetDefaultImportDestination), а не скрытая %LocalAppData%; добавлена
// кнопка копирования пути в буфер обмена (только иконка, MDL2 &#xE8C8;).
//
// Показывает дерево подключённых USB-устройств (Lumia/Android/iOS) через
// DeviceBrowserService (Shell.Application COM). Пользователь выбирает
// папку — импорт копирует фото в локальный кеш, после чего этот путь
// можно добавить в источники PhotoFrame как обычную папку.
//
// build 53: КАЖДЫЙ узел дерева (не только устройство верхнего уровня)
// раскрывается лениво через один и тот же OnTreeItemExpanded, запрашивая
// у DeviceBrowserService.GetChildren() только ОДИН уровень за раз — так
// же, как раньше раскрывался только первый уровень. Глубина вложенности
// больше не ограничена константой (было maxDepth: 3): пользователь может
// зайти настолько глубоко, насколько устроена файловая структура
// конкретного телефона, как в обычном проводнике.

using System;
using System.IO;
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

        private const string LoadingLabel = "Загрузка…";

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
                    DeviceKind.Camera        => "\uE722", // Camera (build 56)
                    _                        => "\uE88E", // USB generic
                };

                DeviceTree.Items.Add(BuildLazyTreeItem(device, icon));
            }
        }

        /// <summary>
        /// Создаёт TreeViewItem для узла (устройство ИЛИ вложенная папка) с
        /// плейсхолдером "Загрузка…" и подпиской на ленивое раскрытие.
        /// Используется единообразно на ЛЮБОЙ глубине — в отличие от build
        /// 52, где ленивое раскрытие работало только для узлов верхнего
        /// уровня, а всё дерево ниже строилось заранее и обрывалось на
        /// фиксированной глубине.
        /// </summary>
        private TreeViewItem BuildLazyTreeItem(DeviceNode node, string icon)
        {
            var item = new TreeViewItem
            {
                Header     = MakeHeader(icon, node.Name),
                Tag        = node,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
            };
            item.Items.Add(new TreeViewItem { Header = LoadingLabel });
            item.Expanded += OnTreeItemExpanded;
            return item;
        }

        private async void OnTreeItemExpanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem item || item.Tag is not DeviceNode node) return;
            if (item.Items.Count != 1 || item.Items[0] is not TreeViewItem ph
                || ph.Header?.ToString() != LoadingLabel) return; // уже раскрыто — второй Expanded не перезагружает

            item.Items.Clear();
            var children = await Task.Run(() => DeviceBrowserService.GetChildren(node));

            if (children.Count == 0)
            {
                item.Items.Add(new TreeViewItem { Header = "(папки не найдены)", IsEnabled = false });
                return;
            }

            foreach (var child in children)
                item.Items.Add(BuildLazyTreeItem(child, "\uE8B7")); // Folder icon
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
            {
                BtnImport.IsEnabled = false; TbSelectedInfo.Text = "";
                ImportDestPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _selectedFolder = node;
            BtnImport.IsEnabled = true;
            TbSelectedInfo.Text = $"Выбрано: {node.Name}  (подсчёт фото…)";

            // build 57: путь назначения по умолчанию — Pictures\{устройство},
            // редактируемый до нажатия "Импортировать".
            ImportDestPanel.Visibility = Visibility.Visible;
            TbImportDest.Text = DeviceBrowserService.GetDefaultImportDestination(node.EffectiveRoot);

            int count = await Task.Run(() => DeviceBrowserService.EstimatePhotoCount(node));
            if (!ReferenceEquals(_selectedFolder, node)) return; // выбор сменился пока считали
            TbSelectedInfo.Text = count >= 0
                ? $"Выбрано: {node.Name}  (найдено фото: {count})"
                : $"Выбрано: {node.Name}";
        }

        /// <summary>
        /// Открывает стандартный системный выбор папки. Путь можно также
        /// ввести вручную — кнопка нужна для выбора каталога без копирования
        /// длинного пути с телефона или камеры.
        /// </summary>
        private void OnBrowseImportDest(object sender, RoutedEventArgs e)
        {
            try
            {
                string? selected = DeviceBrowserService.PickLocalFolder(
                    Directory.Exists(TbImportDest.Text)
                        ? TbImportDest.Text.Trim()
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                if (!string.IsNullOrWhiteSpace(selected))
                    TbImportDest.Text = selected;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть выбор папки:\n{ex.Message}",
                    "Импорт", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Копирует текущий путь назначения импорта в буфер обмена.</summary>
        private void OnCopyImportDest(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(TbImportDest.Text))
                    Clipboard.SetText(TbImportDest.Text.Trim());
            }
            catch { /* буфер обмена может быть занят другим процессом — не критично */ }
        }

        private async void OnImport(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder == null) return;

            string destDir = string.IsNullOrWhiteSpace(TbImportDest.Text)
                ? DeviceBrowserService.GetDefaultImportDestination(_selectedFolder.EffectiveRoot)
                : TbImportDest.Text.Trim();

            if (!Path.IsPathFullyQualified(destDir))
            {
                MessageBox.Show("Укажите полный путь к папке назначения, например C:\\Users\\Artem\\Pictures.",
                    "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Directory.CreateDirectory(destDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось создать папку назначения:\n{ex.Message}",
                    "Импорт", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BtnImport.IsEnabled       = false;
            ImportProgress.Visibility = Visibility.Visible;
            _importCts = new CancellationTokenSource();

            var progress = new Progress<string>(msg => TbImportStatus.Text = msg);

            try
            {
                string? path = await DeviceBrowserService.ImportPhotosAsync(
                    _selectedFolder, destDir, progress, _importCts.Token);

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
                _importCts?.Dispose();
                _importCts = null;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _importCts?.Cancel();
            DialogResult = false;
        }
    }
}
