// Services/LiveTileService.cs — v5.0 (build 52)
// Live Tiles via WinRT reflection. All 4 sizes with proper branding.
// TryPinTileAsync() — requests tile pin dialog on Win10+.
// IsPinningSupported() — gate for showing the pin button in Settings.
//
// build 52: раньше все 4 биндинга (Small/Medium/Wide/Large) получали один
// и тот же необработанный файл фотографии с hint-crop="none" — при
// несовпадении соотношения сторон фото и плитки (квадрат 1:1 для
// Small/Medium/Large, широкий 2.07:1 для Wide) Windows либо обрезает
// произвольно, либо оставляет пустые поля. Теперь заранее готовятся два
// файла нужных пропорций (квадрат и широкий) с управляемой "дистанцией"
// (TilePhotoDistance) — какую часть фото показывать при обрезке — и уже
// ОНИ передаются в биндинги с корректным hint-crop="none" (кадрирование
// уже выполнено нами, дополнительная обрезка системой не нужна).
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public enum TileUpdateResult { Success, NotSupported, NoPhoto, Error }

    public static class LiveTileService
    {
        private const string AppId = "ArtemITuser.PhotoFrame";
        private static readonly string TileTempDir =
            Path.Combine(Path.GetTempPath(), "PhotoFrame", "Tiles");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string id);

        public static void SetAppUserModelId()
        { try { SetCurrentProcessExplicitAppUserModelID(AppId); } catch { } }

        public static TileUpdateResult UpdateTile(string imagePath, TilePhotoDistance distance = TilePhotoDistance.Balanced)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return TileUpdateResult.NoPhoto;
            try { return UpdateInternal(imagePath, distance) ? TileUpdateResult.Success : TileUpdateResult.NotSupported; }
            catch { return TileUpdateResult.Error; }
        }

        public static void ClearTile()
        {
            try { var m = GetUpdater(); m?.GetType().GetMethod("Clear")?.Invoke(m, null); }
            catch { }
            CleanupTempCrops();
        }

        public static bool IsPinningSupported()
        {
            try { return Type.GetType(
                "Windows.UI.StartScreen.SecondaryTile, Windows, ContentType=WindowsRuntime") != null; }
            catch { return false; }
        }

        public static async Task<bool> TryPinTileAsync()
        {
            try
            {
                var t = Type.GetType(
                    "Windows.UI.StartScreen.SecondaryTile, Windows, ContentType=WindowsRuntime");
                if (t == null) return false;
                var sizeType = Type.GetType(
                    "Windows.UI.StartScreen.TileSize, Windows, ContentType=WindowsRuntime");
                if (sizeType == null) return false;
                var tile = Activator.CreateInstance(t,
                    AppId, "PhotoFrame", "photoframe-tile", AppId,
                    Enum.Parse(sizeType, "Wide310x150"));
                if (tile == null) return false;
                var op = t.GetMethod("RequestCreateAsync", Type.EmptyTypes)?.Invoke(tile, null);
                if (op == null) return false;
                var task = op.GetType().GetMethod("AsTask", Type.EmptyTypes)?.Invoke(op, null) as Task<bool>;
                return task != null && await task;
            }
            catch { return false; }
        }

        private static bool UpdateInternal(string imagePath, TilePhotoDistance distance)
        {
            var mgr = GetUpdater(); if (mgr == null) return false;

            // Квадратная обрезка — для TileSmall/TileMedium/TileLarge (1:1).
            // Широкая обрезка — для TileWide (~2.07:1, реальные 310×150).
            string? squareFile = PrepareCroppedCopy(imagePath, 1.0, distance, "square");
            string? wideFile   = PrepareCroppedCopy(imagePath, 310.0 / 150.0, distance, "wide");
            if (squareFile == null || wideFile == null) return false;

            string squareUri = "file:///" + squareFile.Replace('\\', '/');
            string wideUri   = "file:///" + wideFile.Replace('\\', '/');

            string xml = $@"<tile>
  <visual version=""4"" branding=""nameAndLogo"" displayName=""PhotoFrame"">
    <binding template=""TileSmall"">
      <image src=""{squareUri}"" placement=""background"" hint-crop=""none""/>
    </binding>
    <binding template=""TileMedium"">
      <image src=""{squareUri}"" placement=""background"" hint-crop=""none""/>
    </binding>
    <binding template=""TileWide"">
      <image src=""{wideUri}"" placement=""background"" hint-crop=""none""/>
      <group><subgroup hint-weight=""1"">
        <text hint-style=""captionSubtle"" hint-align=""left"">PhotoFrame</text>
      </subgroup></group>
    </binding>
    <binding template=""TileLarge"">
      <image src=""{squareUri}"" placement=""background"" hint-crop=""none""/>
      <group><subgroup hint-weight=""1"">
        <text hint-style=""baseSubtle"" hint-align=""center"">PhotoFrame</text>
      </subgroup></group>
    </binding>
  </visual>
</tile>";
            var doc = LoadXml(xml); if (doc == null) return false;
            var nt = Type.GetType(
                "Windows.UI.Notifications.TileNotification, Windows, ContentType=WindowsRuntime");
            if (nt == null) return false;
            var n = Activator.CreateInstance(nt, doc); if (n == null) return false;
            mgr.GetType().GetMethod("Update")?.Invoke(mgr, new[] { n });
            return true;
        }

        /// <summary>
        /// Создаёт во временной папке копию фото, обрезанную по центру под
        /// заданное соотношение сторон (<paramref name="targetAspect"/> =
        /// ширина/высота). "Дистанция" регулирует зум: Close — заметно
        /// плотнее к центру, Far — минимальная обрезка (при необходимости
        /// оставляет исходные пропорции внутри целевого холста на сплошном
        /// фоне вместо агрессивной обрезки). Старые временные файлы того же
        /// имени перезаписываются — накопления мусора во временной папке
        /// не происходит.
        /// </summary>
        private static string? PrepareCroppedCopy(string sourcePath, double targetAspect,
            TilePhotoDistance distance, string tag)
        {
            try
            {
                Directory.CreateDirectory(TileTempDir);
                string outPath = Path.Combine(TileTempDir, $"tile_{tag}.png");

                var src = new BitmapImage();
                src.BeginInit();
                src.CacheOption = BitmapCacheOption.OnLoad;
                src.UriSource = new Uri(sourcePath, UriKind.Absolute);
                src.EndInit();
                src.Freeze();

                double srcAspect = (double)src.PixelWidth / src.PixelHeight;

                // Zoom-фактор "дистанции": Close приближает (обрезает больше
                // от краёв сверх минимально необходимого для смены пропорций),
                // Far — почти не обрезает (может добавить поля).
                double zoom = distance switch
                {
                    TilePhotoDistance.Close    => 1.35,
                    TilePhotoDistance.Balanced => 1.0,
                    TilePhotoDistance.Far      => 0.75,
                    _ => 1.0
                };

                CroppedBitmap cropped;
                if (srcAspect > targetAspect)
                {
                    // Источник шире цели — обрезаем по бокам.
                    int cropHeight = src.PixelHeight;
                    int idealWidth = (int)(cropHeight * targetAspect);
                    int cropWidth  = Math.Min(src.PixelWidth, (int)(idealWidth * Math.Min(zoom, srcAspect / targetAspect)));
                    cropWidth = Math.Max(1, Math.Min(cropWidth, src.PixelWidth));
                    int x = (src.PixelWidth - cropWidth) / 2;
                    cropped = new CroppedBitmap(src, new Int32Rect(x, 0, cropWidth, cropHeight));
                }
                else
                {
                    // Источник уже/выше цели — обрезаем сверху/снизу.
                    int cropWidth = src.PixelWidth;
                    int idealHeight = (int)(cropWidth / targetAspect);
                    int cropHeight = Math.Min(src.PixelHeight, (int)(idealHeight * Math.Min(zoom, targetAspect / srcAspect)));
                    cropHeight = Math.Max(1, Math.Min(cropHeight, src.PixelHeight));
                    int y = (src.PixelHeight - cropHeight) / 2;
                    cropped = new CroppedBitmap(src, new Int32Rect(0, y, cropWidth, cropHeight));
                }
                cropped.Freeze();

                using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(cropped));
                encoder.Save(fs);

                return outPath;
            }
            catch { return null; }
        }

        private static void CleanupTempCrops()
        {
            try
            {
                if (Directory.Exists(TileTempDir))
                    foreach (var f in Directory.EnumerateFiles(TileTempDir, "tile_*.png"))
                        try { File.Delete(f); } catch { /* файл может быть занят — не критично */ }
            }
            catch { }
        }

        private static object? GetUpdater()
        {
            var t = Type.GetType(
                "Windows.UI.Notifications.TileUpdateManager, Windows, ContentType=WindowsRuntime");
            return t?.GetMethod("CreateTileUpdaterForApplication", new[] { typeof(string) })
                    ?.Invoke(null, new object[] { AppId });
        }

        private static object? LoadXml(string xml)
        {
            var t = Type.GetType(
                "Windows.Data.Xml.Dom.XmlDocument, Windows, ContentType=WindowsRuntime");
            if (t == null) return null;
            var d = Activator.CreateInstance(t);
            t.GetMethod("LoadXml")?.Invoke(d, new object[] { xml });
            return d;
        }
    }
}

