// Services/FileScanner.cs — UWP-порт v3.2 (логика 1:1 с WPF-версией)
// Рекурсивный поиск изображений. Возвращает ScanResult с подробной статистикой.
//
// Отличия от WPF (net8.0) версий — только в источнике путей:
//   * пути могут быть как «real path» (C:\..., \Storage\...), так и token
//     Windows.Storage (ms-appdata:///..., ms-appx:///...). Токены конвертируются
//     в IStorageItem через AccessConfiguration / ApplicationData;
//   * DriveInfo.GetDrives() недоступен в sandbox → съёмные носители ищутся
//     через Windows.Storage.KnownFolders.RemovableDevices (removableStorage capability).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using PhotoFrameUwp.Models;

namespace PhotoFrameUwp.Services
{
    /// <summary>Результат сканирования с раздельной статистикой.</summary>
    public class ScanResult
    {
        /// <summary>Найденные фотографии (индексированные).</summary>
        public List<PhotoInfo> Photos { get; set; } = new List<PhotoInfo>();
        /// <summary>Всего файлов проверено (все типы).</summary>
        public int TotalFilesScanned { get; set; }
        /// <summary>Папок просмотрено.</summary>
        public int DirectoriesScanned { get; set; }
    }

    public static class FileScanner
    {
        private static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };

        /// <summary>
        /// Асинхронное сканирование. Возвращает ScanResult с фото и статистикой.
        /// Логика обхода идентична WPF-версии (System.IO), т.к. broadFileSystemAccess
        /// + real-path разрешения дают прямой доступ к ФС на Desktop/Continuum.
        /// Для Mobile-токенов используется Storage-обход (см. ResolveToDirectoryAsync).
        /// </summary>
        public static async Task<ScanResult> ScanAsync(
            IEnumerable<string> paths,
            bool recursive,
            Action<string> progress = null)
        {
            var photos = new List<PhotoInfo>();
            int totalFiles = 0, dirs = 0;

            foreach (var root in paths)
            {
                var dir = await ResolveToDirectoryAsync(root);
                if (dir == null) continue;
                await ScanDirAsync(dir, recursive, photos,
                    ref totalFiles, ref dirs, progress);
            }

            return new ScanResult
            {
                Photos             = photos,
                TotalFilesScanned  = totalFiles,
                DirectoriesScanned = dirs
            };
        }

        /// <summary>Список всех готовых логических дисков (Desktop/Continuum).</summary>
        public static async Task<List<StorageFolder>> GetAvailableDrivesAsync()
        {
            try
            {
                var drives = await KnownFolders.RemovableDevices.GetFoldersAsync();
                return drives.ToList();
            }
            catch { return new List<StorageFolder>(); }
        }

        /// <summary>Съёмные носители (USB/SD) с фотографиями.</summary>
        public static async Task<List<StorageFolder>> GetRemovableWithPhotosAsync()
        {
            var result = new List<StorageFolder>();
            foreach (var d in await GetAvailableDrivesAsync())
            {
                bool hasPhotos = await HasPhotosAsync(d);
                if (hasPhotos) result.Add(d);
            }
            return result;
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Разрешает строку пути в StorageFolder: real path или storage-token.
        /// (Path.GetFullPath для реальных путей, GetFolderFromPathAsync для токенов.)
        /// </summary>
        public static async Task<StorageFolder> ResolveToDirectoryAsync(string pathOrToken)
        {
            if (string.IsNullOrWhiteSpace(pathOrToken)) return null;
            try
            {
                return await StorageFolder.GetFolderFromPathAsync(pathOrToken);
            }
            catch
            {
                try { return await StorageFolder.GetFolderFromPathAsync(
                        pathOrToken.TrimEnd('\\') + "\\"); }
                catch { return null; }
            }
        }

        private static async Task<bool> HasPhotosAsync(StorageFolder folder)
        {
            try
            {
                return await CheckTreeAsync(folder, depthLimited: true);
            }
            catch { return false; }
        }

        private static async Task<bool> CheckTreeAsync(StorageFolder folder, bool depthLimited)
        {
            try
            {
                var files = await folder.GetFilesAsync();
                if (files.Any(f => SupportedExtensions.Contains(f.FileType)))
                    return true;
                var subs = await folder.GetFoldersAsync();
                foreach (var s in subs)
                    if (await CheckTreeAsync(s, depthLimited)) return true;
            }
            catch { }
            return false;
        }

        private static async Task ScanDirAsync(StorageFolder dir, bool recursive,
            List<PhotoInfo> photos, ref int totalFiles, ref int dirs,
            Action<string> progress)
        {
            try
            {
                progress?.Invoke(dir.Path);
                dirs++;

                foreach (var file in await dir.GetFilesAsync())
                {
                    totalFiles++;
                    if (SupportedExtensions.Contains(file.FileType))
                        photos.Add(new PhotoInfo
                        {
                            FilePath  = file.Path,
                            Directory = GetParentPath(file.Path, dir.Path)
                        });
                }

                if (recursive)
                    foreach (var sub in await dir.GetFoldersAsync())
                        await ScanDirAsync(sub, true, photos,
                            ref totalFiles, ref dirs, progress);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (Exception) { /* sharing violation на SD — пропускаем папку */ }
        }

        private static string GetParentPath(string filePath, string fallbackDir)
        {
            try
            {
                int i = filePath.LastIndexOf('\\');
                if (i > 0) return filePath.Substring(0, i);
                i = filePath.LastIndexOf('/');
                if (i > 0) return filePath.Substring(0, i);
            }
            catch { }
            return fallbackDir;
        }
    }
}
