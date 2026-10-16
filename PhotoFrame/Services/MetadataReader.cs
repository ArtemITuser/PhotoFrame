// Services/MetadataReader.cs
// Извлекает EXIF-метаданные (дата съёмки, GPS) из файлов изображений
// с использованием встроенного API WPF (BitmapMetadata).
// Все операции обёрнуты в try-catch, поскольку многие файлы не содержат EXIF.

using System;
using System.IO;
using System.Windows.Media.Imaging;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public static class MetadataReader
    {
        /// <summary>
        /// Читает метаданные EXIF из файла и заполняет поля DateTaken,
        /// Latitude и Longitude объекта PhotoInfo.
        /// При любой ошибке поля остаются null.
        /// </summary>
        public static void Populate(PhotoInfo photo)
        {
            if (string.IsNullOrEmpty(photo.FilePath) || !File.Exists(photo.FilePath))
                return;

            try
            {
                using var stream = new FileStream(
                    photo.FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.None);

                if (decoder.Frames.Count == 0) return;

                if (decoder.Frames[0].Metadata is not BitmapMetadata meta)
                    return;

                // ── Дата съёмки ────────────────────────────────────────────────────
                photo.DateTaken = ExtractDate(meta);

                // ── GPS ────────────────────────────────────────────────────────────
                ExtractGps(meta, out double? lat, out double? lon);
                photo.Latitude  = lat;
                photo.Longitude = lon;

                // ── ПАТЧ sp1: дополнительные поля EXIF для вкладки «Больше…» ──────
                photo.CameraMake   = AsStr(meta.GetQuery("/app1/ifd/{uint=271}"));
                photo.CameraModel  = AsStr(meta.GetQuery("/app1/ifd/{uint=272}"));
                photo.Artist       = AsStr(meta.GetQuery("/app1/ifd/{uint=315}"));
                photo.Title        = AsStr(meta.GetQuery("/ifd-exif/{uint=4009}"))
                                  ?? AsStr(meta.GetQuery("/app1/ifd/{uint=4009}"));
                photo.Iso          = AsStr(meta.GetQuery("/app1/ifd/{uint=34667}"))
                                  ?? AsStr(meta.GetQuery("/app1/ifd/subifd/{uint=34867}"));
                var fl = ParseRational(meta.GetQuery("/app1/ifd/subifd:{uint=37381}"));
                if (fl.HasValue) photo.FocalLength = $"{fl.Value:F1} мм";
                var sh = ParseRational(meta.GetQuery("/app1/ifd/subifd:{uint=37378}"));
                if (sh.HasValue && sh.Value > 0)
                    photo.ExposureTime = sh.Value >= 1 ? $"{sh.Value:F1} с" : $"1/{(int)(1 / sh.Value)} с";
                var fn = ParseRational(meta.GetQuery("/app1/ifd/subifd:{uint=37366}"));
                if (fn.HasValue) photo.FNumber = $"f/{fn.Value:F1}";
                photo.PixelWidth  = decoder.Frames[0].PixelWidth;
                photo.PixelHeight = decoder.Frames[0].PixelHeight;
            }
            catch
            {
                // Повреждённый файл, неизвестный формат, нет прав — молча игнорируем
            }
        }

        // ─── Приватные вспомогательные методы ─────────────────────────────────────

        private static string? AsStr(object? v) =>
            v == null ? null : (v as string ?? v.ToString())?.Trim();

        /// <summary>Упакованное WPF рациональное (ulong: num | den&lt;&lt;32) → double.</summary>
        private static double? ParseRational(object? raw)
        {
            try
            {
                if (raw is ulong u)
                {
                    uint num = (uint)(u & 0xFFFFFFFF);
                    uint den = (uint)(u >> 32);
                    return den != 0 ? (double)num / den : (double?)null;
                }
            }
            catch { }
            return null;
        }

        private static DateTime? ExtractDate(BitmapMetadata meta)
        {
            try
            {
                // WPF отдаёт DateTaken как строку "YYYY:MM:DD HH:MM:SS"
                string? raw = meta.DateTaken;
                if (string.IsNullOrWhiteSpace(raw)) return null;

                raw = raw.Trim();
                if (raw.Length >= 19)
                {
                    // Нормализуем в ISO 8601
                    string iso = $"{raw[0..4]}-{raw[5..7]}-{raw[8..10]}T{raw[11..19]}";
                    if (DateTime.TryParse(iso, out DateTime dt))
                        return dt;
                }

                // Запасной вариант: стандартный DateTime.TryParse
                if (DateTime.TryParse(raw, out DateTime fallback))
                    return fallback;
            }
            catch { /* игнорируем */ }

            return null;
        }

        private static void ExtractGps(BitmapMetadata meta,
            out double? latitude, out double? longitude)
        {
            latitude  = null;
            longitude = null;

            try
            {
                // EXIF GPS IFD — пути для JPEG/TIFF через WPF query API
                var latRaw  = meta.GetQuery("/app1/ifd/gps/subifd:{uint=2}");
                var latRef  = meta.GetQuery("/app1/ifd/gps/subifd:{uint=1}") as string;
                var lonRaw  = meta.GetQuery("/app1/ifd/gps/subifd:{uint=4}");
                var lonRef  = meta.GetQuery("/app1/ifd/gps/subifd:{uint=3}") as string;

                if (latRaw != null && lonRaw != null)
                {
                    double? lat = ParseGpsRationals(latRaw);
                    double? lon = ParseGpsRationals(lonRaw);

                    if (lat.HasValue && lon.HasValue)
                    {
                        latitude  = (latRef == "S") ? -lat.Value : lat.Value;
                        longitude = (lonRef == "W") ? -lon.Value : lon.Value;
                    }
                }
            }
            catch { /* нет GPS — ничего не делаем */ }
        }

        /// <summary>
        /// GPS хранится как массив из трёх рациональных чисел (градусы, минуты, секунды),
        /// каждое — ulong, упакованный как (числитель | (знаменатель &lt;&lt; 32)).
        /// </summary>
        private static double? ParseGpsRationals(object raw)
        {
            try
            {
                ulong[] rationals;

                if (raw is ulong[] ul)
                    rationals = ul;
                else if (raw is long[] sl)
                {
                    rationals = new ulong[sl.Length];
                    for (int i = 0; i < sl.Length; i++)
                        rationals[i] = (ulong)sl[i];
                }
                else
                    return null;

                if (rationals.Length < 3) return null;

                double degrees = RationalToDouble(rationals[0]);
                double minutes = RationalToDouble(rationals[1]);
                double seconds = RationalToDouble(rationals[2]);

                return degrees + minutes / 60.0 + seconds / 3600.0;
            }
            catch
            {
                return null;
            }
        }

        private static double RationalToDouble(ulong rational)
        {
            uint numerator   = (uint)(rational & 0xFFFFFFFF);
            uint denominator = (uint)(rational >> 32);
            if (denominator == 0) return 0;
            return (double)numerator / denominator;
        }
    }
}
