// Services/SystemIntegration.cs — v3.2
// Скринсейвер, автозагрузка, питание, ClickOnce (env vars .NET 8), GitHub API.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Microsoft.Win32;

namespace PhotoFrame.Services
{
    public enum AppStartMode { Normal = 0, Screensaver = 1, Preview = 2, Configure = 3 }

    public static class SystemIntegration
    {
        // ─── v1.2.4.0: API для новых сервисов b62 (Idle / SunTimes / Location / MSIX / Autostart-out) ──

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        /// <summary>True, если система НЕ использовалась дольше idle.</summary>
        public static bool IsSystemIdle(TimeSpan idle)
        {
            try
            {
                var lii = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (!GetLastInputInfo(ref lii)) return false;
                var idleMs = (uint)Environment.TickCount - lii.dwTime;
                return idleMs >= idle.TotalMilliseconds;
            }
            catch { return false; }
        }

        /// <summary>Автозапуск с сообщением об ошибке (перегрузка для SettingsWindow).</summary>
        public static void SetAutostart(bool enable, out string? error)
        {
            error = null;
            try { SetAutostart(enable); }
            catch (Exception ex) { error = ex.Message; }
        }

        /// <summary>MSIX-packaged deployment detection (GetPackageId via kernel32).</summary>
        public static bool IsRunningAsMsixPackage()
        {
            try
            {
                int len = 0;
                if (GetCurrentProcessPackageId(IntPtr.Zero, ref len) == 0 && len > 0) return true;
                var buf = new char[len];
                GetCurrentProcessPackageId(buf, ref len);
                return len > 0;
            }
            catch { return false; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentProcessPackageId(char[] buffer, ref int length);

        /// <summary>Приблизительная локация по часовому поясу (без сетевых запросов). GPS-метаданные фото — приоритет выше по коду.</summary>
        public static System.Threading.Tasks.Task<(double lat, double lon)?> TryGetApproxLocationAsync()
        {
            return System.Threading.Tasks.Task.Run<(double lat, double lon)?>(() =>
            {
                try
                {
                    var tz = TimeZoneInfo.Local.Id;
                    if (TzApprox.TryGetValue(tz, out var c)) return c;
                    // fallback: смещение UTC -> грубая долгота
                    var off = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalHours;
                    return ((double)0, off * 15.0);
                }
                catch { return null; }
            });
        }

        private static readonly Dictionary<string, (double lat, double lon)> TzApprox = new()
        {
            ["Europe/Kyiv"] = (50.45, 30.52), ["Europe/Kiev"] = (50.45, 30.52),
            ["Europe/Moscow"] = (55.75, 37.62), ["Europe/Berlin"] = (52.52, 13.40),
            ["Europe/London"] = (51.50, -0.12), ["America/New_York"] = (40.71, -74.00),
            ["America/Los_Angeles"] = (34.05, -118.24), ["Asia/Almaty"] = (43.24, 76.89),
        };

        /// <summary>Рассвет/закат по упрощённой солнечной формуле (точность ~минуты, достаточно для day/night-логики).</summary>
        public static (DateTime sunrise, DateTime sunset) CalculateSunTimes(double lat, double lon, DateTime date)
        {
            var d = date.Date;
            double n = (d - new DateTime(2000, 1, 1)).TotalDays;
            double Jmean = n + 0.0008 - (lon / 360.0);
            double M = (357.5291 + 0.98560028 * Jmean) % 360.0;
            double Mr = M * Math.PI / 180.0;
            double C = 1.9148 * Math.Sin(Mr) + 0.02 * Math.Sin(2 * Mr) + 0.0003 * Math.Sin(3 * Mr);
            double lambda = (M + C + 180 + 102.9372) % 360.0;
            double lr = lambda * Math.PI / 180.0;
            double Jtransit = 2451545.0 + Jmean + 0.0053 * Math.Sin(Mr) - 0.0069 * Math.Sin(2 * lr);
            double sinDec = Math.Sin(lr) * 0.39782;
            double cosDec = Math.Cos(Math.Asin(sinDec));
            double latr = lat * Math.PI / 180.0;
            double cosH = (Math.Cos(90.833 * Math.PI / 180.0) - sinDec * Math.Sin(latr)) / (cosDec * Math.Cos(latr));
            if (cosH > 1)  return (d.AddHours(12), d.AddHours(12));   // полярная ночь
            if (cosH < -1) return (d, d.AddDays(1));                   // полярный день
            double H = Math.Acos(cosH) * 180.0 / Math.PI;
            double Jrise = Jtransit - H / 360.0, Jset = Jtransit + H / 360.0;
            double frac(double J) => (J - Math.Floor(J) + 1) % 1;
            return (d.AddSeconds(frac(Jrise) * 86400), d.AddSeconds(frac(Jset) * 86400));
        }

        // ─── Аргументы командной строки ──────────────────────────────────────────

        public static (AppStartMode mode, IntPtr hwnd) ParseArgs(string[] args)
        {
            if (args.Length == 0) return (AppStartMode.Normal, IntPtr.Zero);
            return args[0].ToUpperInvariant().Trim() switch
            {
                "/S" => (AppStartMode.Screensaver, IntPtr.Zero),
                "/P" or "/L" => args.Length > 1 && long.TryParse(args[1], out long h)
                    ? (AppStartMode.Preview, new IntPtr(h))
                    : (AppStartMode.Preview, IntPtr.Zero),
                "/C" => (AppStartMode.Configure, IntPtr.Zero),
                _    => (AppStartMode.Normal, IntPtr.Zero)
            };
        }

        // ─── Скринсейвер ─────────────────────────────────────────────────────────

        public static bool RegisterScreensaver(out string error)
        {
            error = string.Empty;
            try
            {
                string exe     = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string sys32   = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string scrPath = Path.Combine(sys32, "PhotoFrame.scr");

                bool copied = false;
                try { File.Copy(exe, scrPath, overwrite: true); copied = true; }
                catch (UnauthorizedAccessException) { copied = ElevatedCopy(exe, scrPath); }

                if (!copied) { error = "Не удалось скопировать в System32 даже с UAC."; return false; }

                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                key?.SetValue("SCRNSAVE.EXE",     scrPath);
                key?.SetValue("ScreenSaveActive", "1");
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static void UnregisterScreensaver()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                key?.DeleteValue("SCRNSAVE.EXE", false);
                key?.SetValue("ScreenSaveActive", "0");
            }
            catch { }
        }

        public static bool IsScreensaverRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                return (key?.GetValue("SCRNSAVE.EXE") as string)
                    ?.Contains("PhotoFrame", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch { return false; }
        }

        public static void SetScreensaverDelay(int minutes)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                key?.SetValue("ScreenSaveTimeOut", (minutes * 60).ToString());
            }
            catch { }
        }

