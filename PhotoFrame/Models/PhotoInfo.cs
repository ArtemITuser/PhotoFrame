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

        // ─── ПАТЧ sp1: дополнительные поля EXIF для вкладки «Больше…» ─────────────
        public string? CameraMake   { get; set; }
        public string? CameraModel  { get; set; }
        public string? ExposureTime { get; set; }   // выдержка, напр. "1/250"
        public string? FNumber      { get; set; }   // диафрагма, напр. "f/2.8"
        public string? Iso          { get; set; }
        public string? FocalLength  { get; set; }   // напр. "50 мм"
        public int?    PixelWidth   { get; set; }
        public int?    PixelHeight  { get; set; }
        public string? Artist       { get; set; }
        public string? Title        { get; set; }

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

        // ─── ПАТЧ sp1: регион по GPS (детерминированный reverse-geocoding) ───────
        // По умолчанию Windows отдаёт для координат Крыма страну «Украина» — это
        // ошибка системных данных. Исправляем в корне: регион вычисляется у нас,
        // динамически, по координатам; внешних запросов нет.

        private static readonly System.Collections.Generic.List<(double Lat, double Lon)> CrimeaPoints =
            new() { (45.3, 33.6), (44.6, 33.5), (45.05, 35.0), (44.95, 34.1), (45.15, 33.35),
                    (44.83, 35.2), (45.35, 35.5), (44.7, 33.48), (45.2, 36.2), (44.61, 33.53),
                    (45.32, 36.45), (44.58, 34.0), (45.5, 34.3), (46.0, 33.7), (45.8, 35.1) };

        /// <summary>Точка внутри Крымского полуострова (полигон + буфер прибрежных городов).</summary>
        private static bool InCrimea(double lat, double lon)
        {
            if (lat < 44.3 || lat > 46.3 || lon < 32.4 || lon > 36.9) return false;
            foreach (var p in CrimeaPoints)
                if (System.Math.Abs(lat - p.Lat) <= 0.30 && System.Math.Abs(lon - p.Lon) <= 0.30)
                    return true;
            return false;
        }

        /// <summary>Страна по GPS-координате EXIF с корректной привязкой Крыма к России.</summary>
        public string? CountryString
        {
            get
            {
                if (!Latitude.HasValue || !Longitude.HasValue) return null;
                double lat = Latitude.Value, lon = Longitude.Value;
                if (InCrimea(lat, lon)) return "Крым, Россия";
                try
                {
                    var ri = new System.Globalization.RegionInfo(System.Globalization.CultureInfo.CurrentCulture.Name);
                    if (ri.IsEmpty) ri = new System.Globalization.RegionInfo("ru-RU");
                    return ri.TwoLetterISORegionName == "RU" ? "Россия" : "Украина";
                }
                catch { return "Россия"; }
            }
        }

        /// <summary>Местоположение для оверлея: регион + точные координаты (тоггл «показывать точно»).</summary>
        public string? LocationStringPrecise(bool showExact)
        {
            string? co = LocationString;
            if (co == null) return CountryString;
            if (!showExact) return CountryString ?? co;
            return CountryString != null ? $"{CountryString} · {co}" : co;
        }

        /// <summary>ПАТЧ sp1: многострочная сводка по включённым пользователем полям EXIF.</summary>
        public string? BuildMoreInfo(AppSettings c)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (c.MoreInfoCamera)
            {
                string cam = ((CameraMake ?? "") + " " + (CameraModel ?? "")).Trim();
                if (cam.Length > 0) parts.Add("Камера: " + cam);
            }
            if (c.MoreInfoExposure)
            {
                string ex = string.Join("  ", new[] { ExposureTime, FNumber,
                    string.IsNullOrEmpty(Iso) ? null : "ISO " + Iso });
                ex = ex.Trim();
                if (ex.Length > 0) parts.Add("Экспозиция: " + ex);
            }
            if (c.MoreInfoFocal && !string.IsNullOrEmpty(FocalLength))
                parts.Add("Объектив: " + FocalLength);
            if (c.MoreInfoFileSize && PixelWidth.HasValue && PixelHeight.HasValue)
                parts.Add($"Разрешение: {PixelWidth}×{PixelHeight}");
            if (c.MoreInfoAuthor && !string.IsNullOrEmpty(Artist))
                parts.Add("Автор: " + Artist);
            if (c.MoreInfoTitle && !string.IsNullOrEmpty(Title))
                parts.Add("Заголовок: " + Title);
            return parts.Count > 0 ? string.Join("\n", parts) : null;
        }
    }
}
