// Models/PhotoInfo.cs
using System;
using System.IO;

namespace PhotoFrame.Models
{
    /// <summary>Метаданные одной фотографии и служебная информация источника.</summary>
    public class PhotoInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;

        /// <summary>Дата съёмки из EXIF. Null, если EXIF отсутствует.</summary>
        public DateTime? DateTaken { get; set; }
        /// <summary>True после завершения попытки чтения метаданных.</summary>
        public bool MetadataLoaded { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? ResolvedLocationName { get; set; }
        public string? CameraMake { get; set; }
        public string? CameraModel { get; set; }
        public string? LensModel { get; set; }
        public double? FocalLengthMm { get; set; }
        public double? Aperture { get; set; }
        public int? Iso { get; set; }
        public int? PixelWidth { get; set; }
        public int? PixelHeight { get; set; }
        public string? Orientation { get; set; }
        public int? Rating { get; set; }

        /// <summary>Размер файла в байтах. Заполняется без чтения содержимого.</summary>
        public long? FileSizeBytes { get; set; }

        /// <summary>Последнее изменение файла по локальным часам файловой системы.</summary>
        public DateTime? LastWriteTime { get; set; }

        public string FileName => Path.GetFileName(FilePath);
        public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FilePath);
        public string Extension => Path.GetExtension(FilePath);

        /// <summary>Разрешение в мегапикселях, если известны обе стороны.</summary>
        public double? Megapixels => PixelWidth.HasValue && PixelHeight.HasValue
            ? (double)PixelWidth.Value * PixelHeight.Value / 1_000_000d
            : null;

        /// <summary>Отношение сторон ширины к высоте.</summary>
        public double? AspectRatio => PixelWidth.HasValue && PixelHeight.GetValueOrDefault() > 0
            ? (double)PixelWidth.Value / PixelHeight.Value
            : null;

        /// <summary>
        /// Дата для сортировки: EXIF при наличии, иначе время последнего изменения.
        /// Такой порядок детерминирован для файлов без EXIF.
        /// </summary>
        public DateTime SortDate
        {
            get
            {
                if (DateTaken.HasValue) return DateTaken.Value;
                if (LastWriteTime.HasValue) return LastWriteTime.Value;
                try { return File.GetLastWriteTime(FilePath); }
                catch { return DateTime.MinValue; }
            }
        }

        public string? LocationString
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ResolvedLocationName))
                    return ResolvedLocationName;

                if (!Latitude.HasValue || !Longitude.HasValue)
                    return null;

                string latDir = Latitude.Value >= 0 ? "N" : "S";
                string lonDir = Longitude.Value >= 0 ? "E" : "W";
                return $"{Math.Abs(Latitude.Value):F4}° {latDir},  {Math.Abs(Longitude.Value):F4}° {lonDir}";
            }
        }

        public string? DateString =>
            DateTaken.HasValue ? DateTaken.Value.ToString("dd.MM.yyyy  HH:mm") : null;
    }
}