        // ─── Автозагрузка (HKCU\Run — без UAC) ───────────────────────────────────

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void SetAutostart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (enable)
                {
                    string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    key?.SetValue("PhotoFrame", $"\"{exe}\"");
                }
                else key?.DeleteValue("PhotoFrame", false);
            }
            catch { }
        }

        public static bool IsAutostartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue("PhotoFrame") != null;
            }
            catch { return false; }
        }

        // ─── Электропитание ───────────────────────────────────────────────────────

        private const uint ES_CONTINUOUS       = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED  = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        public static void PreventSleep(bool prevent)
        {
            try
            {
                SetThreadExecutionState(prevent
                    ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
                    : ES_CONTINUOUS);
            }
            catch { }
        }

        [DllImport("powrprof.dll")] private static extern bool PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] private static extern bool PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint val);
        [DllImport("powrprof.dll")] private static extern bool PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

        private static readonly Guid GUID_SLEEP_SUB    = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
        private static readonly Guid GUID_STANDBY      = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
        private static readonly Guid GUID_DISPLAY_SUB  = new("7516B95F-F776-4464-8C53-06167F40CC99");
        private static readonly Guid GUID_MONITOR_OFF  = new("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");

        public static bool SetPowerTimeouts(int monitorSec, int sleepSec)
        {
            try
            {
                if (!PowerGetActiveScheme(IntPtr.Zero, out IntPtr p)) return false;
                var scheme = (Guid)(Marshal.PtrToStructure(p, typeof(Guid)) ?? Guid.Empty);
                LocalFree(p);
                var dSub  = GUID_DISPLAY_SUB; var dSet = GUID_MONITOR_OFF;
                var sSub  = GUID_SLEEP_SUB;   var sSet = GUID_STANDBY;
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref dSub, ref dSet, (uint)monitorSec);
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sSub, ref sSet, (uint)sleepSec);
                PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                return true;
            }
            catch { return false; }
        }

        // ─── ClickOnce (.NET 8) — через переменные среды ─────────────────────────
        // .NET 8 не поддерживает System.Deployment.Application.ApplicationDeployment.
        // Вместо него используем переменные среды ClickOnce_*, которые launcher
        // устанавливает при запуске ClickOnce-приложения.

        /// <summary>Запущено ли приложение как ClickOnce (сетевое развёртывание).</summary>
        public static bool IsClickOnceDeployed =>
            Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed")
                ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>Текущая версия по данным ClickOnce launcher.</summary>
        public static string? ClickOnceCurrentVersion =>
            Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion");

        /// <summary>URL источника обновлений ClickOnce.</summary>
        public static string? ClickOnceUpdateLocation =>
            Environment.GetEnvironmentVariable("ClickOnce_UpdateLocation");

        /// <summary>Информационный блок о ClickOnce для UI.</summary>
        public static string GetClickOnceInfo()
        {
            if (!IsClickOnceDeployed)
                return "Приложение запущено напрямую (не через ClickOnce).\n" +
                       "Для автообновлений установите приложение через ClickOnce " +
                       "(опубликуйте через VS → Publish).";

            string ver = ClickOnceCurrentVersion ?? "неизвестно";
            string loc = ClickOnceUpdateLocation ?? "не задан";
            return $"ClickOnce активен.\nВерсия развёртывания: {ver}\n" +
                   $"Источник обновлений: {loc}\n" +
                   "Обновления проверяются автоматически при запуске.";
        }

        // ─── Проверка обновлений через GitHub API ─────────────────────────────────

        /// <summary>
        /// Проверяет последний тег релиза на GitHub.
        /// Возвращает (latestTag, isNewer) или (null, false) при ошибке.
        /// </summary>
        public static async System.Threading.Tasks.Task<(string? tag, bool isNewer)>
            CheckGitHubUpdateAsync(System.Version currentVersion)
        {
            const string url =
                "https://api.github.com/repos/ArtemITuser/PhotoFrame/releases/latest";
            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                http.DefaultRequestHeaders.Add("User-Agent", "PhotoFrame-UpdateChecker");

                string json = await http.GetStringAsync(url);

                // Простой парсинг "tag_name" без зависимостей
                const string key = "\"tag_name\"";
                int idx = json.IndexOf(key, StringComparison.Ordinal);
                if (idx < 0) return (null, false);
                int s = json.IndexOf('"', idx + key.Length + 1);
                int e = json.IndexOf('"', s + 1);
                if (s < 0 || e <= s) return (null, false);

                string tag = json.Substring(s + 1, e - s - 1);  // e.g. "v1.0.8.5"
                string numStr = tag.TrimStart('v', 'V').Trim();

                bool isNewer = System.Version.TryParse(numStr, out var latest) &&
                               latest > currentVersion;

                return (tag, isNewer);
            }
            catch { return (null, false); }
        }

        public static void OpenGitHub()
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/ArtemITuser/PhotoFrame") { UseShellExecute = true }); }
            catch { }
        }

        public static void OpenGitHubReleases()
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/ArtemITuser/PhotoFrame/releases") { UseShellExecute = true }); }
            catch { }
        }

        // ─── Private helpers ──────────────────────────────────────────────────────

        private static bool ElevatedCopy(string src, string dst)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c copy /y \"{src}\" \"{dst}\"")
                {
                    UseShellExecute = true,
                    Verb            = "runas",
                    WindowStyle     = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(12_000);
                return File.Exists(dst);
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch { return false; }
        }
    }
}
