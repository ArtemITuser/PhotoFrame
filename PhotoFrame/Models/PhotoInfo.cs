// Models/PhotoInfo.cs
// Модель, хранящая путь к файлу и метаданные EXIF, извлечённые при сканировании.

using System;

namespace PhotoFrame.Models
{
    /// <summary>
    /// Описывает одну фотографию: путь, дату съёмки и GPS-координаты.
    /// Объект создаётся FileScanner'ом и заполняется MetadataReader'ом.
    /// </summary>
    public class PhotoInfo
    {
        /// <summary>Полный путь к файлу изображения.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Директория, содержащая файл (без имени файла).</summary>
        public string Directory { get; set; } = string.Empty;

        /// <summary>Дата и время съёмки из EXIF. Null, если метаданные отсутствуют.</summary>
        public DateTime? DateTaken { get; set; }

        /// <summary>Широта из GPS EXIF. Null, если недоступна.</summary>
        public double? Latitude { get; set; }

        /// <summary>Долгота из GPS EXIF. Null, если недоступна.</summary>
        public double? Longitude { get; set; }

        /// <summary>
        /// Возвращает форматированную строку координат для оверлея, либо null.
        /// Пример: «55.7558° N, 37.6173° E»
        /// </summary>
        public string? LocationString
        {
            get
            {
                if (!Latitude.HasValue || !Longitude.HasValue)
                    return null;

                string latDir = Latitude.Value >= 0 ? "N" : "S";
                string lonDir = Longitude.Value >= 0 ? "E" : "W";

                return $"{Math.Abs(Latitude.Value):F4}° {latDir},  {Math.Abs(Longitude.Value):F4}° {lonDir}";
            }
        }

        /// <summary>Форматированная дата для оверлея.</summary>
        public string? DateString =>
            DateTaken.HasValue ? DateTaken.Value.ToString("dd.MM.yyyy  HH:mm") : null;
    }
}
