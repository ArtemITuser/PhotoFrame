// Views/UpdateWindow.xaml.cs — v1.0 (build 45)
using System;
using System.Reflection;
using System.Windows;
using PhotoFrame.Services;

namespace PhotoFrame.Views
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _info;

        public UpdateWindow(UpdateInfo info)
        {
            InitializeComponent();
            _info = info;
            var cur = Assembly.GetExecutingAssembly().GetName().Version;
            string curStr = cur != null
                ? $"v{cur.Major}.{cur.Minor}.{cur.Build}.{cur.Revision}"
                : "?";
            TbDesc.Text =
                $"Ваша версия: {curStr}\n" +
                $"Доступна: {info.TagName}\n\n" +
                "Нажмите «Установить» — программа скачает установщик и перезапустится.";
            Helpers.WindowHelper.SetTitleBarDarkMode(this,
                App.CurrentTheme == Models.AppTheme.Dark);
        }

        private void OnLater(object s, RoutedEventArgs e) => Close();

        private async void OnUpdate(object s, RoutedEventArgs e)
        {
            BtnUpdate.IsEnabled = BtnLater.IsEnabled = false;
            PbProgress.Visibility = Visibility.Visible;
            TbProgress.Visibility = Visibility.Visible;
            TbProgress.Text       = "Загрузка…";

            var progress = new Progress<double>(p =>
            {
                PbProgress.Value = p * 100;
                TbProgress.Text  = $"Загрузка: {p:P0}";
            });

            try
            {
                await UpdateService.DownloadAndInstallAsync(_info, progress);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки:\n{ex.Message}",
                    "Обновление", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnUpdate.IsEnabled = BtnLater.IsEnabled = true;
                PbProgress.Visibility = TbProgress.Visibility = Visibility.Collapsed;
            }
        }
    }
}
