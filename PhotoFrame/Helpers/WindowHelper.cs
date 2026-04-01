// Helpers/WindowHelper.cs
// Применяет эффект Mica (Windows 11) или Acrylic Blur (Windows 10)
// к WPF-окну через Win32 DWM API.
// На системах, где эффект недоступен, gracefully возвращается к обычному фону.

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PhotoFrame.Helpers
{
    public static class WindowHelper
    {
        // ─── DWM константы ────────────────────────────────────────────────────────
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE     = 38;  // Windows 11 22H2+
        private const int DWMWA_MICA_EFFECT             = 1029; // Windows 11 21H2+

        private const int DWM_SYSTEMBACKDROP_AUTO  = 0;
        private const int DWM_SYSTEMBACKDROP_NONE  = 1;
        private const int DWM_SYSTEMBACKDROP_MICA  = 2;
        private const int DWM_SYSTEMBACKDROP_ACRYLIC = 3;

        // ─── P/Invoke ─────────────────────────────────────────────────────────────
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(
            IntPtr hwnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left, Right, Top, Bottom;
        }

        // ─── Публичные методы ─────────────────────────────────────────────────────

        /// <summary>
        /// Пытается применить эффект Mica (или Acrylic как запасной вариант).
        /// Возвращает true, если эффект был успешно применён.
        /// </summary>
        public static bool TryApplyMica(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return false;

                // Windows 11 22H2+ — SYSTEMBACKDROP_TYPE (Mica)
                if (TrySetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWM_SYSTEMBACKDROP_MICA))
                    return true;

                // Windows 11 21H2 — устаревший MICA_EFFECT
                if (TrySetAttribute(hwnd, DWMWA_MICA_EFFECT, 1))
                    return true;

                // Fallback: расширяем рамку DWM на весь клиент (стекло Aero-Glass)
                var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Убирает эффект Mica и возвращает стандартный фон.
        /// </summary>
        public static void RemoveMica(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return;

                TrySetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWM_SYSTEMBACKDROP_NONE);
            }
            catch { /* игнорируем */ }
        }

        /// <summary>
        /// Устанавливает режим тёмного заголовка окна (caption bar).
        /// </summary>
        public static void SetTitleBarDarkMode(Window window, bool dark)
        {
            try
            {
                var hwnd  = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd == IntPtr.Zero) return;

                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            }
            catch { /* не поддерживается на старых ОС */ }
        }

        /// <summary>
        /// Проверяет, установлена ли тёмная тема в системе,
        /// через реестр HKCU\...\Personalize\AppsUseLightTheme.
        /// </summary>
        public static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

                var value = key?.GetValue("AppsUseLightTheme");
                return value is int i && i == 0;
            }
            catch
            {
                return false; // предполагаем светлую тему как безопасный дефолт
            }
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        private static bool TrySetAttribute(IntPtr hwnd, int attr, int value)
        {
            int hr = DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));
            return hr >= 0; // S_OK или S_FALSE
        }
    }
}
