// App.xaml.cs — v3.7 (build 53)
//
// build 53 — COMException/UCEERR_RENDERTHREADFAILURE:
//   DispatcherUnhandledException уже перехватывал ЛЮБОЕ исключение и
//   показывал техническое диалоговое окно (это и есть окно из отчёта:
//   "Ошибка: UCEERR_RENDERTHREADFAILURE (0x88980406) / Тип: COMException")
//   — то есть приложение НЕ падало, но пугало пользователя сырым кодом
//   ошибки при полностью штатной, самовосстанавливающейся ситуации: этот
//   конкретный HRESULT — сбой потока композиции WPF, который почти всегда
//   возникает сразу после выхода из спящего режима/сброса видеодрайвера и
//   обычно не требует вмешательства (WPF пересоздаёт поток композиции
//   сам). Теперь для НЕЁ диалог не показывается вовсе — только
//   Trace-запись для диагностики. Остальные исключения по-прежнему
//   показываются, но не чаще одного диалога за ErrorDialogThrottle, чтобы
//   всплеск из нескольких исключений подряд не заваливал пользователя
//   стопкой модальных окон (см. общее пожелание "ошибки не должны
//   слишком часто всплывать").
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using PhotoFrame.Helpers;
using PhotoFrame.Models;
using PhotoFrame.Services;

namespace PhotoFrame
{
    public partial class App : Application
    {
        public static AppTheme     CurrentTheme { get; private set; } = AppTheme.System;
        public static AppStartMode StartMode    { get; private set; } = AppStartMode.Normal;
        public static IntPtr       PreviewHwnd  { get; private set; } = IntPtr.Zero;
        public static event Action<AppTheme>? ThemeChanged;

        /// <summary>HRESULT для UCEERR_RENDERTHREADFAILURE — транзиентный сбой
        /// потока композиции WPF (обычно сразу после resume/смены GPU-
        /// драйвера); WPF в большинстве случаев восстанавливает поток сам.</summary>
        private const int UCEERR_RENDERTHREADFAILURE = unchecked((int)0x88980406);

        private static readonly TimeSpan ErrorDialogThrottle = TimeSpan.FromSeconds(5);
        private static DateTime _lastErrorDialogUtc = DateTime.MinValue;

        protected override void OnStartup(StartupEventArgs e)
        {
            // build 56: доп. защита на вызывающей стороне (сам метод уже
            // оборачивает P/Invoke в try/catch — это просто ещё один слой
            // страховки, чтобы вообще ничто в этой строке не могло сорвать
            // запуск приложения).
            try { LiveTileService.SetAppUserModelId(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[PhotoFrame] SetAppUserModelId: {ex.Message}");
            }
            base.OnStartup(e);
            (StartMode, PreviewHwnd) = SystemIntegration.ParseArgs(e.Args);

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Подстраховка (build 53): раньше необработанное исключение на
            // ЛЮБОМ фоновом потоке (вне Dispatcher) заваливало бы процесс
            // без единого следа — большинство мест в Services уже обёрнуты
            // в try/catch, но это защита "на всякий случай", а не замена
            // им. Ничего не подавляет — только логирует перед тем, как
            // .NET всё равно завершит процесс (UnhandledException не имеет
            // Handled-флага, в отличие от DispatcherUnhandledException).
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                System.Diagnostics.Trace.WriteLine(
                    $"[PhotoFrame] Необработанное исключение вне UI-потока: {args.ExceptionObject}");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[PhotoFrame] Необработанное исключение задачи: {args.Exception}");
                args.SetObserved();
            };

            try
            {
                var settings = SettingsService.Load();
                ApplyTheme(settings.Theme, notify: false);
                ApplyUiMode(settings.UiMode);
            }
            catch
            {
                ApplyTheme(AppTheme.System, notify: false);
                ApplyUiMode(UiMode.Modern);
            }

            if (StartMode == AppStartMode.Configure)
                new Views.SettingsWindow(SettingsService.Load()).Show();
        }

