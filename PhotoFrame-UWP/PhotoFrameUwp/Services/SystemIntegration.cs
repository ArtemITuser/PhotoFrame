// Services/SystemIntegration.cs — UWP-порт v3.2
// Логика верхнего уровня (парсинг режимов, обновления GitHub, питание) сохранена 1:1,
// но WPF/.NET8-only элементы заменены на UWP-эквиваленты:
//   * Реестр / System32 .scr          → недоступен в sandbox → FullScreenMode (Mobile)
//     + «режим фоторамки» внутри приложения; регистрация скринсейвера возвращает false
//     с понятным сообщением (без исключения).
//   * Registry Run key (автозагрузка) → ApplicationData + StartupTaskContract не доступен
//     на Mobile → эмулируется запуском с AutoStart==true (флаг сохраняется в настройках).
//   * SetThreadExecutionState         → Windows.System.Display.DisplayRequest.
//   * PowerGetActiveScheme/powrprof   → недоступно (P/Invoke запрещён в sandbox) → no-op.
//   * Process.Start(UseShellExecute)  → Windows.System.Launcher.
//   * WMI Win32_VolumeChangeEvent     → StorageLibrary/KnownFolders.RemovableDevices polling.

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.System.Profile;
using Windows.System.Display;
using Windows.UI.ViewManagement;

namespace PhotoFrameUwp.Services
{
    public enum AppStartMode { Normal = 0, Screensaver = 1, Preview = 2, Configure = 3 }

    public static class SystemIntegration
    {
        private static DisplayRequest _displayRequest;

        // ─── Аргументы запуска ────────────────────────────────────────────────────
        // В UWP нет командной строки скринсейвера (.scr /S /P /C). Аналог:
        //   * ActivationKind / аргументы протокола photoframe:/s ;
        //   * на Mobile — FullScreenMode (ApplicationView);
        //   * «Configure» — активация SettingsContract (меню параметров системы).

        public static AppStartMode ParseArgs(string[] args)
        {
            if (args == null || args.Length == 0) return AppStartMode.Normal;
            switch (args[0].ToUpperInvariant().Trim())
            {
                case "/S": return AppStartMode.Screensaver;
                case "/P":
                case "/L": return AppStartMode.Preview;
                case "/C": return AppStartMode.Configure;
                default:   return AppStartMode.Normal;
            }
        }

        /// <summary>Анализ protocol activation: photoframe:/s и т.п.</summary>
        public static AppStartMode ModeFromProtocol(string arguments)
        {
            if (string.IsNullOrEmpty(arguments)) return AppStartMode.Normal;
            return ParseArgs(new[] { arguments.TrimStart('/', ' ') });
        }

        // ─── «Скринсейвер» (FullScreenMode вместо .scr + реестра) ─────────────────

        /// <summary>
        /// На телефонах (Continuum/Mobile) включает системный FullScreenMode —
        /// приложение разворачивается на весь экран со скрытием статус-бара,
        /// что является UWP-эквивалентом режима скринсейвера.
        /// Возвращает true, если режим применён.
        /// </summary>
        public static bool TryEnterFullScreenScreensaver()
        {
            try
            {
                var av = ApplicationView.GetForCurrentView();
                if (av.IsFullScreenModeSupported())
                    return av.TryEnterFullScreenMode();
                return false;
            }
            catch { return false; }
        }

        public static void ExitFullScreenScreensaver()
        {
            try { ApplicationView.GetForCurrentView().ExitFullScreenMode(); }
            catch { }
        }

        /// <summary>Регистрация скринсейвера через реестр/System32 невозможна в UWP.</summary>
        public static bool RegisterScreensaver(out string error)
        {
            error = "В UWP-сборке регистрация .scr через System32/реестр недоступна " +
                    "(песочница AppContainer). Используйте встроенный полноэкранный " +
                    "режим фоторамки (FullScreenMode на Mobile / F11 на Desktop).";
            return false;
        }

        public static void UnregisterScreensaver() { /* no-op в UWP */ }

        public static bool IsScreensaverRegistered() => false;

