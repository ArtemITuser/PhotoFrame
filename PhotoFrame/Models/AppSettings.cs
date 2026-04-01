// Models/AppSettings.cs — Модель всех настроек приложения (System.Text.Json-сериализуемая)

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoFrame.Models
{
    /// <summary>Тип перехода между кадрами.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TransitionType
    {
        Random        = 0,
        Fade          = 1,
        SlideLeft     = 2,
        SlideRight    = 3,
        SlideUp       = 4,
        SlideDown     = 5,
        ZoomIn        = 6,
        ZoomOut       = 7,
        FlipH         = 8,
        FlipV         = 9,
        BlurDissolve  = 10,
        WipeLeft      = 11,
        WipeRight     = 12,
        WipeUp        = 13,
        WipeDown      = 14,
        Checkerboard  = 15,
        KenBurns      = 16,
        Mosaic        = 17,
        Spiral        = 18,
        PageTurn      = 19,
    }

    /// <summary>Тема оформления.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AppTheme { System = 0, Dark = 1, Light = 2 }

    /// <summary>Порядок воспроизведения слайдшоу.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PlayMode
    {
        Sequential    = 0,  // по порядку (имя файла)
        Shuffle       = 1,  // перемешать весь список, затем повторить
        TrueRandom    = 2,  // каждый раз случайный кадр (допускает повторы)
        DateAscending = 3,  // по дате съёмки: старые → новые
        DateDescending= 4,  // по дате съёмки: новые → старые
    }

    public class AppSettings
    {
        // ─── Источники ────────────────────────────────────────────────────────────
        public List<string> SelectedPaths       { get; set; } = new();
        public bool         IncludeSubdirectories{ get; set; } = true;
        public List<string> ExcludedPaths       { get; set; } = new();

        // ─── Слайдшоу ─────────────────────────────────────────────────────────────
        public int      SlideshowIntervalSeconds { get; set; } = 5;
        public PlayMode PlayMode                 { get; set; } = PlayMode.Shuffle;
        public bool     AutoStart                { get; set; } = false;
        public bool     LoopSlideshow            { get; set; } = true;

        // ─── Оверлеи ──────────────────────────────────────────────────────────────
        public bool   ShowDirectoryOverlay  { get; set; } = true;
        public bool   ShowDateOverlay       { get; set; } = true;
        public bool   ShowLocationOverlay   { get; set; } = true;
        public double OverlayFontSize       { get; set; } = 15.0;

        // ─── Переходы ─────────────────────────────────────────────────────────────
        public TransitionType TransitionType            { get; set; } = TransitionType.Random;
        public double         TransitionDurationSeconds { get; set; } = 0.75;

        // ─── Внешний вид ──────────────────────────────────────────────────────────
        public AppTheme Theme            { get; set; } = AppTheme.System;
        public bool     EnableMicaEffect { get; set; } = true;

        // ─── Системная интеграция ─────────────────────────────────────────────────
        public bool   StartWithWindows        { get; set; } = false;
        public bool   MinimizeToTray          { get; set; } = true;

        // ─── Скринсейвер ──────────────────────────────────────────────────────────
        public bool   RegisterAsScreensaver    { get; set; } = false;
        public int    ScreensaverDelayMinutes  { get; set; } = 5;

        // ─── Электропитание ───────────────────────────────────────────────────────
        public bool   PreventSleep             { get; set; } = true;
        public bool   ManagePowerOnExit        { get; set; } = false;
        public int    SleepAfterMinutes        { get; set; } = 0; // 0 = не управлять
        public int    MonitorOffAfterMinutes   { get; set; } = 0; // 0 = не управлять
    }
}
