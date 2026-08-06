// Services/LiveTileService.cs — v6.0 (build 53)
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
//
// build 53 — исправление регистрации плиток:
//  • GetUpdater() вызывал CreateTileUpdaterForApplication(AppId) со строкой
//    "ArtemITuser.PhotoFrame", но Package.appxmanifest объявляет
//    <Application Id="PhotoFrame">. Для WinRT-API это РАЗНЫЕ идентификаторы
//    (перегрузка с id предназначена для пакетов с НЕСКОЛЬКИМИ Application-
//    записями и ищет id ИМЕННО там) — при несовпадении обновление плитки
//    тихо не находило нужное приложение. Теперь используется беспараметрный
//    CreateTileUpdaterForApplication() — "текущее приложение пакета",
//    корректно работающий для пакета с одной Application-записью независимо
//    от того, как называется её Id.
//  • TryPinTileAsync() создавал SecondaryTile через Activator.CreateInstance
//    с ошибочными типами аргументов (строка там, где конструктору нужен
//    Uri логотипа) — привязка конструктора по reflection не находила
//    совпадения и всегда падала в catch, поэтому закрепление плитки не
//    работало никогда. Переписано через SecondaryTile(tileId) +
//    установку свойств (DisplayName/Arguments/VisualElements/DesiredSize)
//    по отдельности — устойчивее к reflection, чем угадывание сигнатуры
//    многоаргументного конструктора.
//  • И то, и другое требует package identity (MSIX либо sparse-пакет) —
//    IsPinningSupported() теперь дополнительно проверяет
//    SystemIntegration.IsRunningAsMsixPackage(), а не только наличие
//    WinRT-типов (те доступны в ОС независимо от identity, поэтому раньше
//    кнопка закрепления показывалась даже там, где закрепить было
//    физически невозможно — ClickOnce/InnoSetup).
//  • AppSettings.LiveTilesLargeEnabled существовал, но нигде не
//    использовался — теперь реально исключает биндинг TileLarge, когда
//    выключен (см. UpdateTile/UpdateInternal).
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

        /// <summary>
        /// Sets the process AUMID for unpackaged (ClickOnce/InnoSetup) runs so
        /// taskbar grouping/jump lists behave correctly. Skipped when running
        /// as an MSIX package: packaged processes get their AUMID from the
        /// manifest's &lt;Application Id&gt; automatically, and explicitly
        /// overriding it is documented to fail for packaged callers anyway.
        /// </summary>
        public static void SetAppUserModelId()
        {
            if (SystemIntegration.IsRunningAsMsixPackage()) return;
            try { SetCurrentProcessExplicitAppUserModelID(AppId); } catch { }
        }

        public static TileUpdateResult UpdateTile(string imagePath,
            TilePhotoDistance distance = TilePhotoDistance.Balanced, bool largeEnabled = true)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return TileUpdateResult.NoPhoto;
            try { return UpdateInternal(imagePath, distance, largeEnabled) ? TileUpdateResult.Success : TileUpdateResult.NotSupported; }
            catch { return TileUpdateResult.Error; }
        }

        public static void ClearTile()
        {
            try { var m = GetUpdater(); m?.GetType().GetMethod("Clear")?.Invoke(m, null); }
            catch { }
            CleanupTempCrops();
        }

        /// <summary>
        /// True only when tile pinning has a realistic chance of working:
        /// the WinRT type must be resolvable (Windows 10+) AND the process
        /// must have real package identity (MSIX/sparse package). Both
        /// Windows.UI.StartScreen.SecondaryTile and Windows.UI.Notifications
        /// require package identity to function — the type-existence check
        /// alone (the old implementation) succeeds even for a plain
        /// unpackaged ClickOnce/InnoSetup process, which used to show the
        /// pin button somewhere it could never actually work.
        /// </summary>
        public static bool IsPinningSupported()
        {
            try
            {
                return SystemIntegration.IsRunningAsMsixPackage()
                    && Type.GetType(
                        "Windows.UI.StartScreen.SecondaryTile, Windows, ContentType=WindowsRuntime") != null;
            }
            catch { return false; }
        }

        public static async Task<bool> TryPinTileAsync()
        {
            if (!SystemIntegration.IsRunningAsMsixPackage()) return false;
            try
            {
                var t = Type.GetType(
                    "Windows.UI.StartScreen.SecondaryTile, Windows, ContentType=WindowsRuntime");
                var sizeType = Type.GetType(
                    "Windows.UI.StartScreen.TileSize, Windows, ContentType=WindowsRuntime");
                if (t == null || sizeType == null) return false;

                // SecondaryTile(string tileId) + property setters. Deliberately
                // NOT using the multi-arg constructor overload here: binding a
                // constructor by reflection requires every argument's runtime
                // type to match exactly, and a previous version of this method
                // passed a string where the API expects a Uri, which silently
                // failed to bind and made pinning a no-op. Setting properties
                // individually degrades gracefully if any single one is
                // missing/renamed on a given OS build instead of failing the
                // whole call.
                var tile = Activator.CreateInstance(t, "photoframe-tile");
                if (tile == null) return false;

                t.GetProperty("DisplayName")?.SetValue(tile, "PhotoFrame");
                t.GetProperty("Arguments")?.SetValue(tile, "photoframe-tile");

                var visualElements = t.GetProperty("VisualElements")?.GetValue(tile);
                if (visualElements != null)
                {
                    var ve = visualElements.GetType();
                    ve.GetProperty("Square150x150Logo")?.SetValue(visualElements,
                        new Uri("ms-appx:///Assets/Square150x150Logo.png"));
                    ve.GetProperty("Wide310x150Logo")?.SetValue(visualElements,
                        new Uri("ms-appx:///Assets/Wide310x150Logo.png"));
                    ve.GetProperty("Square44x44Logo")?.SetValue(visualElements,
                        new Uri("ms-appx:///Assets/Square44x44Logo.png"));
                    ve.GetProperty("ShowNameOnSquare150x150Logo")?.SetValue(visualElements, true);
                    ve.GetProperty("ShowNameOnWide310x150Logo")?.SetValue(visualElements, true);
                }

                var desiredSize = Enum.Parse(sizeType, "Wide310x150");
                t.GetProperty("DesiredSize")?.SetValue(tile, desiredSize);

                var op = t.GetMethod("RequestCreateAsync", Type.EmptyTypes)?.Invoke(tile, null);
                if (op == null) return false;
                var task = op.GetType().GetMethod("AsTask", Type.EmptyTypes)?.Invoke(op, null) as Task<bool>;
                return task != null && await task;
            }
            catch { return false; }
        }

        private static bool UpdateInternal(string imagePath, TilePhotoDistance distance, bool largeEnabled)
        {
            var mgr = GetUpdater(); if (mgr == null) return false;

            // Квадратная обрезка — для TileSmall/TileMedium/TileLarge (1:1).
            // Широкая обрезка — для TileWide (~2.07:1, реальные 310×150).
            string? squareFile = PrepareCroppedCopy(imagePath, 1.0, distance, "square");
            string? wideFile   = PrepareCroppedCopy(imagePath, 310.0 / 150.0, distance, "wide");
            if (squareFile == null || wideFile == null) return false;

            string squareUri = "file:///" + squareFile.Replace('\\', '/');
            string wideUri   = "file:///" + wideFile.Replace('\\', '/');

            // TileLarge — необязательный биндинг (build 53: реально читает
            // AppSettings.LiveTilesLargeEnabled, раньше настройка нигде не
            // применялась). Small/Medium/Wide остаются всегда, т.к. явного
            // запроса на их отключение не было. Примечание: Windows на
            // некоторых версиях всё равно показывает на TileSmall (71×71)
            // только значок приложения, игнорируя пользовательское фоновое
            // изображение — это ограничение самой ОС, а не этого кода.
            string largeBinding = largeEnabled ? $@"
    <binding template=""TileLarge"">
      <image src=""{squareUri}"" placement=""background"" hint-crop=""none""/>
      <group><subgroup hint-weight=""1"">
        <text hint-style=""baseSubtle"" hint-align=""center"">PhotoFrame</text>
      </subgroup></group>
    </binding>" : "";

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
    </binding>{largeBinding}
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
            // Parameterless overload = "the calling package's own application".
            // The string-Id overload only matters for a package that declares
            // MULTIPLE <Application> entries and needs to pick one by its
            // manifest Id — this project has exactly one, so the parameterless
            // form is both simpler and immune to Id-string mismatches against
            // Package.appxmanifest.
            return t?.GetMethod("CreateTileUpdaterForApplication", Type.EmptyTypes)
                    ?.Invoke(null, null);
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

