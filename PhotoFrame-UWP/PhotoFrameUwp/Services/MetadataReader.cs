// Services/MetadataReader.cs — UWP-порт
// Извлекает EXIF-метаданные (дата съёмки, GPS) из файлов изображений.
//
// WPF-версия использовала System.Windows.Media.Imaging.BitmapMetadata —
// недоступен в UWP. Здесь — Windows.Storage.FileProperties.PhotoProperties
// (DateTaken / Latitude / Longitude), логика заполнения PhotoInfo идентична.
// Все операции обёрнуты в try-catch, поскольку многие файлы не содержат EXIF.

using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using PhotoFrameUwp.Models;

namespace PhotoFrameUwp.Services
{
    public static class MetadataReader
    {
        /// <summary>
        /// Читает метаданные EXIF из файла и заполняет поля DateTaken,
        /// Latitude и Longitude объекта PhotoInfo.
        /// При любой ошибке поля остаются null.
        /// </summary>
        public static async Task PopulateAsync(PhotoInfo photo)
        {
            if (photo == null || string.IsNullOrEmpty(photo.FilePath))
                return;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(photo.FilePath);

                // BasicProperties: дата съёмки (если нет EXIF — DateModified как в «запасном варианте» WPF)
                var basic = await file.GetBasicPropertiesAsync();

                // PhotoProperties: DateTaken/GPS — типизированный API вместо PropertySet
                var photoProps = await file.GetPropertiesAsync();
                var propStore = photoProps.Properties;

                // ── Дата съёмки ────────────────────────────────────────────────────
                DateTime dt;
                if (photoProps.DateTaken != null
                        && photoProps.DateTaken.Value.Year > 1980)
                    photo.DateTaken = photoProps.DateTaken.Value;
                else if (TryToDateTime(basic.DateModified, out dt))
                    photo.DateTaken = dt;   // запасной вариант (аналог fallback в WPF)

                // ── GPS ────────────────────────────────────────────────────────────
                double lat = 0, lon = 0;
                bool hasLat = TryGetProperty(propStore, "System.GPS.LatitudeDecimal", out lat);
                bool hasLon = TryGetProperty(propStore, "System.GPS.LongitudeDecimal", out lon);

                if (hasLat && hasLon && (Math.Abs(lat) > 0.000001 || Math.Abs(lon) > 0.000001))
                {
                    photo.Latitude  = lat;
                    photo.Longitude = lon;
                }
            }
            catch
            {
                // Повреждённый файл, неизвестный формат, нет прав — молча игнорируем
            }
        }

        /// <summary>Синхронная обёртка для вызова вне UI-потока (логически = Populate в WPF).</summary>
        public static void Populate(PhotoInfo photo)
            => PopulateAsync(photo).GetAwaiter().GetResult();

        // ─── Приватные вспомогательные методы ─────────────────────────────────────

        private static bool TryGetProperty(object store, string key, out double value)
        {
            value = 0;
            try
            {
                var get = store.GetType()
                    .GetMethod("GetValueOrDefault", new[] { typeof(string) });
                if (get == null) return false;
                object raw = get.Invoke(store, new object[] { key });
                if (raw == null) return false;
                value = Convert.ToDouble(raw);
                return true;
            }
            catch { return false; }
        }

        private static bool TryGetProperty(object store, string key, out object value)
        {
            value = null;
            try
            {
                var get = store.GetType()
                    .GetMethod("GetValueOrDefault", new[] { typeof(string) });
                if (get == null) return false;
                value = get.Invoke(store, new object[] { key });
                return value != null;
            }
            catch { return false; }
        }

        private static bool TryToDateTime(Windows.Foundation.DateTime ft, out DateTime dt)
        {
            try
            {
                dt = new DateTime(ft.UniversalDateTime.Ticks, DateTimeKind.Local)
                        .ToLocalTime();
                return dt.Year > 1980;
            }
            catch { dt = default(DateTime); return false; }
        }
    }
}
