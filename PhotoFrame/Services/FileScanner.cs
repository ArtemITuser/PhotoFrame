// Services/FileScanner.cs
// Рекурсивно собирает список фотографий из выбранных директорий/томов.
// Обрабатывает ошибки доступа без прерывания сканирования.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public static class FileScanner
    {
        // Поддерживаемые расширения изображений
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp"
        };

        /// <summary>
        /// Асинхронно сканирует все указанные пути и возвращает список PhotoInfo.
        /// Метаданные EXIF читаются lazily — только при показе фото.
        /// </summary>
        /// <param name="paths">Пути к директориям или логическим дискам (например, "D:\").</param>
        /// <param name="recursive">Включать ли подпапки.</param>
        /// <param name="progress">Опциональный callback прогресса (путь текущей папки).</param>
        public static Task<List<PhotoInfo>> ScanAsync(
            IEnumerable<string> paths,
            bool recursive,
            Action<string>? progress = null)
        {
            return Task.Run(() =>
            {
                var results = new List<PhotoInfo>();
                var option = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                foreach (var root in paths)
                {
                    if (!Directory.Exists(root) && !IsLogicalDrive(root))
                        continue;

                    // При сканировании тома берём его корень
                    string scanRoot = IsLogicalDrive(root) ? root : root;

                    ScanDirectory(scanRoot, option, results, progress);
                }

                return results;
            });
        }

        /// <summary>
        /// Возвращает список всех логических дисков в системе,
        /// доступных для чтения.
        /// </summary>
        public static List<DriveInfo> GetAvailableDrives()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .ToList();
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        private static void ScanDirectory(
            string dirPath,
            SearchOption option,
            List<PhotoInfo> results,
            Action<string>? progress)
        {
            try
            {
                progress?.Invoke(dirPath);

                // Сначала файлы в текущей папке
                foreach (var file in Directory.EnumerateFiles(dirPath))
                {
                    if (SupportedExtensions.Contains(Path.GetExtension(file)))
                    {
                        results.Add(new PhotoInfo
                        {
                            FilePath  = file,
                            Directory = Path.GetDirectoryName(file) ?? dirPath
                        });
                    }
                }

                // Затем рекурсивно подпапки (по одной, чтобы перехватывать ошибки доступа)
                if (option == SearchOption.AllDirectories)
                {
                    foreach (var sub in Directory.EnumerateDirectories(dirPath))
                    {
                        try { ScanDirectory(sub, option, results, progress); }
                        catch (UnauthorizedAccessException) { /* пропускаем недоступные папки */ }
                        catch (IOException) { /* пропускаем папки с ошибками ввода-вывода */ }
                    }
                }
            }
            catch (UnauthorizedAccessException) { /* нет доступа к корневой папке — пропуск */ }
            catch (IOException) { /* ошибка чтения — пропуск */ }
        }

        private static bool IsLogicalDrive(string path)
        {
            return path.Length <= 3 && path.EndsWith("\\") ||
                   path.Length == 2 && path[1] == ':';
        }
    }
}
