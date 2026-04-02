// Helpers/WindowHelper.cs — v3.2
// Прозрачность/размытие:
//   Win11 22H2+ → Mica (DWMWA_SYSTEMBACKDROP_TYPE = 2)
//   Win11 21H2   → Mica legacy (DWMWA_MICA_EFFECT = 1)
//   Win10        → Acrylic blur via SetWindowCompositionAttribute (undocumented API)
//   Fallback     → DWM glass (extend frame into client)
// DWM заголовок (тёмный/светлый) работает на Win10+.

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PhotoFrame.Helpers
{
    public static class WindowHelper
    {
        // ─── DWM constants ────────────────────────────────────────────────────────
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE     = 38;  // Win11 22H2+
        private const int DWMWA_MICA_EFFECT             = 1029; // Win11 21H2

        private const int DWM_SYSTEMBACKDROP_MICA   = 2;
        private const int DWM_SYSTEMBACKDROP_NONE   = 1;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(
            IntPtr hwnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left, Right, Top, Bottom; }

        // ─── Win10 Acrylic (SetWindowCompositionAttribute) ────────────────────────
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(
            IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int   Attribute;
            public IntPtr Data;
            public int   SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;  // AABBGGRR
            public int AnimationId;
        }

        private const int WCA_ACCENT_POLICY         = 19;
        private const int ACCENT_DISABLED           = 0;
        private const int ACCENT_ENABLE_BLURBEHIND  = 3;   // Win10 blur
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4; // Win10 1803+ Acrylic

        // ─── OS version helpers ────────────────────────────────────────────────────
        private static readonly Version _osVer = Environment.OSVersion.Version;
        private static bool IsWin11     => _osVer.Build >= 22000;
        private static bool IsWin1022H2 => _osVer.Build >= 22621;

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Применяет наилучший доступный эффект прозрачности:
        /// Mica (Win11) → Acrylic (Win10 1803+) → Aero Blur → Fallback.
        /// Возвращает имя применённого эффекта.
        /// </summary>
        public static string TryApplyMica(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return "none";

                if (IsWin11)
                {
                    // Win11: Mica через DWM
                    if (IsWin1022H2 && TrySetAttr(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWM_SYSTEMBACKDROP_MICA))
                        return "mica";
                    if (TrySetAttr(hwnd, DWMWA_MICA_EFFECT, 1))
                        return "mica-legacy";
                }

                // Win10: Acrylic через SetWindowCompositionAttribute
                // Нужно чтобы Window.Background содержал прозрачность
                if (TryApplyAcrylic(hwnd, window))
                    return "acrylic";

                // Fallback: расширяем DWM frame на весь клиент
                var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                return "glass";
            }
            catch { return "none"; }
        }

        /// <summary>Убирает эффект Mica/Acrylic.</summary>
        public static void RemoveMica(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return;

                if (IsWin11)
                    TrySetAttr(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWM_SYSTEMBACKDROP_NONE);

                // Отключаем Acrylic
                ApplyAccentPolicy(hwnd, ACCENT_DISABLED, 0, 0);

                // Убираем расширение frame
                var margins = new MARGINS { Left = 0, Right = 0, Top = 0, Bottom = 0 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
            }
            catch { }
        }

        /// <summary>Тёмный/светлый заголовок окна (кнопки свернуть/закрыть).</summary>
        public static void SetTitleBarDarkMode(Window window, bool dark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return;
                int value = dark ? 1 : 0;
                // На Win11 используем атрибут 20, на Win10 < 20H1 — 19
                if (!TrySetAttr(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, value))
                    TrySetAttr(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, value);
            }
            catch { }
        }

        /// <summary>Системная тёмная тема (HKCU\...\Personalize).</summary>
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

        // ─── Private ──────────────────────────────────────────────────────────────

        private static bool TrySetAttr(IntPtr hwnd, int attr, int value)
        {
            int hr = DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));
            return hr == 0; // S_OK
        }

        /// <summary>
        /// Win10 Acrylic через SetWindowCompositionAttribute.
        /// Требует WindowStyle=None + AllowsTransparency=False (или частично прозрачный фон).
        /// Полупрозрачный цвет задаём сами (тёмный/светлый в зависимости от темы).
        /// </summary>
        private static bool TryApplyAcrylic(IntPtr hwnd, Window window)
        {
            try
            {
                // Определяем цвет оверлея (AABBGGRR) — полупрозрачный тёмный/светлый
                bool dark = IsSystemDarkMode();
                // 0xCC = 80% opacity overlay
                int gradientColor = dark
                    ? unchecked((int)0xCC1A1A1A)  // тёмный: #1A1A1A с 80% alpha
                    : unchecked((int)0xCCF0F0F0); // светлый: #F0F0F0 с 80% alpha

                // Пробуем Acrylic (Win10 1803+, build 17134)
                if (_osVer.Build >= 17134)
                {
                    bool ok = ApplyAccentPolicy(hwnd, ACCENT_ENABLE_ACRYLICBLURBEHIND,
                        0x02, gradientColor);
                    if (ok) return true;
                }

                // Fallback: просто blur без цвета
                return ApplyAccentPolicy(hwnd, ACCENT_ENABLE_BLURBEHIND, 0, 0);
            }
            catch { return false; }
        }

        private static bool ApplyAccentPolicy(IntPtr hwnd, int accentState,
            int accentFlags, int gradientColor)
        {
            var policy = new AccentPolicy
            {
                AccentState   = accentState,
                AccentFlags   = accentFlags,
                GradientColor = gradientColor,
                AnimationId   = 0
            };
            int policySize = Marshal.SizeOf<AccentPolicy>();
            IntPtr policyPtr = Marshal.AllocHGlobal(policySize);
            try
            {
                Marshal.StructureToPtr(policy, policyPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute  = WCA_ACCENT_POLICY,
                    Data       = policyPtr,
                    SizeOfData = policySize
                };
                int result = SetWindowCompositionAttribute(hwnd, ref data);
                return result == 1; // TRUE
            }
            finally
            {
                Marshal.FreeHGlobal(policyPtr);
            }
        }
    }
}
