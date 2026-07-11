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
        /// Название места на русском (город, страна), полученное через
        /// обратное геокодирование (см. ReverseGeocodeService). Заполняется
        /// асинхронно, отдельно от Populate() — опционально, см.
        /// AppSettings.GpsReverseGeocodeEnabled.
        /// </summary>
        public string? ResolvedLocationName { get; set; }

        /// <summary>
        /// Возвращает строку для оверлея местоположения: название места,
        /// если геокодирование включено и успешно, иначе сырые координаты,
        /// иначе null. Пример с геокодированием: «Москва, Россия».
        /// Пример без него: «55.7558° N, 37.6173° E»
        /// </summary>
        public string? LocationString
        {
            get
            {
                if (!string.IsNullOrEmpty(ResolvedLocationName))
                    return ResolvedLocationName;

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
