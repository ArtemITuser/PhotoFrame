// Services/SystemIntegration.cs — v4.0 (build 52)
//
// • DllImport → LibraryImport (source-generated P/Invoke marshaling)
// • PreventSleep: проверяет результат SetThreadExecutionState
// • Win7/8: предложение установить Segoe MDL2 Assets (archive.org), без
//   повторов на Win10+, с выбором "пропустить навсегда"
// • Чёрный список недоступных путей источников (DismissedPaths)
// • GetMemoryUsageInfo()/SafeCleanup() — для "О программе"
// • IsSystemIdle()/GetSystemIdleTime() через GetLastInputInfo — для
//   корректного скринсейвера из трея
// • Расписание автоотключения рамки: системный интеллектуальный подсчёт
//   простоя, ручное расписание от/до, либо закат-рассвет (через
//   геолокацию IP или сохранённые координаты)
// • Зеркало/своя ссылка для проверки обновлений (вместо хардкод GitHub URL)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace PhotoFrame.Services
{
    public enum AppStartMode { Normal = 0, Screensaver = 1, Preview = 2, Configure = 3 }

    public static partial class SystemIntegration
    {
        // ─── Shared HttpClient (аудит GC, build 52) ─────────────────────────────
        // Ранее каждый метод создавал собственный `using var http = new
        // HttpClient(...)`. При частых вызовах (проверка обновлений,
        // геолокация) это приводит к исчерпанию сокетов из-за TIME_WAIT —
        // известная проблема .NET (HttpClient не предназначен для
        // одноразового использования). Один статический экземпляр без
        // фиксированного Timeout + CancellationTokenSource на каждый вызов
        // даёт тот же контроль за временем ожидания без повторного создания
        // соединений/сборщика мусора для каждого запроса.
        private static readonly HttpClient _sharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

        // ─── Args parsing ────────────────────────────────────────────────────────
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

        // ─── Screensaver registration ────────────────────────────────────────────
        public static bool RegisterScreensaver(out string error)
        {
            error = string.Empty;
            try
            {
                string? exe = ResolveHostExecutablePath();
                if (string.IsNullOrEmpty(exe))
                {
                    error = "Не удалось определить путь к исполняемому файлу приложения.";
                    return false;
                }
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string scr   = Path.Combine(sys32, "PhotoFrame.scr");
                bool copied  = false;
                try { File.Copy(exe, scr, true); copied = true; }
                catch (UnauthorizedAccessException) { copied = ElevatedCopy(exe, scr); }
                catch (IOException ioEx)
                {
                    error = $"Файл занят другим процессом: {ioEx.Message}";
                    return false;
                }
                if (!copied)
                {
                    error = "Не удалось скопировать в System32. Подтвердите UAC.";
                    return false;
                }
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                    if (k == null)
                    {
                        error = "Не удалось открыть раздел реестра Control Panel\\Desktop.";
                        return false;
                    }
                    k.SetValue("SCRNSAVE.EXE", scr);
                    k.SetValue("ScreenSaveActive", "1");
                    k.SetValue("ScreenSaveIsSecure", "0");
                }
                catch (UnauthorizedAccessException)
                {
                    error = "Файл скопирован, но нет прав на запись в реестр (HKCU\\Control Panel\\Desktop).";
                    return false;
                }
                catch (System.Security.SecurityException)
                {
                    error = "Файл скопирован, но недостаточно прав для изменения реестра.";
                    return false;
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Недостаточно прав. Запустите PhotoFrame от имени администратора.";
                return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static void UnregisterScreensaver()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                k?.DeleteValue("SCRNSAVE.EXE", false);
                k?.SetValue("ScreenSaveActive", "0");
            }
            catch { }
        }

        public static bool IsScreensaverRegistered()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                return (k?.GetValue("SCRNSAVE.EXE") as string)
                    ?.Contains("PhotoFrame", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch { return false; }
        }

        public static void SetScreensaverDelay(int minutes)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                k?.SetValue("ScreenSaveTimeOut", (minutes * 60).ToString());
            }
            catch { }
        }

        // ─── True idle detection (GetLastInputInfo) ──────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize, dwTime; }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetLastInputInfo(ref LASTINPUTINFO p);

        public static TimeSpan GetSystemIdleTime()
        {
            try
            {
                var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref lii)) return TimeSpan.Zero;
                return TimeSpan.FromMilliseconds((uint)Environment.TickCount - lii.dwTime);
            }
            catch { return TimeSpan.Zero; }
        }

        public static bool IsSystemIdle(TimeSpan threshold) => GetSystemIdleTime() >= threshold;

        // ─── MSIX-упаковка (build 52) ────────────────────────────────────────────
        [LibraryImport("kernel32.dll")]
        private static partial int GetCurrentPackageFullName(ref int length, IntPtr fullName);

        private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

        /// <summary>
        /// True, если процесс запущен из MSIX-пакета (package identity).
        /// Для InnoSetup/ClickOnce всегда false.
        /// </summary>
        public static bool IsRunningAsMsixPackage()
        {
            try
            {
                int len = 0;
                int rc = GetCurrentPackageFullName(ref len, IntPtr.Zero);
                return rc != APPMODEL_ERROR_NO_PACKAGE; // 0 или ERROR_INSUFFICIENT_BUFFER = пакет есть
            }
            catch { return false; } // API недоступен на старых системах — считаем "не MSIX"
        }

        // ─── Autostart (HKCU\Run) ─────────────────────────────────────────────────
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// Возвращает путь, который действительно можно запустить как
        /// приложение (в кавычках, готов для записи в реестр/ярлык):
        ///  1) ClickOnce — путь к .exe меняется при КАЖДОМ автообновлении
        ///     (изолированный кеш-каталог с версией в пути), поэтому
        ///     единственный стабильный вариант — ярлык .appref-ms в
        ///     Меню Пуск, который ClickOnce сам поддерживает актуальным.
        ///  2) Обычный/MSIX/InnoSetup запуск — <see cref="Environment.ProcessPath"/>
        ///     корректно возвращает apphost .exe даже для single-file и
        ///     self-contained публикации, в отличие от
        ///     Assembly.GetExecutingAssembly().Location, который для .NET 5+
        ///     часто указывает на управляемую .dll (её нельзя запустить
        ///     напрямую через ShellExecute) либо вовсе пуст для single-file.
        /// </summary>
        private static string? ResolveHostExecutablePath()
        {
            try
            {
                var appRefMs = FindClickOnceShortcut();
                if (!string.IsNullOrEmpty(appRefMs)) return appRefMs;
            }
            catch { /* не ClickOnce либо ярлык не найден — переходим к обычному пути */ }

            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return exe;

            // Крайний запасной вариант — прежнее поведение.
            string fallback = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrEmpty(fallback) ? null : fallback;
        }

        private static string? FindClickOnceShortcut()
        {
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (!Directory.Exists(programs)) return null;
            // ClickOnce публикует ярлык как <Programs>\<Publisher>\<Product>.appref-ms
            return Directory.EnumerateFiles(programs, "PhotoFrame*.appref-ms", SearchOption.AllDirectories)
                             .FirstOrDefault();
        }

        public static bool SetAutostart(bool enable, out string error)
        {
            error = string.Empty;

            // MSIX: автозапуск объявлен в Package.appxmanifest как
            // windows.startupTask — пользователь включает/выключает его в
            // Диспетчере задач → «Автозагрузка», а не через HKCU\Run.
            // Запись в реестр отсюда для MSIX не требуется и не является
            // штатным способом для магазинных приложений.
            if (IsRunningAsMsixPackage())
            {
                error = "Приложение установлено как MSIX-пакет: включите автозапуск " +
                         "в Диспетчере задач → вкладка «Автозагрузка» → PhotoFrame.";
                return false;
            }

            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (k == null)
                {
                    error = "Не удалось открыть раздел реестра автозагрузки (HKCU\\...\\Run).";
                    return false;
                }
                if (enable)
                {
                    string? target = ResolveHostExecutablePath();
                    if (string.IsNullOrEmpty(target))
                    {
                        error = "Не удалось определить путь запуска приложения.";
                        return false;
                    }
                    k.SetValue("PhotoFrame", $"\"{target}\"");
                }
                else
                {
                    k.DeleteValue("PhotoFrame", false);
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Нет доступа к разделу реестра автозагрузки.";
                return false;
            }
            catch (System.Security.SecurityException)
            {
                error = "Недостаточно прав для изменения автозагрузки.";
                return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool IsAutostartEnabled()
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("PhotoFrame") != null; }
            catch { return false; }
        }

        // ─── Sleep prevention ────────────────────────────────────────────────────
        private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM = 0x00000001, ES_DISPLAY = 0x00000002;

        [LibraryImport("kernel32.dll")]
        private static partial uint SetThreadExecutionState(uint flags);

        /// <returns>true если вызов успешен (результат проверяется)</returns>
        public static bool PreventSleep(bool prevent)
        {
            try
            {
                uint res = SetThreadExecutionState(
                    prevent ? ES_CONTINUOUS | ES_SYSTEM | ES_DISPLAY : ES_CONTINUOUS);
                return res != 0;
            }
            catch { return false; }
        }

        // ─── Power timeouts ──────────────────────────────────────────────────────
        [LibraryImport("powrprof.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [LibraryImport("powrprof.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PowerWriteACValueIndex(
            IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint val);
        [LibraryImport("powrprof.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [LibraryImport("kernel32.dll")]
        private static partial IntPtr LocalFree(IntPtr mem);

        private static readonly Guid G_SLEEP_SUB = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
        private static readonly Guid G_STANDBY   = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
        private static readonly Guid G_DISP_SUB  = new("7516B95F-F776-4464-8C53-06167F40CC99");
        private static readonly Guid G_MON_OFF   = new("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");

        public static bool SetPowerTimeouts(int monSec, int sleepSec)
        {
            try
            {
                if (!PowerGetActiveScheme(IntPtr.Zero, out IntPtr p) || p == IntPtr.Zero) return false;
                var s = Marshal.PtrToStructure<Guid>(p);
                LocalFree(p);
                var dSub = G_DISP_SUB; var dSet = G_MON_OFF;
                var sSub = G_SLEEP_SUB; var sSet = G_STANDBY;
                return PowerWriteACValueIndex(IntPtr.Zero, ref s, ref dSub, ref dSet, (uint)monSec)
                    && PowerWriteACValueIndex(IntPtr.Zero, ref s, ref sSub, ref sSet, (uint)sleepSec)
                    && PowerSetActiveScheme(IntPtr.Zero, ref s);
            }
            catch { return false; }
        }

        // ─── Win7/8: Segoe MDL2 font fallback ────────────────────────────────────
        private const string RegPath = @"Software\PhotoFrame";
        private const string FontUrl = "https://archive.org/download/segmdl2/segmdl2.ttf";

        public static bool IsLegacyWindows => Environment.OSVersion.Version.Major < 10;

        public static bool IsSegoeMdl2Installed()
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
                return k?.GetValueNames().Any(n =>
                    n.Contains("Segoe MDL2", StringComparison.OrdinalIgnoreCase)) == true;
            }
            catch { return false; }
        }

        public static bool ShouldOfferMdl2Font()
        {
            if (!IsLegacyWindows || IsSegoeMdl2Installed()) return false;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RegPath);
                return (k?.GetValue("Mdl2FontDeclined") as string) != "1";
            }
            catch { return true; }
        }

        public static void SetMdl2FontDeclinedForever()
        {
            try { using var k = Registry.CurrentUser.CreateSubKey(RegPath); k?.SetValue("Mdl2FontDeclined", "1"); }
            catch { }
        }

        public static async System.Threading.Tasks.Task<bool> DownloadAndInstallMdl2FontAsync(
            Action<string>? progress = null)
        {
            try
            {
                progress?.Invoke("Загрузка шрифта…");
                string tmp = Path.Combine(Path.GetTempPath(), "segmdl2.ttf");
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    using var resp = await _sharedHttp.GetAsync(FontUrl, cts.Token);
                    resp.EnsureSuccessStatusCode();
                    await using var fs = File.Create(tmp);
                    await resp.Content.CopyToAsync(fs, cts.Token);
                }
                progress?.Invoke("Установка (потребуется UAC)…");
                string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                string dst   = Path.Combine(fonts, "segmdl2.ttf");
                string scr   = Path.Combine(Path.GetTempPath(), "install_mdl2.cmd");
                File.WriteAllText(scr,
                    "@echo off\r\n" + $"copy /y \"{tmp}\" \"{dst}\"\r\n" +
                    "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts\" " +
                    "/v \"Segoe MDL2 Assets (TrueType)\" /t REG_SZ /d \"segmdl2.ttf\" /f\r\n");
                var psi = new ProcessStartInfo("cmd.exe", $"/c \"{scr}\"")
                    { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
                using var proc = Process.Start(psi);
                if (proc != null) await proc.WaitForExitAsync();
                try { File.Delete(scr); } catch { }
                return IsSegoeMdl2Installed() || File.Exists(dst);
            }
            catch { return false; }
        }

        // ─── Dismissed-paths stop-list ────────────────────────────────────────────
        private const string StopListKey = "DismissedPaths";

        public static HashSet<string> LoadDismissedPaths()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RegPath);
                var raw = k?.GetValue(StopListKey) as string;
                return string.IsNullOrEmpty(raw)
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(raw.Split('|', StringSplitOptions.RemoveEmptyEntries),
                        StringComparer.OrdinalIgnoreCase);
            }
            catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        }

        public static void SaveDismissedPaths(IEnumerable<string> paths)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RegPath);
                k?.SetValue(StopListKey, string.Join('|', paths.Distinct(StringComparer.OrdinalIgnoreCase)));
            }
            catch { }
        }

        public static void ClearDismissedPaths()
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(RegPath, true); k?.DeleteValue(StopListKey, false); }
            catch { }
        }

        // ─── Memory / cache info ──────────────────────────────────────────────────
        public class MemoryInfo
        {
            public long WorkingSetBytes { get; init; }
            public long PrivateBytes    { get; init; }
            public long GcHeapBytes     { get; init; }
            public long ThumbCacheBytes { get; init; }
            public int  ThumbCacheCount { get; init; }
        }

        public static MemoryInfo GetMemoryUsageInfo()
        {
            long ws = 0, priv = 0, gc = 0;
            try { using var p = Process.GetCurrentProcess(); ws = p.WorkingSet64; priv = p.PrivateMemorySize64; }
            catch { }
            try { gc = GC.GetTotalMemory(false); } catch { }
            long cb = 0; int cc = 0;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "PhotoFrameCache");
                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    cc = files.Length;
                    foreach (var f in files) try { cb += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return new MemoryInfo
                { WorkingSetBytes = ws, PrivateBytes = priv, GcHeapBytes = gc, ThumbCacheBytes = cb, ThumbCacheCount = cc };
        }

        public static (bool ok, string message) SafeCleanup()
        {
            try
            {
                long before = GC.GetTotalMemory(false);
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                    System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                long after = GC.GetTotalMemory(false);
                int del = 0;
                try
                {
                    string dir = Path.Combine(Path.GetTempPath(), "PhotoFrameCache");
                    if (Directory.Exists(dir))
                        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                            try { File.Delete(f); del++; } catch { }
                }
                catch { }
                return (true, $"Освобождено ≈{Math.Max(0, before - after) / 1048576} МБ, удалено кешей: {del}.");
            }
            catch (Exception ex) { return (false, $"Ошибка: {ex.Message}"); }
        }

        // ─── Закат/рассвет (для расписания автоотключения) ──────────────────────
        //
        // Простая астрономическая формула (NOAA Solar Calculator, упрощённая) —
        // не требует сети. Точность ±2-3 минуты, достаточно для авто-расписания.
        // Координаты берутся либо из ручных настроек пользователя, либо через
        // приблизительную IP-геолокацию (см. TryGetApproxLocationAsync).

        public static (DateTime sunrise, DateTime sunset) CalculateSunTimes(
            double latitude, double longitude, DateTime date)
        {
            double zenith = 90.833; // официальный закат/рассвет с учётом рефракции
            double lngHour = longitude / 15.0;

            (DateTime, DateTime) Calc()
            {
                DateTime CalcOne(bool isSunrise)
                {
                    double n = date.DayOfYear;
                    double t = isSunrise ? n + ((6 - lngHour) / 24) : n + ((18 - lngHour) / 24);
                    double m = (0.9856 * t) - 3.289;
                    double l = m + (1.916 * Math.Sin(Deg2Rad(m)))
                                 + (0.020 * Math.Sin(Deg2Rad(2 * m))) + 282.634;
                    l = NormalizeDeg(l);
                    double ra = Rad2Deg(Math.Atan(0.91764 * Math.Tan(Deg2Rad(l))));
                    ra = NormalizeDeg(ra);
                    double lQuad = Math.Floor(l / 90) * 90;
                    double raQuad = Math.Floor(ra / 90) * 90;
                    ra += lQuad - raQuad;
                    ra /= 15;

                    double sinDec = 0.39782 * Math.Sin(Deg2Rad(l));
                    double cosDec = Math.Cos(Math.Asin(sinDec));
                    double cosH = (Math.Cos(Deg2Rad(zenith)) - (sinDec * Math.Sin(Deg2Rad(latitude))))
                                  / (cosDec * Math.Cos(Deg2Rad(latitude)));
                    cosH = Math.Clamp(cosH, -1, 1); // защита от полярного дня/ночи

                    double h = isSunrise
                        ? 360 - Rad2Deg(Math.Acos(cosH))
                        : Rad2Deg(Math.Acos(cosH));
                    h /= 15;

                    double localT = h + ra - (0.06571 * t) - 6.622;
                    double utcT = NormalizeHour(localT - lngHour);

                    var utcDate = date.Date.AddHours(utcT);
                    return DateTime.SpecifyKind(utcDate, DateTimeKind.Utc).ToLocalTime();
                }
                return (CalcOne(true), CalcOne(false));
            }
            return Calc();

            static double Deg2Rad(double d) => d * Math.PI / 180.0;
            static double Rad2Deg(double r) => r * 180.0 / Math.PI;
            static double NormalizeDeg(double v) => v < 0 ? v + 360 : v >= 360 ? v - 360 : v;
            static double NormalizeHour(double v) => v < 0 ? v + 24 : v >= 24 ? v - 24 : v;
        }

        /// <summary>
        /// Приблизительная геолокация по IP (без точного GPS) — используется
        /// только если пользователь не указал координаты вручную в настройках.
        /// Бесплатный сервис ip-api.com, без ключа. Возвращает null при ошибке.
        /// </summary>
        public static async System.Threading.Tasks.Task<(double lat, double lon)?>
            TryGetApproxLocationAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                string json = await _sharedHttp.GetStringAsync(
                    "http://ip-api.com/json/?fields=lat,lon,status", cts.Token);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var st) && st.GetString() == "success"
                    && root.TryGetProperty("lat", out var la) && root.TryGetProperty("lon", out var lo))
                    return (la.GetDouble(), lo.GetDouble());
                return null;
            }
            catch { return null; }
        }

        // ─── ClickOnce ────────────────────────────────────────────────────────────
        public static bool IsClickOnceDeployed =>
            Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed")
                ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        public static string? ClickOnceCurrentVersion =>
            Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion");
        public static string GetClickOnceInfo() =>
            IsClickOnceDeployed
            ? $"ClickOnce активен.\nВерсия: {ClickOnceCurrentVersion ?? "?"}\n" +
              $"Источник: {Environment.GetEnvironmentVariable("ClickOnce_UpdateLocation") ?? "не задан"}"
            : "Запущено напрямую (не через ClickOnce). Установите через ClickOnce для автообновлений.";

        // ─── GitHub update check (с поддержкой зеркала/своей ссылки) ────────────
        //
        // По умолчанию проверяет официальный репозиторий GitHub. Если в
        // настройках указан UpdateMirrorUrl, используется он вместо
        // дефолтного — должен быть REST-эндпоинтом, возвращающим JSON
        // в формате GitHub Releases API ({"tag_name": "v1.2.3", ...}).
        // Это позволяет указать зеркало (напр. для regions с ограниченным
        // доступом к GitHub) или собственный сервер релизов.

        private const string DefaultUpdateUrl =
            "https://api.github.com/repos/ArtemITuser/PhotoFrame/releases/latest";

        public static async System.Threading.Tasks.Task<(string? tag, bool isNewer)>
            CheckUpdateAsync(Version currentVersion, string? customUrl = null)
        {
            string url = string.IsNullOrWhiteSpace(customUrl) ? DefaultUpdateUrl : customUrl;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "PhotoFrame-UpdateChecker");
                using var resp = await _sharedHttp.SendAsync(req, cts.Token);
                resp.EnsureSuccessStatusCode();
                string json = await resp.Content.ReadAsStringAsync(cts.Token);
                const string key = "\"tag_name\"";
                int idx = json.IndexOf(key, StringComparison.Ordinal);
                if (idx < 0) return (null, false);
                int s = json.IndexOf('"', idx + key.Length + 1);
                int e = json.IndexOf('"', s + 1);
                if (s < 0 || e <= s) return (null, false);
                string tag = json.Substring(s + 1, e - s - 1);
                bool newer = Version.TryParse(tag.TrimStart('v', 'V').Trim(), out var v) && v > currentVersion;
                return (tag, newer);
            }
            catch { return (null, false); }
        }

        // Обратная совместимость со старым именем метода
        public static System.Threading.Tasks.Task<(string? tag, bool isNewer)>
            CheckGitHubUpdateAsync(Version cur) => CheckUpdateAsync(cur, null);

        public static void OpenGitHub()
        { try { Process.Start(new ProcessStartInfo("https://github.com/ArtemITuser/PhotoFrame") { UseShellExecute = true }); } catch { } }

        public static void OpenGitHubReleases()
        { try { Process.Start(new ProcessStartInfo("https://github.com/ArtemITuser/PhotoFrame/releases") { UseShellExecute = true }); } catch { } }

        public static void OpenUrl(string url)
        { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

        // ─── Private ──────────────────────────────────────────────────────────────
        private static bool ElevatedCopy(string src, string dst)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c copy /y \"{src}\" \"{dst}\"")
                    { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using var p = Process.Start(psi);
                p?.WaitForExit(12000);
                return File.Exists(dst);
            }
            catch { return false; }
        }
    }
}
