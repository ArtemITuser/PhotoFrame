// Services/MetadataReader.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public static class MetadataReader
    {
        public static void Populate(PhotoInfo photo)
        {
            if (string.IsNullOrWhiteSpace(photo.FilePath) || !File.Exists(photo.FilePath)) return;
            if (PhotoMetadataCache.TryGet(photo.FilePath, out var cached))
            {
                Copy(cached, photo);
                photo.MetadataLoaded = true;
                return;
            }
            try
            {
                var fileInfo = new FileInfo(photo.FilePath);
                photo.FileSizeBytes = fileInfo.Length;
                photo.LastWriteTime = fileInfo.LastWriteTime;
                using var stream = new FileStream(photo.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return;
                if (decoder.Frames[0].Metadata is not BitmapMetadata meta) return;
                photo.DateTaken = ExtractDate(meta);
                photo.CameraMake = GetString(meta, new[] { "/app1/ifd/{ushort=271}", "/app1/ifd/271" });
                photo.CameraModel = GetString(meta, new[] { "/app1/ifd/{ushort=272}", "/app1/ifd/272" });
                photo.LensModel = GetString(meta, new[] { "/app1/ifd/exif/subifd/{ushort=42036}", "/app1/ifd/exif/subifd/{uint=42036}" });
                photo.FocalLengthMm = GetDouble(meta, new[] { "/app1/ifd/exif/subifd/{ushort=37386}", "/app1/ifd/exif/subifd/{uint=37386}" });
                photo.Aperture = GetDouble(meta, new[] { "/app1/ifd/exif/subifd/{ushort=33437}", "/app1/ifd/exif/subifd/{uint=33437}" });
                photo.Iso = GetInt(meta, new[] { "/app1/ifd/exif/subifd/{ushort=34855}", "/app1/ifd/exif/subifd/{uint=34855}" });
                photo.Orientation = GetString(meta, new[] { "/app1/ifd/{ushort=274}", "/app1/ifd/{uint=274}" });
                photo.Rating = GetInt(meta, new[] { "/app1/xmp/{wstr=http://ns.adobe.com/xap/1.0/:Rating}", "/xmp/xmp:Rating" });
                photo.PixelWidth = decoder.Frames[0].PixelWidth;
                photo.PixelHeight = decoder.Frames[0].PixelHeight;
                ExtractGps(meta, out var lat, out var lon);
                photo.Latitude = lat;
                photo.Longitude = lon;
                PhotoMetadataCache.Store(photo);
            }
            catch { }
            finally
            {
                photo.MetadataLoaded = true;
            }
        }

        /// <summary>Заполняет дату у большого списка параллельно и ограниченно.</summary>
        public static async Task PopulateDatesAsync(IEnumerable<PhotoInfo> photos,
            IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            var list = photos.ToList();
            if (list.Count == 0) return;
            using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 2, 2, 6));
            int completed = 0;
            var tasks = list.Select(async photo =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Populate(photo);
                    progress?.Report(Interlocked.Increment(ref completed));
                }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static void Copy(PhotoInfo source, PhotoInfo target)
        {
            target.DateTaken = source.DateTaken;
            target.MetadataLoaded = source.MetadataLoaded;
            target.Latitude = source.Latitude;
            target.Longitude = source.Longitude;
            target.ResolvedLocationName = source.ResolvedLocationName;
            target.CameraMake = source.CameraMake;
            target.CameraModel = source.CameraModel;
            target.LensModel = source.LensModel;
            target.FocalLengthMm = source.FocalLengthMm;
            target.Aperture = source.Aperture;
            target.Iso = source.Iso;
            target.PixelWidth = source.PixelWidth;
            target.PixelHeight = source.PixelHeight;
            target.Orientation = source.Orientation;
            target.Rating = source.Rating;
            target.FileSizeBytes = source.FileSizeBytes;
            target.LastWriteTime = source.LastWriteTime;
        }

        private static string? GetString(BitmapMetadata meta, IEnumerable<string> queries)
        {
            foreach (var q in queries)
            {
                try
                {
                    var value = meta.GetQuery(q)?.ToString()?.Trim().Trim('\0');
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                catch { }
            }
            return null;
        }

        private static double? GetDouble(BitmapMetadata meta, IEnumerable<string> queries)
        {
            foreach (var q in queries)
            {
                try
                {
                    var value = meta.GetQuery(q);
                    if (value is ulong ul)
                    {
                        uint n=(uint)(ul & 0xffffffffUL), d=(uint)(ul >> 32);
                        if (d != 0) return (double)n/d;
                    }
                    if (value is long sl)
                    {
                        var v = ReadRational(sl); if (!double.IsNaN(v)) return v;
                    }
                    if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var d2)) return d2;
                } catch { }
            }
            return null;
        }

        private static int? GetInt(BitmapMetadata meta, IEnumerable<string> queries)
        {
            var value = GetDouble(meta, queries);
            return value.HasValue ? (int)Math.Round(value.Value) : null;
        }

        private static DateTime? ExtractDate(BitmapMetadata meta)
        {
            string? raw = null;
            try { raw = meta.DateTaken; } catch { }
            if (string.IsNullOrWhiteSpace(raw))
            {
                foreach (var query in new[]
                {
                    "/app1/ifd/exif/subifd:{uint=36867}",
                    "/app1/ifd/exif/subifd:{uint=36868}",
                    "/app1/ifd/{uint=306}"
                })
                {
                    try
                    {
                        raw = meta.GetQuery(query) as string;
                        if (!string.IsNullOrWhiteSpace(raw)) break;
                    }
                    catch { }
                }
            }
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim().Replace('\0', ' ');
            if (DateTime.TryParseExact(raw, new[] { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy:MM:dd HH:mm:ss.FFF" },
                CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
                return exact;
            return DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
                ? parsed : null;
        }

        private static void ExtractGps(BitmapMetadata meta, out double? latitude, out double? longitude)
        {
            latitude = null; longitude = null;
            try
            {
                var latRaw = meta.GetQuery("/app1/ifd/gps/subifd:{uint=2}");
                var latRef = meta.GetQuery("/app1/ifd/gps/subifd:{uint=1}")?.ToString();
                var lonRaw = meta.GetQuery("/app1/ifd/gps/subifd:{uint=4}");
                var lonRef = meta.GetQuery("/app1/ifd/gps/subifd:{uint=3}")?.ToString();
                var lat = ParseGpsRationals(latRaw);
                var lon = ParseGpsRationals(lonRaw);
                if (!lat.HasValue || !lon.HasValue) return;
                if (latRef?.Trim().Equals("S", StringComparison.OrdinalIgnoreCase) == true) lat = -lat.Value;
                if (lonRef?.Trim().Equals("W", StringComparison.OrdinalIgnoreCase) == true) lon = -lon.Value;
                if (lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
                { latitude = lat; longitude = lon; }
            }
            catch { }
        }

        private static double? ParseGpsRationals(object? raw)
        {
            try
            {
                if (raw is not Array array || array.Length < 3) return null;
                double d0 = ReadRational(array.GetValue(0));
                double d1 = ReadRational(array.GetValue(1));
                double d2 = ReadRational(array.GetValue(2));
                if (double.IsNaN(d0) || double.IsNaN(d1) || double.IsNaN(d2)) return null;
                return d0 + d1 / 60.0 + d2 / 3600.0;
            }
            catch { return null; }
        }

        private static double ReadRational(object? value)
        {
            if (value is ulong ul)
            {
                uint numerator = (uint)(ul & 0xFFFFFFFFUL);
                uint denominator = (uint)(ul >> 32);
                return denominator == 0 ? double.NaN : (double)numerator / denominator;
            }
            if (value is long sl) return ReadRational(unchecked((ulong)sl));
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var result) ? result : double.NaN;
        }
    }
}
