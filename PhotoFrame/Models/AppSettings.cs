// Models/AppSettings.cs — v4.1 (build 44 / v1.2.0.1)

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PhotoFrame.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TransitionType
    {
        Random=0, Fade=1, SlideLeft=2, SlideRight=3, SlideUp=4, SlideDown=5,
        ZoomIn=6, ZoomOut=7, FlipH=8, FlipV=9, BlurDissolve=10,
        WipeLeft=11, WipeRight=12, WipeUp=13, WipeDown=14,
        Checkerboard=15, KenBurns=16, Mosaic=17, Spiral=18, PageTurn=19,
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AppTheme { System=0, Dark=1, Light=2 }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PlayMode
    {
        Sequential=0, Shuffle=1, TrueRandom=2,
        DateAscending=3, DateDescending=4,
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CounterFormat { PhotoOnly=0, WithTotal=1, Hidden=2 }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UiMode { Modern=0, Aero7=1 }

    public class AppSettings
    {
        // ── Sources ──────────────────────────────────────────────────────────
        public List<string> SelectedPaths         { get; set; } = new();
        public bool         IncludeSubdirectories { get; set; } = true;
        public bool         WatchRemovableMedia   { get; set; } = false;
        public bool         SuggestRemovableMedia { get; set; } = true;

        // ── Slideshow ────────────────────────────────────────────────────────
        public int      SlideshowIntervalSeconds { get; set; } = 5;
        public PlayMode PlayMode                 { get; set; } = PlayMode.Shuffle;
        public bool     AutoStart                { get; set; } = false;
        public bool     LoopSlideshow            { get; set; } = true;

        // ── Overlays ─────────────────────────────────────────────────────────
        public bool   ShowDirectoryOverlay { get; set; } = true;
        public bool   ShowDateOverlay      { get; set; } = true;
        public bool   ShowLocationOverlay  { get; set; } = true;
        public double OverlayFontSize      { get; set; } = 15.0;

        // ── Counter ──────────────────────────────────────────────────────────
        public CounterFormat CounterDisplayFormat { get; set; } = CounterFormat.PhotoOnly;

        // ── Transitions ──────────────────────────────────────────────────────
        public TransitionType TransitionType            { get; set; } = TransitionType.Random;
        public double         TransitionDurationSeconds { get; set; } = 0.75;

        // ── Appearance ───────────────────────────────────────────────────────
        public AppTheme Theme            { get; set; } = AppTheme.System;
        public bool     EnableMicaEffect { get; set; } = true;
        public UiMode   UiMode           { get; set; } = UiMode.Modern;

        // ── Live Tiles ───────────────────────────────────────────────────────
        /// <summary>Enable Live Tile updates (requires ClickOnce install).</summary>
        public bool LiveTilesEnabled           { get; set; } = false;

        /// <summary>Also push Large (310×310) tile. Default true.</summary>
        public bool LiveTilesLargeEnabled      { get; set; } = true;

        /// <summary>
        /// Tile photo rotation interval in seconds, independent of slideshow.
        /// 0 = follow SlideshowIntervalSeconds. Min 5 s when non-zero.
        /// </summary>
        public int  LiveTileCycleIntervalSeconds { get; set; } = 0;

        // ── System ───────────────────────────────────────────────────────────
        public bool MinimizeToTray          { get; set; } = true;
        public bool RegisterAsScreensaver   { get; set; } = false;
        public int  ScreensaverDelayMinutes { get; set; } = 5;
        public bool AutostartEnabled        { get; set; } = false;

        // ── Power ────────────────────────────────────────────────────────────
        public bool PreventSleep           { get; set; } = true;
        public int  MonitorOffAfterMinutes { get; set; } = 0;
        public int  SleepAfterMinutes      { get; set; } = 0;
    }
}
