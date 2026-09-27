// Models/AppSettings.cs — v3.2
// Все настройки приложения. System.Text.Json-сериализуемые.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoFrame.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TransitionType
    {
        Random = 0, Fade = 1,
        SlideLeft = 2, SlideRight = 3, SlideUp = 4, SlideDown = 5,
        ZoomIn = 6, ZoomOut = 7, FlipH = 8, FlipV = 9,
        BlurDissolve = 10,
        WipeLeft = 11, WipeRight = 12, WipeUp = 13, WipeDown = 14,
        Checkerboard = 15, KenBurns = 16, Mosaic = 17, Spiral = 18, PageTurn = 19,
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AppTheme { System = 0, Dark = 1, Light = 2 }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PlayMode
    {
        Sequential = 0,
        Shuffle    = 1,
        TrueRandom = 2,
        DateAscending  = 3,
        DateDescending = 4,
    }

    public class AppSettings
    {
        // ─── Источники ────────────────────────────────────────────────────────────
        public List<string> SelectedPaths          { get; set; } = new();
        public bool         IncludeSubdirectories  { get; set; } = true;
        /// <summary>Автоматически индексировать съёмные носители при подключении.</summary>
        public bool         WatchRemovableMedia    { get; set; } = false;
        /// <summary>Предлагать добавить съёмный носитель при обнаружении фото.</summary>
        public bool         SuggestRemovableMedia  { get; set; } = true;

        // ─── Слайдшоу ─────────────────────────────────────────────────────────────
        public int      SlideshowIntervalSeconds { get; set; } = 5;
        public PlayMode PlayMode                 { get; set; } = PlayMode.Shuffle;
        public bool     AutoStart                { get; set; } = false;
        public bool     LoopSlideshow            { get; set; } = true;

        // ─── Оверлеи ──────────────────────────────────────────────────────────────
        public bool   ShowDirectoryOverlay { get; set; } = true;
        public bool   ShowDateOverlay      { get; set; } = true;
        public bool   ShowLocationOverlay  { get; set; } = true;
        public double OverlayFontSize      { get; set; } = 15.0;

        // ─── Переходы ─────────────────────────────────────────────────────────────
        public TransitionType TransitionType            { get; set; } = TransitionType.Random;
        public double         TransitionDurationSeconds { get; set; } = 0.75;

        // ─── Внешний вид ──────────────────────────────────────────────────────────
        /// <summary>По умолчанию System — следует за темой Windows.</summary>
        public AppTheme Theme            { get; set; } = AppTheme.System;
        public bool     EnableMicaEffect { get; set; } = true;

        // ─── Системная интеграция ─────────────────────────────────────────────────
        public bool MinimizeToTray        { get; set; } = true;
        public bool RegisterAsScreensaver { get; set; } = false;
        public int  ScreensaverDelayMinutes { get; set; } = 5;

        // ─── Электропитание ───────────────────────────────────────────────────────
        public bool PreventSleep           { get; set; } = true;
        public int  MonitorOffAfterMinutes { get; set; } = 0;
        public int  SleepAfterMinutes      { get; set; } = 0;

        // ─── Обновления (v3.3) ─────────────────────────────────────────────────────
        /// <summary>Проверять наличие новой версии на старте приложения.</summary>
        public bool AutoCheckUpdates       { get; set; } = true;
        /// <summary>Скачанный установщик: проверять SHA-256 по sidecar-файлу .sha256.</summary>
        public bool VerifyUpdateChecksum   { get; set; } = true;
    }
}
