// Services/SystemIntegration.cs
// Системная интеграция: скринсейвер, автозагрузка, электропитание, трей.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PhotoFrame.Services
{
    public enum AppStartMode
    {
        Normal      = 0,   // Обычный запуск
        Screensaver = 1,   // /s — полноэкранный режим скринсейвера
        Preview     = 2,   // /p <hwnd> — предпросмотр в маленьком окне
        Configure   = 3,   // /c — открыть настройки
    }

    public static class SystemIntegration
    {
        // ─── Запуск в режиме скринсейвера ────────────────────────────────────────

        /// <summary>
        /// Разбирает аргументы командной строки скринсейвера.
        /// Возвращает режим запуска и HWND для Preview-режима.
        /// </summary>
        public static (AppStartMode mode, IntPtr previewHwnd) ParseArgs(string[] args)
        {
            if (args.Length == 0) return (AppStartMode.Normal, IntPtr.Zero);

            string arg0 = args[0].ToUpperInvariant().Trim();
            switch (arg0)
            {
                case "/S":
                    return (AppStartMode.Screensaver, IntPtr.Zero);

                case "/P":
                case "/L":
                    if (args.Length > 1 && long.TryParse(args[1], out long hwnd))
                        return (AppStartMode.Preview, new IntPtr(hwnd));
                    return (AppStartMode.Preview, IntPtr.Zero);

                case "/C":
                    return (AppStartMode.Configure, IntPtr.Zero);

                default:
                    return (AppStartMode.Normal, IntPtr.Zero);
            }
        }

        // ─── Регистрация как скринсейвер ─────────────────────────────────────────

        private const string ScrsaverRegistryKey =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

        /// <summary>
        /// Копирует .exe в System32 как .scr и прописывает в системе.
        /// Требует прав администратора, иначе возвращает false.
        /// </summary>
        public static bool RegisterScreensaver(out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                string exe    = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string scr    = Path.Combine(system32, "PhotoFrame.scr");

                File.Copy(exe, scr, overwrite: true);

                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Desktop", writable: true);
                key?.SetValue("SCRNSAVE.EXE", scr);
                key?.SetValue("ScreenSaveActive", "1");

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>Снимает регистрацию скринсейвера.</summary>
        public static void UnregisterScreensaver()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Desktop", writable: true);
                key?.DeleteValue("SCRNSAVE.EXE",    throwOnMissingValue: false);
                key?.SetValue("ScreenSaveActive", "0");
            }
            catch { /* игнорируем */ }
        }

        /// <summary>Возвращает true, если приложение уже зарегистрировано как скринсейвер.</summary>
        public static bool IsScreensaverRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                string? val = key?.GetValue("SCRNSAVE.EXE") as string;
                return val?.IndexOf("PhotoFrame", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        // ─── Задержка скринсейвера ────────────────────────────────────────────────

        public static void SetScreensaverDelay(int minutes)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Desktop", writable: true);
                key?.SetValue("ScreenSaveTimeOut", (minutes * 60).ToString());
            }
            catch { /* нет прав — игнорируем */ }
        }

        // ─── Автозапуск ───────────────────────────────────────────────────────────

        private const string RunKey =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void SetAutostart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (enable)
                {
                    string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    key?.SetValue("PhotoFrame", $"\"{exe}\"");
                }
                else
                {
                    key?.DeleteValue("PhotoFrame", throwOnMissingValue: false);
                }
            }
            catch { /* нет прав — игнорируем */ }
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

        // Флаги SetThreadExecutionState
        private const uint ES_CONTINUOUS        = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED   = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED  = 0x00000002;

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        /// <summary>Запрещает уход системы в сон/гибернацию пока приложение работает.</summary>
        public static void PreventSleep(bool prevent)
        {
            try
            {
                if (prevent)
                    SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
                else
                    SetThreadExecutionState(ES_CONTINUOUS);
            }
            catch { /* P/Invoke не работает — игнорируем */ }
        }

        // ─── Тайм-ауты монитора и сна ────────────────────────────────────────────

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool PowerWriteACValueIndex(
            IntPtr RootPowerKey, ref Guid SchemeGuid,
            ref Guid SubGroupGuid, ref Guid PowerSettingGuid, uint AcValueIndex);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

        [DllImport("powrprof.dll")]
        private static extern bool PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool LocalFree(IntPtr hMem);

        // GUID-ы подгрупп и параметров питания
        private static readonly Guid GUID_SLEEP_SUBGROUP   = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
        private static readonly Guid GUID_STANDBY_TIMEOUT  = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
        private static readonly Guid GUID_DISPLAY_SUBGROUP = new("7516B95F-F776-4464-8C53-06167F40CC99");
        private static readonly Guid GUID_MONITOR_TIMEOUT  = new("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");

        /// <summary>
        /// Задаёт тайм-аут монитора и сна в текущей схеме питания (значения в секундах; 0 = никогда).
        /// Применяется только для AC (от розетки).
        /// </summary>
        public static bool SetPowerTimeouts(int monitorOffSeconds, int sleepSeconds)
        {
            try
            {
                if (!PowerGetActiveScheme(IntPtr.Zero, out IntPtr pScheme)) return false;

                // Читаем Guid из неуправляемой памяти без unsafe-блока
                Guid scheme = (Guid)(System.Runtime.InteropServices.Marshal.PtrToStructure(
                    pScheme, typeof(Guid)) ?? Guid.Empty);
                LocalFree(pScheme);

                var dispSub  = GUID_DISPLAY_SUBGROUP;
                var dispGuid = GUID_MONITOR_TIMEOUT;
                var slpSub   = GUID_SLEEP_SUBGROUP;
                var slpGuid  = GUID_STANDBY_TIMEOUT;

                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref dispSub, ref dispGuid,
                    (uint)monitorOffSeconds);
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref slpSub, ref slpGuid,
                    (uint)sleepSeconds);
                PowerSetActiveScheme(IntPtr.Zero, ref scheme);

                return true;
            }
            catch { return false; }
        }
    }
}