        public static void SetScreensaverDelay(int minutes)
        {
            // Сохраняем задержку как пользовательскую настройку «авто-полный экран»;
            // реальная установка ScreenSaveTimeOut в реестре в sandbox невозможна.
            try
            {
                var c = ApplicationData.Current.LocalSettings;
                c.Values["ScreensaverDelayMinutes"] = minutes;
            }
            catch { }
        }

        // ─── Автозагрузка ──────────────────────────────────────────────────────────

        /// <summary>
        /// Autostart в UWP: на Desktop возможен StartupTask с RestrictedCapability
        /// (не для Mobile), поэтому флаг хранится в LocalSettings и применяется
        /// при следующем запуске (AutoStart слайдшоу + полный экран).
        /// </summary>
        public static void SetAutostart(bool enable)
        {
            try { ApplicationData.Current.LocalSettings.Values["AutostartEnabled"] = enable; }
            catch { }
        }

        public static bool IsAutostartEnabled()
        {
            try
            {
                object v;
                return ApplicationData.Current.LocalSettings.Values
                    .TryGetValue("AutostartEnabled", out v) && v is bool b && b;
            }
            catch { return false; }
        }

        // ─── Электропитание (DisplayRequest вместо SetThreadExecutionState) ───────

        public static void PreventSleep(bool prevent)
        {
            try
            {
                if (prevent)
                {
                    if (_displayRequest == null) _displayRequest = new DisplayRequest();
                    _displayRequest.RequestActive();
                }
                else if (_displayRequest != null)
                {
                    _displayRequest.RequestRelease();
                }
            }
            catch { }
        }

        /// <summary>Таймауты питания через powrprof.dll недоступны в sandbox — no-op.</summary>
        public static bool SetPowerTimeouts(int monitorSec, int sleepSec) => false;

        // ─── Развёртывание ─────────────────────────────────────────────────────────

        /// <summary>UWP всегда развёрнут как MSIX/Appx — ClickOnce неприменим.</summary>
        public static bool IsClickOnceDeployed => false;

        public static string GetDeploymentInfo()
        {
            string family = Utilities.HasProgrammaticAccess
                ? "programmatic" : "user";
            return "Развёртывание: Appx/UWP (sandbox).\n" +
                   "Обновления — через магазин или Sidecar/ADAL-установку (MaxVersionTested 16299).\n" +
                   "Канал активаций: " + family + ".";
        }

        // ─── Проверка обновлений через GitHub API (логика идентична WPF) ──────────

        public static async Task<Tuple<string, bool>>
            CheckGitHubUpdateAsync(Version currentVersion)
        {
            const string url =
                "https://api.github.com/repos/ArtemITuser/PhotoFrame/releases/latest";
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(10);
                    http.DefaultRequestHeaders.Add("User-Agent", "PhotoFrame-UpdateChecker");

                    string json = await http.GetStringAsync(url);

                    // Простой парсинг "tag_name" без зависимостей
                    const string key = "\"tag_name\"";
                    int idx = json.IndexOf(key, StringComparison.Ordinal);
                    if (idx < 0) return Tuple.Create((string)null, false);
                    int s = json.IndexOf('"', idx + key.Length + 1);
                    int e = json.IndexOf('"', s + 1);
                    if (s < 0 || e <= s) return Tuple.Create((string)null, false);

                    string tag = json.Substring(s + 1, e - s - 1);  // e.g. "v1.2.2.0"
                    string numStr = tag.TrimStart('v', 'V').Trim();

                    Version latest;
                    bool isNewer = Version.TryParse(numStr, out latest) &&
                                   latest > currentVersion;

                    return Tuple.Create(tag, isNewer);
                }
            }
            catch { return Tuple.Create((string)null, false); }
        }

        public static async void OpenGitHub()
        {
            try { await Launcher.LaunchUriAsync(new Uri("https://github.com/ArtemITuser/PhotoFrame")); }
            catch { }
        }

        public static async void OpenGitHubReleases()
        {
            try { await Launcher.LaunchUriAsync(new Uri("https://github.com/ArtemITuser/PhotoFrame/releases")); }
            catch { }
        }

        public static async Task OpenUriAsync(string uri)
        {
            try { await Launcher.LaunchUriAsync(new Uri(uri)); }
            catch { }
        }
    }
}
