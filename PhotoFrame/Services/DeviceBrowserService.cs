// Services/DeviceBrowserService.cs — v1.0 (build 52)
//
// Просмотр и импорт фото с иерархических ФС подключённых по USB устройств:
// Windows 10 Mobile (Lumia, MTP), Android (MTP), iOS (Apple Photos AFC/MTP).
// Эти устройства видны в "Этот компьютер" как именованные узлы БЕЗ буквы
// диска — обычные System.IO.* API их не видят. Используется поздне-связанный
// COM-интерфейс Shell.Application (доступен на любой Windows, без NuGet).
//
// Архитектура:
//   1. GetConnectedDevices()      — список устройств под "Этот компьютер"
//      (фильтр: не DriveInfo, т.е. нет буквы диска → портативное устройство)
//   2. GetDeviceTree(device)      — рекурсивное дерево папок для ПРЕДПРОСМОТРА
//      (используется в UI как TreeView, без копирования файлов)
//   3. ImportPhotosAsync(...)     — копирует фото из выбранной папки устройства
//      через Shell COM CopyHere() в локальный кеш
//      (%LocalAppData%\PhotoFrame\DeviceImports\{Device}\{Folder}),
//      который ЗАТЕМ добавляется в SelectedPaths как обычный путь — это
//      единственный надёжный способ дать FileScanner работать с MTP-фото
//      в WPF/.NET 8 без полной реализации WPD API.
//
// Ограничения: Shell COM CopyHere — асинхронная операция без явного await,
// поэтому используется поллинг количества файлов в целевой папке с таймаутом.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoFrame.Services
{
    public class DeviceNode
    {
        public string Name      { get; init; } = "";
        /// <summary>Внутренний shell-путь устройства (НЕ файловый путь!).</summary>
        public string ShellPath { get; init; } = "";
        public bool   IsFolder  { get; init; }
        public List<DeviceNode> Children { get; } = new();
    }

    public enum DeviceKind { Unknown, WindowsMobile, Android, Apple }

    public static class DeviceBrowserService
    {
        private static readonly string[] _photoExts =
            { ".jpg",".jpeg",".png",".bmp",".gif",".tiff",".tif",".webp",".heic" };

        /// <summary>
        /// Возвращает список подключённых портативных устройств (без буквы
        /// диска) под "Этот компьютер" — кандидаты на Lumia/Android/iOS.
        /// Обычные USB-флешки/HDD с буквой диска сюда не попадают (для них
        /// уже есть "+ Диск" в источниках).
        /// </summary>
        public static List<DeviceNode> GetConnectedDevices()
        {
            var result = new List<DeviceNode>();
            dynamic? shell = null;
            try
            {
                shell = CreateShell();
                if (shell == null) return result;

                // ssfDRIVES = 17 → "Этот компьютер"
                dynamic myComputer = shell.NameSpace(17);
                if (myComputer == null) return result;

                foreach (var itemObj in myComputer.Items())
                {
                    dynamic item = itemObj;
                    try
                    {
                        string name = item.Name?.ToString() ?? "";
                        string path = SafeGetPath(item);

                        // DriveInfo-резолвимые пути (C:\, D:\...) пропускаем —
                        // это обычные диски, не MTP-устройства.
                        bool looksLikeDrive = path.Length >= 2 && path[1] == ':';
                        if (looksLikeDrive) continue;
                        if (string.IsNullOrEmpty(path)) continue;

                        result.Add(new DeviceNode { Name = name, ShellPath = path, IsFolder = true });
                    }
                    finally { ReleaseCom(item); }
                }
                ReleaseCom(myComputer);
            }
            catch { /* COM недоступен или устройств нет */ }
            finally { ReleaseCom(shell); }
            return result;
        }

        /// <summary>
        /// Угадывает тип устройства по имени для отображения иконки/подсказки.
        /// </summary>
        public static DeviceKind GuessKind(string deviceName)
        {
            string n = deviceName.ToLowerInvariant();
            if (n.Contains("lumia") || n.Contains("windows phone") || n.Contains("mobile"))
                return DeviceKind.WindowsMobile;
            if (n.Contains("android") || n.Contains("samsung") || n.Contains("xiaomi")
                || n.Contains("huawei") || n.Contains("pixel") || n.Contains("oneplus"))
                return DeviceKind.Android;
            if (n.Contains("iphone") || n.Contains("ipad") || n.Contains("apple"))
                return DeviceKind.Apple;
            return DeviceKind.Unknown;
        }

        /// <summary>
        /// Строит дерево подпапок устройства до заданной глубины — для
        /// предпросмотра в UI (TreeView) без копирования файлов.
        /// </summary>
        public static DeviceNode? GetDeviceTree(DeviceNode device, int maxDepth = 3)
        {
            dynamic? shell = null;
            try
            {
                shell = CreateShell();
                if (shell == null) return null;
                dynamic? folder = ResolveFolder(shell, device.ShellPath);
                if (folder == null) return device;

                PopulateChildren(device, folder, maxDepth, 0);
                ReleaseCom(folder);
                return device;
            }
            catch { return device; }
            finally { ReleaseCom(shell); }
        }

        private static void PopulateChildren(DeviceNode node, dynamic folder, int maxDepth, int depth)
        {
            if (depth >= maxDepth) return;
            try
            {
                foreach (var childObj in folder.Items())
                {
                    dynamic child = childObj;
                    try
                    {
                        bool isFolder = child.IsFolder;
                        if (!isFolder) continue; // дерево показывает только папки

                        var childNode = new DeviceNode
                        {
                            Name      = child.Name?.ToString() ?? "",
                            ShellPath = SafeGetPath(child),
                            IsFolder  = true
                        };
                        node.Children.Add(childNode);

                        dynamic subFolder = child.GetFolder;
                        if (subFolder != null)
                        {
                            PopulateChildren(childNode, subFolder, maxDepth, depth + 1);
                            ReleaseCom(subFolder);
                        }
                    }
                    finally { ReleaseCom(child); }
                }
            }
            catch { /* часть устройств не даёт рекурсивный доступ — пропускаем */ }
        }

        /// <summary>
        /// Подсчитывает примерное количество фото в папке устройства (по
        /// расширению имени файла) — используется в UI как индикатор перед
        /// импортом, без полного копирования.
        /// </summary>
        public static int EstimatePhotoCount(DeviceNode folderNode)
        {
            dynamic? shell = null;
            try
            {
                shell = CreateShell();
                if (shell == null) return -1;
                dynamic? folder = ResolveFolder(shell, folderNode.ShellPath);
                if (folder == null) return -1;
                int count = 0;
                foreach (var itemObj in folder.Items())
                {
                    dynamic item = itemObj;
                    try
                    {
                        string name = item.Name?.ToString() ?? "";
                        if (_photoExts.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                            count++;
                    }
                    finally { ReleaseCom(item); }
                }
                ReleaseCom(folder);
                return count;
            }
            catch { return -1; }
            finally { ReleaseCom(shell); }
        }

        /// <summary>
        /// Импортирует (копирует) фото из папки устройства в локальный кеш
        /// через Shell COM CopyHere, затем возвращает путь к локальной копии
        /// — этот путь и нужно добавить в AppSettings.SelectedPaths, чтобы
        /// FileScanner мог его сканировать обычным образом.
        /// </summary>
        public static async Task<string?> ImportPhotosAsync(
            DeviceNode folderNode, IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            try
            {
                string safeDeviceName = SanitizeFileName(folderNode.Name);
                string destDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoFrame", "DeviceImports", safeDeviceName);
                Directory.CreateDirectory(destDir);

                progress?.Report($"Подключение к {folderNode.Name}…");

                return await Task.Run(() =>
                {
                    dynamic? shell = null;
                    dynamic? srcFolder = null;
                    dynamic? srcItems = null;
                    dynamic? destFolderItem = null;
                    try
                    {
                        shell = CreateShell();
                        if (shell == null) return null;

                        srcFolder = ResolveFolder(shell, folderNode.ShellPath);
                        if (srcFolder == null) return null;

                        destFolderItem = shell.NameSpace(destDir);
                        if (destFolderItem == null) return null;

                        int before = SafeCountFiles(destDir);
                        progress?.Report("Копирование файлов…");

                        // FOF_NO_UI = 4 (без диалогов прогресса), CopyHere асинхронен
                        const int FOF_NO_UI = 4;
                        srcItems = srcFolder.Items();
                        destFolderItem.CopyHere(srcItems, FOF_NO_UI);

                        // Поллинг: ждём пока число файлов перестанет расти (макс. 5 мин)
                        int stableCount = 0, lastCount = before;
                        var deadline = DateTime.UtcNow.AddMinutes(5);
                        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                        {
                            Thread.Sleep(1500);
                            int now = SafeCountFiles(destDir);
                            if (now == lastCount) { stableCount++; if (stableCount >= 3) break; }
                            else { stableCount = 0; lastCount = now; }
                            progress?.Report($"Скопировано файлов: {now}");
                        }

                        return destDir;
                    }
                    finally
                    {
                        ReleaseCom(srcItems);
                        ReleaseCom(destFolderItem);
                        ReleaseCom(srcFolder);
                        ReleaseCom(shell);
                    }
                }, ct);
            }
            catch { return null; }
        }

        // ─── Shell COM helpers (поздне-связанный, без COM-ссылки в csproj) ──────

        private static dynamic? CreateShell()
        {
            try
            {
                var t = Type.GetTypeFromProgID("Shell.Application");
                return t != null ? Activator.CreateInstance(t) : null;
            }
            catch { return null; }
        }

        private static dynamic? ResolveFolder(dynamic shell, string shellPath)
        {
            try { return shell.NameSpace(shellPath); }
            catch { return null; }
        }

        /// <summary>
        /// Освобождает RCW (Runtime Callable Wrapper) COM-объекта Shell.Application
        /// и его дочерних Folder/FolderItem. Без явного освобождения COM-обёртки
        /// живут до финализации GC — при частом открытии браузера устройств это
        /// может надолго удерживать хэндлы shell-namespace нативной стороной.
        /// Аудит (build 52): добавлено release для shell/folder-объектов во
        /// всех точках использования ниже.
        /// </summary>
        private static void ReleaseCom(object? comObject)
        {
            if (comObject == null) return;
            try { if (System.Runtime.InteropServices.Marshal.IsComObject(comObject)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(comObject); }
            catch { /* объект уже освобождён либо не COM — безопасно игнорировать */ }
        }

        private static string SafeGetPath(dynamic item)
        {
            try { return item.Path?.ToString() ?? ""; }
            catch { return ""; }
        }

        private static int SafeCountFiles(string dir)
        {
            try { return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length; }
            catch { return 0; }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Device" : name;
        }
    }
}
