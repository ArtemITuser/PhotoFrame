// Services/SystemIntegration.cs — v3.7 (build 40)
// Screensaver, autostart, power management, ClickOnce env, GitHub update check.
// Screensaver logic fixed: RunKey stores full quoted path.
// Autostart: HKCU\Run — no UAC needed.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PhotoFrame.Services
{
    public enum AppStartMode { Normal=0, Screensaver=1, Preview=2, Configure=3 }

    public static class SystemIntegration
    {
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

        // ─── Screensaver ─────────────────────────────────────────────────────────

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

                if (!copied) { error = "Не удалось скопировать в System32. Нажмите «Да» в диалоге UAC."; return false; }

                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
                if (key != null)
                {
                    key.SetValue("SCRNSAVE.EXE", scrPath);
                    key.SetValue("ScreenSaveActive", "1");
                    key.SetValue("ScreenSaveIsSecure", "0");
                }
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
                var val = key?.GetValue("SCRNSAVE.EXE") as string;
                return val?.Contains("PhotoFrame", StringComparison.OrdinalIgnoreCase) == true;
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

        // ─── Autostart (HKCU\Run — no UAC required) ──────────────────────────────

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// Enables or disables autostart. Stores full quoted exe path.
        /// HKCU\Run does NOT require admin rights.
        /// </summary>
        public static void SetAutostart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;
                if (enable)
                {
                    // Full quoted path ensures spaces are handled correctly
                    string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    key.SetValue("PhotoFrame", $"\"{exe}\"");
                }
                else
                {
                    key.DeleteValue("PhotoFrame", throwOnMissingValue: false);
                }
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

        // ─── Sleep prevention ────────────────────────────────────────────────────

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

        // ─── Power timeouts ─────────────────────────────────────────────────────

        [DllImport("powrprof.dll")]
        private static extern bool PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")]
        private static extern bool PowerWriteACValueIndex(IntPtr root, ref Guid scheme,
            ref Guid sub, ref Guid setting, uint val);
        [DllImport("powrprof.dll")]
        private static extern bool PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        private static readonly Guid GUID_SLEEP_SUB   = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
        private static readonly Guid GUID_STANDBY     = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
        private static readonly Guid GUID_DISPLAY_SUB = new("7516B95F-F776-4464-8C53-06167F40CC99");
        private static readonly Guid GUID_MONITOR_OFF = new("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");

        public static bool SetPowerTimeouts(int monitorSec, int sleepSec)
        {
            try
            {
                if (!PowerGetActiveScheme(IntPtr.Zero, out IntPtr p)) return false;
                var scheme = (Guid)(Marshal.PtrToStructure(p, typeof(Guid)) ?? Guid.Empty);
                LocalFree(p);
                var dSub = GUID_DISPLAY_SUB; var dSet = GUID_MONITOR_OFF;
                var sSub = GUID_SLEEP_SUB;   var sSet = GUID_STANDBY;
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref dSub, ref dSet, (uint)monitorSec);
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sSub, ref sSet, (uint)sleepSec);
                PowerSetActiveScheme(IntPtr.Zero, ref scheme);
                return true;
            }
            catch { return false; }
        }

        // ─── ClickOnce (.NET 8 via env vars) ────────────────────────────────────

        public static bool IsClickOnceDeployed =>
            Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed")
                ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        public static string? ClickOnceCurrentVersion =>
            Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion");

        public static string? ClickOnceUpdateLocation =>
            Environment.GetEnvironmentVariable("ClickOnce_UpdateLocation");

        public static string GetClickOnceInfo()
        {
            if (!IsClickOnceDeployed)
                return "Приложение запущено напрямую (не через ClickOnce).\n" +
                       "Установите через ClickOnce для автообновлений.";
            return $"ClickOnce активен.\nВерсия: {ClickOnceCurrentVersion ?? "?"}\n" +
                   $"Источник обновлений: {ClickOnceUpdateLocation ?? "не задан"}";
        }

        // ─── GitHub update check ─────────────────────────────────────────────────

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

                const string key = "\"tag_name\"";
                int idx = json.IndexOf(key, StringComparison.Ordinal);
                if (idx < 0) return (null, false);
                int s = json.IndexOf('"', idx + key.Length + 1);
                int e = json.IndexOf('"', s + 1);
                if (s < 0 || e <= s) return (null, false);
                string tag = json.Substring(s + 1, e - s - 1);
                string numStr = tag.TrimStart('v', 'V').Trim();
                bool isNewer = System.Version.TryParse(numStr, out var latest)
                              && latest > currentVersion;
                return (tag, isNewer);
            }
            catch { return (null, false); }
        }

        public static void OpenGitHub()
        {
            try { Process.Start(new ProcessStartInfo(
                "https://github.com/ArtemITuser/PhotoFrame") { UseShellExecute=true }); }
            catch { }
        }

        public static void OpenGitHubReleases()
        {
            try { Process.Start(new ProcessStartInfo(
                "https://github.com/ArtemITuser/PhotoFrame/releases") { UseShellExecute=true }); }
            catch { }
        }

        // ─── Private helpers ─────────────────────────────────────────────────────

        private static bool ElevatedCopy(string src, string dst)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c copy /y \"{src}\" \"{dst}\"")
                { UseShellExecute=true, Verb="runas", WindowStyle=ProcessWindowStyle.Hidden };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(12_000);
                return File.Exists(dst);
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch { return false; }
        }
    }
}
