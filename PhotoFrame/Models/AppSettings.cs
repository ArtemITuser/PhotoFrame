// Models/AppSettings.cs — v4.3 (build 53)

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

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AutoOffMode { Disabled=0, SmartUsage=1, ManualSchedule=2, SunsetToSunrise=3 }

    /// <summary>
    /// Режим кадрирования фото для плиток Пуск (build 52). Плитки имеют
    /// два разных соотношения сторон — квадрат (Small/Medium/Large) и
    /// широкий формат 2.07:1 (Wide) — и без предварительного кадрирования
    /// система обрезает/сжимает фото произвольно. "Дистанция" определяет,
    /// насколько сильно приближается центр кадра при обрезке под квадрат/
    /// широкий формат.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TilePhotoDistance
    {
        /// <summary>Крупный план — плотная обрезка по центру (заметный зум).</summary>
        Close = 0,
        /// <summary>Сбалансированно — обрезка по центру без лишнего зума (по умолчанию).</summary>
        Balanced = 1,
        /// <summary>Целиком — всё фото видно, при необходимости с полями по краям.</summary>
        Far = 2,
    }

    public class AppSettings
    {
        // ── Sources ──────────────────────────────────────────────────────────
        public List<string> SelectedPaths         { get; set; } = new();
        public bool         IncludeSubdirectories { get; set; } = true;
        public bool         WatchRemovableMedia   { get; set; } = false;
        public bool         SuggestRemovableMedia { get; set; } = true;

        /// <summary>
        /// Подмножество SelectedPaths, добавленное со съёмных носителей
        /// (USB-флешки и т.п.) — определяется по DriveType на момент
        /// добавления пути (build 53). Когда такой путь временно
        /// недоступен (флешка просто не воткнута), FileScanner тихо
        /// пропускает его вместо показа баннера "накопитель не найден" —
        /// это ожидаемая, а не ошибочная ситуация. Обычные несъёмные пути
        /// сюда не попадают и продолжают показывать баннер как раньше.
        /// </summary>
        public List<string> RemovableSourcePaths { get; set; } = new();

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

        /// <summary>Кадрирование фото под квадратные/широкие плитки (build 52).</summary>
        public TilePhotoDistance TilePhotoDistance { get; set; } = TilePhotoDistance.Balanced;

        // ── System ───────────────────────────────────────────────────────────
        public bool MinimizeToTray          { get; set; } = true;
        public bool RegisterAsScreensaver   { get; set; } = false;
        public int  ScreensaverDelayMinutes { get; set; } = 5;
        public bool AutostartEnabled        { get; set; } = false;

        // ── Power ────────────────────────────────────────────────────────────
        public bool PreventSleep           { get; set; } = true;
        public int  MonitorOffAfterMinutes { get; set; } = 0;
        public int  SleepAfterMinutes      { get; set; } = 0;

        // ── GPS / геокодирование ────────────────────────────────────────────
        /// <summary>Определять название места (на русском) по GPS EXIF через интернет.</summary>
        public bool GpsReverseGeocodeEnabled { get; set; } = false;

        // ── Автоотключение рамки по расписанию (Ночной режим) ───────────────
        public AutoOffMode AutoOffMode { get; set; } = AutoOffMode.Disabled;

        /// <summary>Начало "тихого" окна в минутах от полуночи (режим ManualSchedule).</summary>
        public int AutoOffFromMinutes { get; set; } = 23 * 60;      // 23:00
        /// <summary>Конец "тихого" окна в минутах от полуночи (режим ManualSchedule).</summary>
        public int AutoOffToMinutes   { get; set; } = 7 * 60;       // 07:00

        /// <summary>Использовать вручную заданные координаты вместо IP-геолокации.</summary>
        public bool    AutoOffUseManualCoords { get; set; } = false;
        public double? AutoOffLatitude        { get; set; }
        public double? AutoOffLongitude       { get; set; }

        // ── Обновления: зеркало/своя ссылка/период проверки ─────────────────
        /// <summary>
        /// Пользовательский URL для проверки обновлений (формат GitHub
        /// Releases API: JSON с полем tag_name). Пусто = использовать
        /// официальный репозиторий GitHub по умолчанию.
        /// </summary>
        public string? UpdateMirrorUrl { get; set; }

        /// <summary>Автоматически проверять обновления в фоне (build 52).</summary>
        public bool AutoCheckUpdatesEnabled { get; set; } = true;

        /// <summary>
        /// Период автопроверки обновлений в днях. Служба проверяет не чаще
        /// этого интервала — вместо проверки при каждом запуске приложения.
        /// </summary>
        public int UpdateCheckPeriodDays { get; set; } = 3;

        /// <summary>UTC-время последней выполненной проверки обновлений (ISO 8601).</summary>
        public string? LastUpdateCheckUtc { get; set; }

        // ── Состояние сеанса (build 53) ──────────────────────────────────────
        /// <summary>
        /// Восстанавливать полноэкранный режим и воспроизведение слайд-шоу
        /// при следующем запуске, если они были активны в прошлом сеансе.
        /// Работает как подстраховка на случай выхода из спящего режима:
        /// процесс обычно переживает сон Windows без изменений (см.
        /// MainWindow.OnPowerModeChanged), но если по любой причине
        /// приложение всё же перезапустится, состояние не теряется.
        /// </summary>
        public bool RestoreLastSessionState { get; set; } = true;

        /// <summary>Был ли включён полноэкранный режим в момент последнего
        /// сохранения состояния (закрытие окна/уход в спящий режим).</summary>
        public bool WasFullscreen { get; set; } = false;

        /// <summary>Проигрывалось ли слайд-шоу в момент последнего
        /// сохранения состояния.</summary>
        public bool WasPlaying { get; set; } = false;
    }
}
