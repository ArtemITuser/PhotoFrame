using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoFrame.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DisplayScalingMode
    {
        Fit = 0,
        Fill = 1,
        SmartCrop = 2,
        PixelPerfect = 3
    }

    public sealed class PhotoFrameProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Основной";
        public List<string> SelectedPaths { get; set; } = new();
        public int SlideshowIntervalSeconds { get; set; } = 5;
        public PlayMode PlayMode { get; set; } = PlayMode.Shuffle;
        public TransitionType TransitionType { get; set; } = TransitionType.Random;
        public double TransitionDurationSeconds { get; set; } = 0.75;
        public DisplayScalingMode ScalingMode { get; set; } = DisplayScalingMode.Fit;
        public bool Loop { get; set; } = true;
        public bool ShowOverlays { get; set; } = true;
        public double KenBurnsStartZoom { get; set; } = 1.04;
        public double KenBurnsEndZoom { get; set; } = 1.16;
        public double KenBurnsPanX { get; set; } = 0.12;
        public double KenBurnsPanY { get; set; } = 0.08;
        public bool HighQualityDecode { get; set; } = true;
    }

    /// <summary>
    /// Правила Smart Collection. Все заданные ограничения объединяются через AND.
    /// Пустые поля не участвуют в фильтрации.
    /// </summary>
    public sealed class SmartCollectionDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Новая коллекция";
        public int? Year { get; set; }
        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }
        public string? CameraMake { get; set; }
        public string? CameraModel { get; set; }
        public string? LensModel { get; set; }
        public string? Extension { get; set; }
        public bool? HasGps { get; set; }
        public bool? HasExifDate { get; set; }
        public int? MinimumWidth { get; set; }
        public int? MinimumHeight { get; set; }
        public int? MinimumMegapixels { get; set; }
        public int? IsoMinimum { get; set; }
        public int? IsoMaximum { get; set; }
        public int? RatingMinimum { get; set; }
        public double? FocalLengthMinimum { get; set; }
        public double? FocalLengthMaximum { get; set; }
        public string? DirectoryContains { get; set; }
        public string? LocationContains { get; set; }
    }

    public sealed class ProfileScheduleEntry
    {
        public string ProfileId { get; set; } = string.Empty;
        public int StartMinutes { get; set; }
        public int EndMinutes { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public sealed class MonitorProfile
    {
        public string MonitorDeviceName { get; set; } = string.Empty;
        public string? ProfileId { get; set; }
        public int FallbackProfileIndex { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
