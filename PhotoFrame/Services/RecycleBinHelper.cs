// Services/RecycleBinHelper.cs
// Удаление файла в Корзину через Shell32 SHFileOperation.
// Не требует Microsoft.VisualBasic — чистый P/Invoke.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PhotoFrame.Services
{
    public static class RecycleBinHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint   wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]   public bool   fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        private const uint   FO_DELETE         = 0x0003;
        private const ushort FOF_ALLOWUNDO      = 0x0040; // → Корзина
        private const ushort FOF_NOCONFIRMATION = 0x0010; // без лишних диалогов
        private const ushort FOF_SILENT         = 0x0004; // без прогресса

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOp);

        /// <summary>
        /// Перемещает файл в Корзину Windows.
        /// Возвращает true при успехе.
        /// </summary>
        public static bool SendToRecycleBin(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                // SHFileOperation требует двойного нуль-терминатора
                var op = new SHFILEOPSTRUCT
                {
                    hwnd   = IntPtr.Zero,
                    wFunc  = FO_DELETE,
                    pFrom  = filePath + '\0' + '\0',
                    pTo    = null!,
                    fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT)
                };
                int result = SHFileOperation(ref op);
                return result == 0 && !op.fAnyOperationsAborted;
            }
            catch { return false; }
        }

        /// <summary>
        /// Открывает Проводник Windows с выделенным файлом.
        /// </summary>
        public static void ShowInExplorer(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                else
                {
                    string? dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start("explorer.exe", $"\"{dir}\"");
                }
            }
            catch { }
        }
    }
}
