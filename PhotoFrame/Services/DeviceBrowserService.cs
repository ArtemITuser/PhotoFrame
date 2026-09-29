// Services/DeviceBrowserService.cs — v2.3 (build 57)
//
// build 57: импорт теперь по умолчанию идёт в "Изображения" пользователя
// (Pictures\{имя устройства}), а не в скрытую %LocalAppData% — см.
// GetDefaultImportDestination/ImportPhotosAsync ниже. Путь назначения
// теперь явно передаётся вызывающей стороной (Views/DeviceBrowserWindow.
// xaml.cs), которая также даёт скопировать его в буфер обмена кнопкой рядом.
//
// Просмотр и импорт фото с иерархических ФС подключённых по USB устройств:
// Windows Phone 8.x / Windows 10 Mobile (Lumia, MTP), Android (MTP),
// iOS (Apple Photos AFC/MTP), цифровые фотоаппараты (MTP или PTP). Эти
// устройства видны в "Этот компьютер" как именованные узлы БЕЗ буквы
// диска — обычные System.IO.* API их не видят. Используется поздне-
// связанный COM-интерфейс Shell.Application (доступен на любой Windows,
// без NuGet и без COM-ссылки в csproj).
//
// build 56 (объединение параллельно доработанной ветки) — самый значимый
// найденный баг был не в потоковой модели, а в одну строку раньше:
//   ГЛАВНАЯ ПРИЧИНА, по которой НИ ОДНО устройство не находилось (ни
//   Lumia 950, ни POCO M8 Pro 5G, ни iPhone 13 mini, ни Coolpix S33):
//   фильтр "это диск, пропускаем" проверял `path[1] == ':'`, рассчитывая
//   отсечь "C:\", "D:\" и т.п. Но у MTP/AFC-устройств Path — это shell
//   parsing path вида "::{20D04FE0-3AEA-...}\...", который НАЧИНАЕТСЯ С
//   ДВУХ ДВОЕТОЧИЙ — то есть тот же символ [1] тоже ':'! Проверка ложно
//   срабатывала на КАЖДОМ настоящем устройстве, и они все отбрасывались
//   как "диски" ещё до того, как до них доходила очередь показаться в
//   списке. Исправлено: дополнительно требуем, чтобы path[0] была буквой
//   (см. GetConnectedDevices ниже) — так отсекаются именно буквы дисков,
//   а не произвольные двоеточия в начале строки.
//
//   Второй фактор (потоковая модель) сохранён из более ранней доработки:
//   COM-работа выполняется на отдельном потоке с явным ApartmentState.STA,
//   а не через Task.Run (ThreadPool, по умолчанию MTA) — Shell.Application
//   и WPD-провайдеры пространства имён документированно менее надёжны на
//   MTA (тот же код, что и в explorer.exe, обычно исполняется на STA).
//
// ВАЖНО, честно: часть устройств физически МОГУТ не быть MTP/PTP вообще, а
// подключаться как обычный USB Mass Storage (буква диска) — простые
// компактные фотоаппараты нередко так и работают; для них уже есть
// "+ Диск" в источниках, а не этот браузер устройств. iPhone дополнительно
// требует установленного Apple Mobile Device Support (часть iTunes либо
// отдельное приложение "Apple Devices" из Microsoft Store) — без него
// Windows не показывает iPhone как просматриваемое устройство нигде, даже
// в Проводнике. Ни то, ни другое не устраняется кодом этого файла.
//
// Архитектура:
//   1. GetConnectedDevices()      — список устройств под "Этот компьютер"
//      (фильтр: не DriveInfo, т.е. нет буквы диска → портативное устройство)
//   2. GetChildren(node)          — ОДИН уровень подпапок узла, для
//      ленивого раскрытия TreeView (см. Views/DeviceBrowserWindow.xaml.cs)
//   3. ImportPhotosAsync(...)     — копирует фото из выбранной папки устройства
//      через Shell COM CopyHere() в локальный кеш
//      (%LocalAppData%\PhotoFrame\DeviceImports\{Device}\{Folder}),
//      который ЗАТЕМ добавляется в SelectedPaths как обычный путь — это
//      единственный надёжный способ дать FileScanner работать с MTP-фото
//      в WPF/.NET 8 без полной реализации WPD API.
//
// build 53 — исправление иерархического просмотра (архитектура
// ResolveNode/NamePath не менялась в build 55/56):
//   Раньше GetDeviceTree(device, maxDepth: 3) РЕКУРСИВНО и ЖАДНО обходил
//   дерево до фиксированной глубины 3 за один вызов, а EstimatePhotoCount/
//   ImportPhotosAsync повторно резолвили выбранную ВЛОЖЕННУЮ папку через
//   shell.NameSpace(node.ShellPath). Теперь КАЖДЫЙ DeviceNode хранит
//   RootDevice и NamePath, резолвинг вложенных узлов идёт через один
//   надёжный NameSpace() на корне + пошаговый обход .Items()/.GetFolder.
//   Дерево строится лениво, по одному уровню за раз (см. GetChildren).
//
// Ограничения: Shell COM CopyHere — асинхронная операция без явного await,
// поэтому используется поллинг количества файлов в целевой папке с таймаутом.
// Блокирующие вызовы COM (Items()/GetFolder/NameSpace) не поддерживают
// отмену на уровне ОС — GetConnectedDevices/GetChildren оборачиваются в
// таймаут (см. RunBlocking), чтобы "уснувшее"/не отвечающее устройство не
// подвешивало окно браузера навсегда; сам фоновый STA-поток при этом может
// продолжать ждать нативный вызов — это неустранимое ограничение
// позднего связывания COM, а не утечка (поток естественным образом
// завершится, когда/если вызов всё же вернёт управление; помечен
// IsBackground=true, поэтому не помешает закрытию процесса).

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
        /// <summary>
        /// Внутренний shell-путь (НЕ файловый путь!). Надёжно резолвится
        /// через NameSpace() ТОЛЬКО для корневых узлов устройства (см.
        /// RootDevice) — для вложенных узлов используйте NamePath.
        /// </summary>
        public string ShellPath { get; init; } = "";
        public bool   IsFolder  { get; init; }
        public List<DeviceNode> Children { get; } = new();

        /// <summary>Узел устройства (корень), из которого получен этот
        /// узел. Null, если этот узел САМ является корнем устройства.</summary>
        public DeviceNode? RootDevice { get; init; }

        /// <summary>Путь имён папок от RootDevice до этого узла (пусто для
        /// самого корня устройства).</summary>
        public IReadOnlyList<string> NamePath { get; init; } = Array.Empty<string>();

        /// <summary>Узел, чей ShellPath можно надёжно передать в
        /// shell.NameSpace() напрямую — сам узел, если он корень, иначе
        /// его RootDevice.</summary>
        public DeviceNode EffectiveRoot => RootDevice ?? this;

        /// <summary>Создаёт дочерний узел, наследующий RootDevice/NamePath
        /// от текущего — используется при построении одного уровня дерева.</summary>
        public DeviceNode MakeChild(string name, string shellPath, bool isFolder)
        {
            var childNamePath = new List<string>(NamePath) { name };
            return new DeviceNode
            {
                Name       = name,
                ShellPath  = shellPath,
                IsFolder   = isFolder,
                RootDevice = EffectiveRoot,
                NamePath   = childNamePath
            };
        }
    }

    public enum DeviceKind { Unknown, WindowsMobile, Android, Apple, Camera }

    public static class DeviceBrowserService
    {
        private static readonly string[] _photoExts =
            { ".jpg",".jpeg",".png",".bmp",".gif",".tiff",".tif",".webp",".heic" };

        /// <summary>Таймаут для отдельных блокирующих COM-обходов —
        /// защищает UI браузера устройств от "зависшего"/уснувшего
        /// устройства (см. заголовок файла).</summary>
        private static readonly TimeSpan ComTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Возвращает список подключённых портативных устройств (без буквы
        /// диска) под "Этот компьютер" — кандидаты на Lumia/Android/iOS.
        /// Обычные USB-флешки/HDD с буквой диска сюда не попадают (для них
        /// уже есть "+ Диск" в источниках).
        /// </summary>
        public static List<DeviceNode> GetConnectedDevices()
            => RunBlocking(() =>
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

                            // build 56 (объединение веток) — НАЙДЕННЫЙ, БОЛЕЕ
                            // ЗНАЧИМЫЙ баг, чем потоковая модель ниже: прежняя
                            // проверка `path[1] == ':'` должна была отсекать
                            // буквы дисков (C:\, D:\...), но MTP/AFC-устройства
                            // возвращают "parsing path" вида "::{20D04FE0-…}\…"
                            // — а У НЕГО символ [1] ТОЖЕ ':' (строка начинается
                            // с ДВУХ двоеточий)! В результате ВСЕ настоящие
                            // устройства ошибочно классифицировались как
                            // "диск" и отбрасывались этой же строкой — что,
                            // вероятно, было основной причиной, по которой ни
                            // одно устройство не находилось вообще. Теперь
                            // требуем, чтобы ПЕРВЫЙ символ был буквой — так
                            // отсекаются именно C:\/D:\..., но не "::{GUID}".
                            bool looksLikeDrive = path.Length >= 2
                                && char.IsLetter(path[0]) && path[1] == ':';
                            if (looksLikeDrive) continue;
                            if (string.IsNullOrEmpty(path)) continue;

                            // build 53 здесь стоял доп. фильтр по IsFileSystem —
                            // убран в build 55: при недостаточной определённости
                            // поведения конкретного провайдера устройства он мог
                            // случайно скрыть настоящий телефон/камеру (в
                            // частности, есть основания считать, что iPhone
                            // через протокол AFC отдаёт IsFileSystem=true, хотя
                            // является полноценным устройством с иерархической
                            // ФС — не просто диском). Библиотечные папки типа
                            // "Документы"/"Изображения" под "Этот компьютер"
                            // отсекаются уже ВЫШЕ — у них обычный файловый путь
                            // вида "C:\Users\...", который ловит проверка на
                            // букву диска, так что отдельный фильтр по
                            // IsFileSystem для этого не нужен.

                            result.Add(new DeviceNode { Name = name, ShellPath = path, IsFolder = true });
                        }
                        finally { ReleaseCom(item); }
                    }
                    ReleaseCom(myComputer);
                }
                catch { /* COM недоступен или устройств нет */ }
                finally { ReleaseCom(shell); }
                return result;
            }, new List<DeviceNode>());

        /// <summary>
        /// Угадывает тип устройства по имени для отображения иконки/подсказки.
        /// build 56: расширен список брендов (объединение веток) — теперь
        /// также распознаёт распространённые бренды камер отдельным видом
        /// DeviceKind.Camera. Не используется как фильтр видимости —
        /// только для иконки/сортировки.
        /// </summary>
        public static DeviceKind GuessKind(string deviceName)
        {
            string n = deviceName.ToLowerInvariant();
            if (n.Contains("lumia") || n.Contains("windows phone") || n.Contains("mobile"))
                return DeviceKind.WindowsMobile;
            if (n.Contains("nikon") || n.Contains("coolpix") || n.Contains("canon")
                || n.Contains("sony") || n.Contains("gopro") || n.Contains("fujifilm")
                || n.Contains("olympus") || n.Contains("panasonic") || n.Contains("camera"))
                return DeviceKind.Camera;
            if (n.Contains("android") || n.Contains("samsung") || n.Contains("xiaomi")
                || n.Contains("huawei") || n.Contains("pixel") || n.Contains("oneplus")
                || n.Contains("poco") || n.Contains("realme") || n.Contains("oppo")
                || n.Contains("redmi") || n.Contains("motorola") || n.Contains("vivo"))
                return DeviceKind.Android;
            if (n.Contains("iphone") || n.Contains("ipad") || n.Contains("apple"))
                return DeviceKind.Apple;
            return DeviceKind.Unknown;
        }

        /// <summary>
        /// Возвращает ОДИН уровень дочерних папок узла (устройства или
        /// вложенной папки) — для ленивого раскрытия TreeView. Глубина не
        /// ограничена: вызывающая сторона запрашивает следующий уровень при
        /// каждом реальном Expand, как обычный проводник файлов (см.
        /// Views/DeviceBrowserWindow.xaml.cs).
        /// </summary>
        public static List<DeviceNode> GetChildren(DeviceNode node)
            => RunBlocking(() =>
            {
                var result = new List<DeviceNode>();
                dynamic? shell = null;
                try
                {
                    shell = CreateShell();
                    if (shell == null) return result;
                    dynamic? folder = ResolveNode(shell, node);
                    if (folder == null) return result;

                    try
                    {
                        foreach (var childObj in folder.Items())
                        {
                            dynamic child = childObj;
                            try
                            {
                                bool isFolder = child.IsFolder;
                                if (!isFolder) continue; // дерево показывает только папки
                                string name = child.Name?.ToString() ?? "";
                                result.Add(node.MakeChild(name, SafeGetPath(child), true));
                            }
                            finally { ReleaseCom(child); }
                        }
                    }
                    catch { /* устройство не даёт доступ к этому уровню — вернём то, что есть */ }
                    ReleaseCom(folder);
                }
                catch { }
                finally { ReleaseCom(shell); }
                return result;
            }, new List<DeviceNode>());

        /// <summary>
        /// Подсчитывает примерное количество фото в папке устройства (по
        /// расширению имени файла) — используется в UI как индикатор перед
        /// импортом, без полного копирования.
        /// </summary>
        public static int EstimatePhotoCount(DeviceNode folderNode)
            => RunBlocking(() =>
            {
                dynamic? shell = null;
                dynamic? folder = null;
                try
                {
                    shell = CreateShell();
                    if (shell == null) return -1;
                    folder = ResolveNode(shell, folderNode);
                    if (folder == null) return -1;

                    // Считаем не только содержимое выбранной папки, но и
                    // фотографии во вложенных каталогах. Это соответствует
                    // IncludeSubdirectories=true у основного сканера и не
                    // вводит пользователя в заблуждение относительно
                    // фактического объёма импорта.
                    return CountPhotosRecursive(folder);
                }
                catch { return -1; }
                finally
                {
                    ReleaseCom(folder);
                    ReleaseCom(shell);
                }
            }, -1);

        private static int CountPhotosRecursive(dynamic folder)
        {
            int count = 0;
            dynamic? items = null;

            try
            {
                items = folder.Items();
                foreach (var itemObj in items)
                {
                    dynamic item = itemObj;
                    try
                    {
                        string name = item.Name?.ToString() ?? "";
                        if (_photoExts.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                        {
                            count++;
                            continue;
                        }

                        dynamic? childFolder = null;
                        try
                        {
                            if ((bool)item.IsFolder)
                                childFolder = item.GetFolder;
                        }
                        catch { }

                        if (childFolder != null)
                        {
                            count += CountPhotosRecursive(childFolder);
                            ReleaseCom(childFolder);
                        }
                    }
                    finally { ReleaseCom(item); }
                }
            }
            catch { /* отдельная недоступная ветка не обнуляет уже найденное */ }
            finally { ReleaseCom(items); }

            return count;
        }

        /// <summary>build 57: путь по умолчанию для импорта — Изображения
        /// пользователя, а не скрытая %LocalAppData%, чтобы импортированные
        /// фото сразу были видны в обычном Проводнике
        /// ("C:\Users\{имя}\Pictures\{имя устройства}").</summary>
        public static string GetDefaultImportDestination(DeviceNode device)
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                SanitizeFileName(device.EffectiveRoot.Name));

        /// <summary>Открывает системный диалог выбора папки Windows Explorer.</summary>
        public static string? PickLocalFolder(string currentPath = "")
            => RunBlocking(() =>
            {
                dynamic? shell = null;
                dynamic? browser = null;
                dynamic? initial = null;
                try
                {
                    shell = CreateShell();
                    if (shell == null) return null;
                    // BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE.
                    const int flags = 0x0001 | 0x0040;
                    // 17 = ssfDRIVES (Этот компьютер). Если есть текущий
                    // путь, используем его как корневой узел диалога.
                    object root = 17;
                    if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
                    {
                        initial = shell.NameSpace(currentPath);
                        if (initial != null) root = initial;
                    }
                    browser = shell.BrowseForFolder(0, "Выберите папку для сохранения фотографий", flags, root);
                    if (browser == null) return null;
                    try { return browser.Self.Path?.ToString(); } catch { return null; }
                }
                catch { return null; }
                finally
                {
                    ReleaseCom(browser);
                    ReleaseCom(initial);
                    ReleaseCom(shell);
                }
            }, (string?)null);

        /// <summary>
        /// Импортирует (копирует) фото из папки устройства в <paramref
        /// name="destDir"/> через Shell COM CopyHere, затем возвращает путь
        /// к локальной копии — этот путь и нужно добавить в
        /// AppSettings.SelectedPaths, чтобы FileScanner мог его сканировать
        /// обычным образом. build 57: destDir теперь ЯВНО передаётся
        /// вызывающей стороной (было — вычислялось внутри как скрытая
        /// %LocalAppData%\PhotoFrame\DeviceImports\...) — см.
        /// GetDefaultImportDestination для разумного значения по умолчанию,
        /// которое можно показать пользователю и дать изменить перед стартом.
        /// </summary>
        public static async Task<string?> ImportPhotosAsync(
            DeviceNode folderNode, string destDir, IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (folderNode == null || string.IsNullOrWhiteSpace(destDir)) return null;
            Directory.CreateDirectory(destDir);

            progress?.Report($"Подключение к {folderNode.Name}…");

            return await RunOnStaThreadAsync(() =>
            {
                dynamic? shell = null;
                dynamic? srcFolder = null;
                var jobs = new List<PhotoImportJob>();
                try
                {
                    shell = CreateShell();
                    if (shell == null) return null;
                    srcFolder = ResolveNode(shell, folderNode);
                    if (srcFolder == null) return null;
                    CollectPhotoJobs(srcFolder, string.Empty, jobs, ct);
                    if (jobs.Count == 0)
                    {
                        progress?.Report("Фотографии не найдены.");
                        return destDir;
                    }

                    int completed = 0;
                    int skipped = 0;
                    foreach (var job in jobs)
                    {
                        ct.ThrowIfCancellationRequested();
                        string targetDir = string.IsNullOrEmpty(job.RelativeDirectory)
                            ? destDir
                            : Path.Combine(destDir, job.RelativeDirectory);
                        Directory.CreateDirectory(targetDir);

                        string targetPath = Path.Combine(targetDir, SanitizeFileName(job.FileName));
                        if (File.Exists(targetPath))
                        {
                            ReleaseCom(job.Item);
                            skipped++;
                            completed++;
                            progress?.Report($"Импорт: {completed} / {jobs.Count} · пропущено: {skipped}");
                            continue;
                        }

                        progress?.Report($"Импорт: {completed + 1} / {jobs.Count}\n{job.FileName}");
                        bool copied;
                        try { copied = CopyShellItem(job.Item, targetDir, targetPath, ct); }
                        finally { ReleaseCom(job.Item); }
                        if (copied) completed++;
                        else
                        {
                            // Одна ошибка не должна скрывать уже импортированные фотографии.
                            progress?.Report($"Импорт: {completed} / {jobs.Count} · ошибка: {job.FileName}");
                        }
                    }

                    progress?.Report($"Готово: {completed} / {jobs.Count} фото · пропущено: {skipped}");
                    return destDir;
                }
                finally
                {
                    foreach (var job in jobs) ReleaseCom(job.Item);
                    ReleaseCom(srcFolder);
                    ReleaseCom(shell);
                }
            });
        }

        private sealed class PhotoImportJob
        {
            public object Item { get; init; } = null!;
            public string FileName { get; init; } = "";
            public string RelativeDirectory { get; init; } = "";
        }

        private static void CollectPhotoJobs(dynamic folder, string relativeDirectory,
            List<PhotoImportJob> jobs, CancellationToken ct)
        {
            dynamic? items = null;
            try
            {
                items = folder.Items();
                foreach (var itemObj in items)
                {
                    ct.ThrowIfCancellationRequested();
                    dynamic item = itemObj;
                    bool retained = false;
                    try
                    {
                        string name = item.Name?.ToString() ?? "";
                        bool isFolder = false;
                        try { isFolder = (bool)item.IsFolder; } catch { }

                        if (!isFolder && _photoExts.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        {
                            jobs.Add(new PhotoImportJob
                            { Item = item, FileName = name, RelativeDirectory = relativeDirectory });
                            retained = true;
                            continue;
                        }

                        if (isFolder)
                        {
                            dynamic? childFolder = null;
                            try { childFolder = item.GetFolder; } catch { }
                            if (childFolder != null)
                            {
                                string childRelative = string.IsNullOrEmpty(relativeDirectory)
                                    ? SanitizeFileName(name)
                                    : Path.Combine(relativeDirectory, SanitizeFileName(name));
                                CollectPhotoJobs(childFolder, childRelative, jobs, ct);
                                ReleaseCom(childFolder);
                            }
                        }
                    }
                    finally
                    {
                        // Фото-элемент, добавленный в jobs, освобождается после копирования
                        // в ImportPhotosAsync; остальные RCW можно освободить сразу.
                        if (!retained) ReleaseCom(item);
                    }
                }
            }
            finally { ReleaseCom(items); }
        }

        private static bool CopyShellItem(dynamic sourceItem, string targetDir, string targetPath, CancellationToken ct)
        {
            dynamic? shell = null;
            dynamic? destFolder = null;
            try
            {
                shell = CreateShell();
                if (shell == null) return false;
                destFolder = shell.NameSpace(targetDir);
                if (destFolder == null) return false;

                const int FOF_NO_UI = 4;
                destFolder.CopyHere(sourceItem, FOF_NO_UI);

                var deadline = DateTime.UtcNow.AddMinutes(10);
                long lastLength = -1;
                int stable = 0;
                while (DateTime.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    if (File.Exists(targetPath))
                    {
                        long length;
                        try { length = new FileInfo(targetPath).Length; }
                        catch { length = -1; }
                        if (length >= 0 && length == lastLength)
                        {
                            if (++stable >= 2) return true;
                        }
                        else
                        {
                            stable = 0;
                            lastLength = length;
                        }
                    }
                    Thread.Sleep(300);
                }
                return File.Exists(targetPath);
            }
            finally
            {
                ReleaseCom(destFolder);
                ReleaseCom(shell);
            }
        }

        private static Task<T> RunOnStaThreadAsync<T>(Func<T?> work) where T : class
        {
            var tcs = new TaskCompletionSource<T?>();
            var thread = new Thread(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task!;
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

        /// <summary>
        /// Надёжно резолвит Folder для ЛЮБОГО DeviceNode — корневого или
        /// произвольно вложенного. Всегда резолвит NameSpace() только ОДИН
        /// раз, на корне устройства (ShellPath корня подтверждённо
        /// парсится — именно так пользователь дошёл до него из "Этот
        /// компьютер"), а дальше идёт по NamePath шагами .Items()/.GetFolder
        /// — тем же способом, каким дерево строилось изначально. Это
        /// заменяет прежний прямой shell.NameSpace(node.ShellPath) для
        /// вложенных узлов, где ShellPath одного вложенного MTP-элемента
        /// не гарантированно повторно резолвится в NameSpace() (см.
        /// заголовок файла).
        /// </summary>
        private static dynamic? ResolveNode(dynamic shell, DeviceNode node)
        {
            dynamic? folder = ResolveFolder(shell, node.EffectiveRoot.ShellPath);
            if (folder == null) return null;

            foreach (var segment in node.NamePath)
            {
                dynamic? next = null;
                try
                {
                    foreach (var childObj in folder!.Items())
                    {
                        dynamic child = childObj;
                        if (next == null && string.Equals(child.Name?.ToString(), segment, StringComparison.Ordinal))
                            next = child.GetFolder;
                        ReleaseCom(child);
                    }
                }
                catch { ReleaseCom(folder); return null; }

                ReleaseCom(folder);
                if (next == null) return null;
                folder = next;
            }
            return folder;
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

        /// <summary>Null = свойство недоступно/бросило исключение (не удалось
        /// определить) — вызывающий код должен трактовать это как "не
        /// фильтровать", а не как false.</summary>
        private static bool? SafeGetIsFileSystem(dynamic item)
        {
            try { return (bool)item.IsFileSystem; }
            catch { return null; }
        }

        private static int SafeCountFiles(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Count(path => _photoExts.Any(ext =>
                        path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
            }
            catch { return 0; }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Device" : name;
        }

        /// <summary>
        /// Выполняет синхронную блокирующую COM-работу на отдельном потоке с
        /// таймаутом, чтобы не отвечающее/уснувшее MTP-устройство не
        /// подвешивало вызывающий поток (у вызывающей стороны уже свой
        /// await Task.Run(...) — см. Views/DeviceBrowserWindow.xaml.cs), а
        /// возвращал управление с безопасным значением по умолчанию.
        /// Late-bound COM-вызовы нельзя отменить на уровне ОС: если поток
        /// действительно завис на native-вызове, он завершится сам, когда
        /// (если) вызов когда-нибудь вернёт управление — это ограничение
        /// самого механизма позднего связывания, а не утечка ресурсов этого
        /// класса.
        /// </summary>
        /// <summary>
        /// Выполняет синхронную блокирующую COM-работу на ОТДЕЛЬНОМ, СПЕЦИАЛЬНО
        /// СОЗДАННОМ потоке с явным ApartmentState.STA (build 55 — было
        /// Task.Run, то есть поток из ThreadPool, апартамент которого по
        /// умолчанию MTA; см. подробное объяснение в заголовке файла), с
        /// таймаутом, чтобы не отвечающее/уснувшее устройство не подвешивало
        /// вызывающий поток (у вызывающей стороны уже свой
        /// await Task.Run(...) — см. Views/DeviceBrowserWindow.xaml.cs) —
        /// возвращает управление с безопасным значением по умолчанию.
        /// Late-bound COM-вызовы нельзя отменить на уровне ОС: если поток
        /// действительно завис на native-вызове, он завершится сам, когда
        /// (если) вызов когда-нибудь вернёт управление — это ограничение
        /// самого механизма позднего связывания, а не утечка ресурсов этого
        /// класса (поток помечен IsBackground=true, поэтому не удерживает
        /// процесс от завершения).
        /// </summary>
        private static T RunBlocking<T>(Func<T> work, T fallback)
        {
            T result = fallback;
            var thread = new Thread(() =>
            {
                try { result = work(); }
                catch { /* result остаётся fallback */ }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            try
            {
                thread.Start();
                return thread.Join(ComTimeout) ? result : fallback;
            }
            catch { return fallback; }
        }
    }
}
