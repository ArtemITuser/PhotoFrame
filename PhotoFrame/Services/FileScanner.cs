// Services/FileScanner.cs — v3.6
// DiskError and ErrorKind defined HERE only.
// DiskErrorTypes.cs has been removed to prevent any duplicate-definition errors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    // ─── Error types (defined here, not in a separate file) ──────────────────

    public enum ErrorKind { AccessDenied, IoError, DriveNotReady, Unknown }

    public class DiskError
    {
        public string    Path    { get; init; } = "";
        public string    Message { get; init; } = "";
        public ErrorKind Kind    { get; init; }
        /// <summary>Корневой том для chkdsk, e.g. "C:\\"</summary>
        public string?   Volume  { get; init; }
    }

    // ─── Scan result ─────────────────────────────────────────────────────────

    public class ScanResult
    {
        public List<PhotoInfo> Photos             { get; init; } = new();
        public int             TotalFilesScanned  { get; init; }
        public int             DirectoriesScanned { get; init; }
        public List<DiskError> DiskErrors         { get; init; } = new();
    }

    // ─── Scanner ─────────────────────────────────────────────────────────────

    public static class FileScanner
    {
        private static readonly HashSet<string> SupportedExts =
            new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" };

        public static Task<ScanResult> ScanAsync(
            IEnumerable<string> paths,
            bool recursive,
            Action<string>? progress = null,
            System.Threading.CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var photos  = new List<PhotoInfo>();
                var errors  = new List<DiskError>();
                int files   = 0, dirs = 0;
                var opt     = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                foreach (var root in paths)
                {
                    if (!Directory.Exists(root))
                    {
                        errors.Add(new DiskError
                        {
                            Path    = root,
                            Message = $"Путь недоступен: {root}",
                            Kind    = ErrorKind.DriveNotReady,
                            Volume  = GetVolume(root)
                        });
                        continue;
                    }
                    ScanDir(root, opt, photos, errors, ref files, ref dirs, progress);
                }

                return new ScanResult
                {
                    Photos             = photos,
                    TotalFilesScanned  = files,
                    DirectoriesScanned = dirs,
                    DiskErrors         = errors
                };
            });
        }

        public static List<DriveInfo> GetAvailableDrives()
            => DriveInfo.GetDrives().Where(d => d.IsReady).ToList();

        public static async Task<List<DriveInfo>> GetRemovableWithPhotosAsync()
        {
            var removable = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Removable)
                .ToList();
            var result = new List<DriveInfo>();
            foreach (var d in removable)
            {
                bool has = await Task.Run(() =>
                {
                    try { return Directory.EnumerateFiles(
                            d.RootDirectory.FullName, "*.*",
                            SearchOption.AllDirectories)
                            .Any(f => SupportedExts.Contains(Path.GetExtension(f))); }
                    catch { return false; }
                });
                if (has) result.Add(d);
            }
            return result;
        }

        public static void ExportPhotoList(
            IEnumerable<PhotoInfo> photos, string outputPath, bool includeTree = true)
        {
            var sb  = new StringBuilder();
            bool csv = outputPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            if (csv)
            {
                sb.AppendLine("Путь;Папка;Имя файла;Расширение");
                foreach (var p in photos)
                    sb.AppendLine($"{p.FilePath};" +
                        $"{p.Directory.Replace(";",",")};" +
                        $"{Path.GetFileNameWithoutExtension(p.FilePath).Replace(";",",")};" +
                        $"{Path.GetExtension(p.FilePath)}");
            }
            else if (includeTree)
            {
                var grouped = photos.GroupBy(p => p.Directory).OrderBy(g => g.Key);
                sb.AppendLine($"PhotoFrame — список фотографий ({DateTime.Now:dd.MM.yyyy HH:mm})");
                sb.AppendLine(new string('─', 60));
                foreach (var g in grouped)
                {
                    sb.AppendLine(); sb.AppendLine($"📁 {g.Key}");
                    foreach (var p in g.OrderBy(x => Path.GetFileName(x.FilePath)))
                        sb.AppendLine($"   • {Path.GetFileName(p.FilePath)}");
                }
                sb.AppendLine(); sb.AppendLine(new string('─', 60));
                sb.AppendLine($"Итого: {photos.Count()} фотографий");
            }
            else
                foreach (var p in photos.OrderBy(x => x.FilePath)) sb.AppendLine(p.FilePath);

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        // ─── Private ─────────────────────────────────────────────────────────

        private static void ScanDir(string dir, SearchOption opt,
            List<PhotoInfo> photos, List<DiskError> errors,
            ref int files, ref int dirs, Action<string>? progress)
        {
            try
            {
                progress?.Invoke(dir); dirs++;
                IEnumerable<string> fileList;
                try { fileList = Directory.EnumerateFiles(dir); }
                catch (IOException ioEx)
                {
                    errors.Add(new DiskError { Path=dir,
                        Message=$"Ошибка чтения в {dir}: {ioEx.Message}",
                        Kind=ErrorKind.IoError, Volume=GetVolume(dir) });
                    return;
                }

                foreach (var file in fileList)
                {
                    files++;
                    try { if (SupportedExts.Contains(Path.GetExtension(file)))
                        photos.Add(new PhotoInfo { FilePath=file, Directory=dir }); }
                    catch { }
                }

                if (opt == SearchOption.AllDirectories)
                {
                    IEnumerable<string> subdirs;
                    try { subdirs = Directory.EnumerateDirectories(dir); }
                    catch { return; }
                    foreach (var sub in subdirs)
                        try { ScanDir(sub, opt, photos, errors, ref files, ref dirs, progress); }
                        catch (UnauthorizedAccessException) { }
                        catch (IOException ioEx)
                        { errors.Add(new DiskError { Path=sub, Message=ioEx.Message,
                            Kind=ErrorKind.IoError, Volume=GetVolume(sub) }); }
                }
            }
            catch (UnauthorizedAccessException uae)
            { errors.Add(new DiskError { Path=dir,
                Message=$"Нет доступа: {uae.Message}",
                Kind=ErrorKind.AccessDenied, Volume=GetVolume(dir) }); }
            catch (IOException ioEx)
            { errors.Add(new DiskError { Path=dir, Message=ioEx.Message,
                Kind=ErrorKind.IoError, Volume=GetVolume(dir) }); }
        }

        private static string? GetVolume(string path)
        { try { return Path.GetPathRoot(path); } catch { return null; } }
    }
}
