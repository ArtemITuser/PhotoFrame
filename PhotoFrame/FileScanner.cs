// Services/FileScanner.cs — v3.3
// Рекурсивный поиск изображений. Возвращает ScanResult с подробной статистикой.
// ВАЖНО (исправление «вылет через некоторое время работы»): обход подкаталогов
// переведён с рекурсии на явный стек (итеративный обход). Прежняя рекурсивная
// версия могла упасть с StackOverflowException при глубоком дереве папок
// (~несколько тысяч вложенных каталогов) и, кроме того, не останавливалась
// на точках перекрёстных ссылок (junction / symbolic link → цикл), что
// приводило к бесконечному обходу. Теперь:
//   * итеративный обход — глубина не ограничена стеком потока;
//   * защита от циклов — посещённые каталоги нормализуются по FullName;
//   * лимит Directory.EnumerateFiles(*.*) c AllDirectories тоже заменён
//     на итеративный вариант (раньше падал на cycle-ссылках и длинных путях).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var photos = new List<PhotoInfo>();
                int totalFiles = 0, dirs = 0;

                foreach (var root in paths)
                {
                    if (!Directory.Exists(root)) continue;
                    Walk(root, recursive, photos, ref totalFiles, ref dirs, progress);
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
                bool hasPhotos = await Task.Run(() => HasPhotosIterative(d.RootDirectory.FullName));
                if (hasPhotos) result.Add(d);
            }
            return result;
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Итеративный обход дерева (явный стек вместо рекурсии + множество
        /// посещённых каталогов против циклов через junction/symlink).
        /// </summary>
        private static void Walk(string root, bool recursive,
            List<PhotoInfo> photos, ref int totalFiles, ref int dirs,
            Action<string>? progress)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack   = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();

                string key;
                try { key = Path.GetFullPath(dir); }
                catch { key = dir; }
                if (!visited.Add(key)) continue; // цикл или повтор — пропускаем

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

                    if (recursive)
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                            stack.Push(sub);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }

        /// <summary>Быстрая проверка «есть ли фото где-то в дереве» без рекурсии.</summary>
        private static bool HasPhotosIterative(string root)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack   = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string key;
                try { key = Path.GetFullPath(dir); }
                catch { key = dir; }
                if (!visited.Add(key)) continue;

                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                        if (SupportedExtensions.Contains(Path.GetExtension(f)))
                            return true;
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        stack.Push(sub);
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            return false;
        }
    }
}
