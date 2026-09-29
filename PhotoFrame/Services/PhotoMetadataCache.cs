using System;
using System.Collections.Concurrent;
using System.IO;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    /// <summary>
    /// Лёгкий кэш метаданных в памяти. Ключом служит путь + размер + время изменения,
    /// поэтому изменившийся файл автоматически перестаёт совпадать с прежней записью.
    /// </summary>
    public static class PhotoMetadataCache
    {
        private sealed record Entry(long Length, long LastWriteUtcTicks, PhotoInfo Snapshot);

        private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static bool TryGet(string path, out PhotoInfo snapshot)
        {
            snapshot = null!;
            if (!TryReadStamp(path, out var length, out var ticks)) return false;
            if (!Cache.TryGetValue(path, out var entry)) return false;
            if (entry.Length != length || entry.LastWriteUtcTicks != ticks)
            {
                Cache.TryRemove(path, out _);
                return false;
            }

            snapshot = Clone(entry.Snapshot);
            return true;
        }

        public static void Store(PhotoInfo photo)
        {
            if (!TryReadStamp(photo.FilePath, out var length, out var ticks)) return;
            Cache[photo.FilePath] = new Entry(length, ticks, Clone(photo));
        }

        public static void Invalidate(string path) => Cache.TryRemove(path, out _);
        public static void Clear() => Cache.Clear();

        private static bool TryReadStamp(string path, out long length, out long ticks)
        {
            length = 0;
            ticks = 0;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return false;
                length = info.Length;
                ticks = info.LastWriteTimeUtc.Ticks;
                return true;
            }
            catch { return false; }
        }

        private static PhotoInfo Clone(PhotoInfo p) => new()
        {
            FilePath = p.FilePath,
            Directory = p.Directory,
            DateTaken = p.DateTaken,
            MetadataLoaded = p.MetadataLoaded,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            ResolvedLocationName = p.ResolvedLocationName,
            CameraMake = p.CameraMake,
            CameraModel = p.CameraModel,
            LensModel = p.LensModel,
            FocalLengthMm = p.FocalLengthMm,
            Aperture = p.Aperture,
            Iso = p.Iso,
            PixelWidth = p.PixelWidth,
            PixelHeight = p.PixelHeight,
            Orientation = p.Orientation,
            Rating = p.Rating,
            FileSizeBytes = p.FileSizeBytes,
            LastWriteTime = p.LastWriteTime
        };
    }
}
