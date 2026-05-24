// Helpers/WindowHelper.cs — v3.7 (build 40)
// AllowsTransparency=True + WindowStyle=None.
// Win11 22H2+ (incl. 25H2): DWMWA_SYSTEMBACKDROP_TYPE = MICA (2)
// Win11 21H2:                DWMWA_MICA_EFFECT = 1
// Win10 1803-21H2 (17134+):  SetWindowCompositionAttribute Acrylic
// Fallback:                  DWM blur

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PhotoFrame.Helpers
{
    public static class WindowHelper
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE     = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE         = 38;
        private const int DWMWA_MICA_EFFECT                 = 1029;
        private const int DWMSBT_NONE                       = 1;
        private const int DWMSBT_MICA                       = 2;
        private const int DWMSBT_ACRYLIC                    = 3;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")]
        private static extern bool DwmIsCompositionEnabled(out bool enabled);

        // Win10 Acrylic (undocumented user32)
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(
            IntPtr hwnd, ref WCA_DATA data);

        [StructLayout(LayoutKind.Sequential)]
        private struct WCA_DATA
        { public int Attribute; public IntPtr Data; public int SizeOfData; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY
        { public int AccentState, AccentFlags, GradientColor, AnimationId; }

        private const int WCA_ACCENT_POLICY               = 19;
        private const int ACCENT_DISABLED                  = 0;
        private const int ACCENT_ENABLE_BLURBEHIND         = 3;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        private static readonly Version _os = Environment.OSVersion.Version;

        public  static bool IsWin11     => _os.Build >= 22000;
        private static bool IsWin1122H2 => _os.Build >= 22621;  // covers 25H2 (26100)
        private static bool IsWin1121H2 => _os.Build >= 22000 && _os.Build < 22621;

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Apply Mica (Win11) or Acrylic (Win10).
        /// Requires AllowsTransparency=True + Background=Transparent.
        /// Returns effect name applied.
        /// </summary>
        public static string TryApplyBackdrop(Window window, bool isDark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return "none";

                DwmIsCompositionEnabled(out bool comp);
                if (!comp) return "none";

                // Win11 22H2+ (build 22621+, includes 25H2 build 26100)
                if (IsWin1122H2)
                {
                    if (TryAttr(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MICA))
                    {
                        SetTransparent(window);
                        return "mica";
                    }
                    // Acrylic fallback on Win11
                    if (TryAttr(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_ACRYLIC))
                    {
                        SetTransparent(window);
                        return "acrylic-win11";
                    }
                }

                // Win11 21H2 (22000–22620)
                if (IsWin1121H2 && TryAttr(hwnd, DWMWA_MICA_EFFECT, 1))
                {
                    SetTransparent(window);
                    return "mica-legacy";
                }

                // Win10 1803+ (17134+) — Acrylic via SetWindowCompositionAttribute
                // GradientColor format: AABBGGRR
                if (_os.Build >= 17134)
                {
                    int gc = isDark
                        ? unchecked((int)0xBB1A1A1A)
                        : unchecked((int)0x88EEEEEE);

                    if (ApplyAccent(hwnd, ACCENT_ENABLE_ACRYLICBLURBEHIND, 0x02, gc))
                    {
                        SetTransparent(window);
                        return "acrylic-win10";
                    }
                }

                // Fallback: basic DWM blur
                if (ApplyAccent(hwnd, ACCENT_ENABLE_BLURBEHIND, 0, 0))
                {
                    SetTransparent(window);
                    return "blur";
                }

                return "none";
            }
            catch { return "none"; }
        }

        public static void RemoveBackdrop(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return;
                if (IsWin11) TryAttr(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_NONE);
                ApplyAccent(hwnd, ACCENT_DISABLED, 0, 0);
                // Restore solid fallback
                bool dark = IsSystemDarkMode();
                window.Dispatcher.Invoke(() =>
                    window.Background = new SolidColorBrush(dark
                        ? Color.FromArgb(0xCC, 0x11, 0x11, 0x11)
                        : Color.FromArgb(0xCC, 0xF0, 0xF0, 0xF0)));
            }
            catch { }
        }

        public static void SetTitleBarDarkMode(Window window, bool dark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return;
                int v = dark ? 1 : 0;
                if (!TryAttr(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, v))
                    TryAttr(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, v);
            }
            catch { }
        }

        public static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int i && i == 0;
            }
            catch { return false; }
        }

        // ─── Private ─────────────────────────────────────────────────────────────

        private static void SetTransparent(Window w) =>
            w.Dispatcher.Invoke(() => w.Background = Brushes.Transparent);

        private static bool TryAttr(IntPtr hwnd, int attr, int v) =>
            DwmSetWindowAttribute(hwnd, attr, ref v, sizeof(int)) == 0;

        private static bool ApplyAccent(IntPtr hwnd, int state, int flags, int gc)
        {
            var p = new ACCENT_POLICY
                { AccentState=state, AccentFlags=flags, GradientColor=gc };
            int sz  = Marshal.SizeOf<ACCENT_POLICY>();
            var ptr = Marshal.AllocHGlobal(sz);
            try
            {
                Marshal.StructureToPtr(p, ptr, false);
                var d = new WCA_DATA
                    { Attribute=WCA_ACCENT_POLICY, Data=ptr, SizeOfData=sz };
                return SetWindowCompositionAttribute(hwnd, ref d) == 1;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
    }
}
