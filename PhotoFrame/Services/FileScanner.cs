// Services/FileScanner.cs — v3.2
// Рекурсивный поиск изображений. Возвращает ScanResult с подробной статистикой.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    /// <summary>Результат сканирования с раздельной статистикой.</summary>
    public class ScanResult
    {
        /// <summary>Найденные фотографии (индексированные).</summary>
        public List<PhotoInfo> Photos { get; init; } = new();
        /// <summary>Всего файлов проверено (все типы).</summary>
        public int TotalFilesScanned { get; init; }
        /// <summary>Папок просмотрено.</summary>
        public int DirectoriesScanned { get; init; }
    }

    public static class FileScanner
    {
        private static readonly HashSet<string> SupportedExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };

        /// <summary>
        /// Асинхронное сканирование. Возвращает ScanResult с фото и статистикой.
        /// </summary>
        public static Task<ScanResult> ScanAsync(
            IEnumerable<string> paths,
            bool recursive,
            Action<string>? progress = null)
        {
            return Task.Run(() =>
            {
                var photos = new List<PhotoInfo>();
                int totalFiles = 0, dirs = 0;
                var option = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                foreach (var root in paths)
                {
                    if (!Directory.Exists(root)) continue;
                    ScanDir(root, option, photos, ref totalFiles, ref dirs, progress);
                }

                return new ScanResult
                {
                    Photos            = photos,
                    TotalFilesScanned = totalFiles,
                    DirectoriesScanned = dirs
                };
            });
        }

        /// <summary>Список всех готовых логических дисков.</summary>
        public static List<DriveInfo> GetAvailableDrives()
            => DriveInfo.GetDrives().Where(d => d.IsReady).ToList();

        /// <summary>Список съёмных носителей (USB/SD) с фотографиями.</summary>
        public static async Task<List<DriveInfo>> GetRemovableWithPhotosAsync()
        {
            var removable = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Removable)
                .ToList();

            var result = new List<DriveInfo>();
            foreach (var d in removable)
            {
                bool hasPhotos = await Task.Run(() =>
                {
                    try
                    {
                        return Directory.EnumerateFiles(
                            d.RootDirectory.FullName, "*.*",
                            SearchOption.AllDirectories)
                            .Any(f => SupportedExtensions.Contains(
                                Path.GetExtension(f)));
                    }
                    catch { return false; }
                });
                if (hasPhotos) result.Add(d);
            }
            return result;
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        private static void ScanDir(string dir, SearchOption option,
            List<PhotoInfo> photos, ref int totalFiles, ref int dirs,
            Action<string>? progress)
        {
            try
            {
                progress?.Invoke(dir);
                dirs++;

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    totalFiles++;
                    if (SupportedExtensions.Contains(Path.GetExtension(file)))
                        photos.Add(new PhotoInfo
                        {
                            FilePath  = file,
                            Directory = Path.GetDirectoryName(file) ?? dir
                        });
                }

                if (option == SearchOption.AllDirectories)
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        try { ScanDir(sub, option, photos, ref totalFiles, ref dirs, progress); }
                        catch (UnauthorizedAccessException) { }
                        catch (IOException) { }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }
}