        private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs ex)
        {
            var inner = ex.Exception;
            while (inner.InnerException != null) inner = inner.InnerException;

            if (inner is COMException comEx && comEx.HResult == UCEERR_RENDERTHREADFAILURE)
            {
                System.Diagnostics.Trace.WriteLine(
                    "[PhotoFrame] UCEERR_RENDERTHREADFAILURE перехвачен и подавлен " +
                    "(транзиентный сбой потока композиции WPF, обычно после resume) — диалог не показан.");
                ex.Handled = true;
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastErrorDialogUtc) < ErrorDialogThrottle) { ex.Handled = true; return; }
            _lastErrorDialogUtc = now;

            MessageBox.Show($"Ошибка: {inner.Message}\n\nТип: {inner.GetType().Name}",
                "PhotoFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        }

        public static void ChangeTheme(AppTheme t) => ApplyTheme(t, notify: true);

        /// <summary>
        /// Применяет/снимает наложение AeroTheme (+ AeroDarkTheme при тёмной
        /// цветовой схеме) поверх CommonStyles. Снятие обоих словарей
        /// восстанавливает Modern-оформление по умолчанию.
        /// </summary>
        public static void ApplyUiMode(UiMode mode)
        {
            CurrentUiMode = mode;
            RefreshAeroOverlay();
            UiModeChanged?.Invoke(mode);
        }

        public static UiMode CurrentUiMode { get; private set; } = UiMode.Modern;
        public static event Action<UiMode>? UiModeChanged;

        /// <summary>
        /// Пересобирает стек Aero-словарей в соответствии с текущими
        /// CurrentUiMode и CurrentTheme. AeroTheme.xaml задаёт структуру
        /// (стеклянные шаблоны), AeroDarkTheme.xaml — переопределяет базовую
        /// палитру и те стили, что были зашиты под светлое стекло, поэтому
        /// он всегда добавляется ПОСЛЕ AeroTheme.xaml, чтобы выиграть в
        /// порядке поиска ресурсов WPF.
        /// </summary>
        private static void RefreshAeroOverlay()
        {
            var dicts = Current.Resources.MergedDictionaries;
            const string aeroSource     = "Themes/AeroTheme.xaml";
            const string aeroDarkSource = "Themes/AeroDarkTheme.xaml";

            for (int i = dicts.Count - 1; i >= 0; i--)
            {
                var src = dicts[i].Source?.OriginalString;
                if (src != null && (src.EndsWith("AeroTheme.xaml", StringComparison.OrdinalIgnoreCase)
                                  || src.EndsWith("AeroDarkTheme.xaml", StringComparison.OrdinalIgnoreCase)))
                    dicts.RemoveAt(i);
            }

            if (CurrentUiMode == UiMode.Aero7)
            {
                dicts.Add(new ResourceDictionary { Source = new Uri(aeroSource, UriKind.Relative) });
                if (CurrentTheme == AppTheme.Dark)
                    dicts.Add(new ResourceDictionary { Source = new Uri(aeroDarkSource, UriKind.Relative) });
            }
        }

        private static void ApplyTheme(AppTheme requested, bool notify)
        {
            AppTheme actual = requested == AppTheme.System
                ? (WindowHelper.IsSystemDarkMode() ? AppTheme.Dark : AppTheme.Light)
                : requested;
            CurrentTheme = actual;

            Uri uri = actual == AppTheme.Light
                ? new Uri("Themes/LightTheme.xaml", UriKind.Relative)
                : new Uri("Themes/DarkTheme.xaml",  UriKind.Relative);

            var dicts = Current.Resources.MergedDictionaries;
            bool replaced = false;
            for (int i = 0; i < dicts.Count; i++)
            {
                var src = dicts[i].Source?.OriginalString;
                if (src != null && src.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase)
                    && !src.EndsWith("AeroTheme.xaml", StringComparison.OrdinalIgnoreCase)
                    && !src.EndsWith("AeroDarkTheme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    dicts.Insert(i, new ResourceDictionary { Source = uri });
                    dicts.RemoveAt(i + 1);
                    replaced = true; break;
                }
            }
            if (!replaced) dicts.Insert(0, new ResourceDictionary { Source = uri });

            // Тема сменилась — Aero7-оверлей должен отреагировать (Light↔Dark),
            // поэтому пересобираем его стек здесь же, а не только в ApplyUiMode.
            RefreshAeroOverlay();

            if (notify) ThemeChanged?.Invoke(actual);
        }
    }
}

