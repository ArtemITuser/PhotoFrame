// Services/SettingsService.cs — UWP-порт (логика 1:1 с WPF v3.2)
// Хранение настроек в ApplicationData.LocalFolder\settings.json (аналог %AppData%\PhotoFrame).
// System.Text.Json недоступен в .NET Native для UAP < 16299 → используется DataContractJsonSerializer
// (System.Runtime.Serialization.Json), сериализуемый контракт тот же (AppSettings v3.2).

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using PhotoFrameUwp.Models;

namespace PhotoFrameUwp.Services
{
    [DataContract]
    public class AppSettingsWire
    {
        // Дублирует AppSettings «плоско» — enum'ы как int, чтобы DataContract
        // не требовал KnownTypes; конвертация в доменные enum'ы — в Load/Save.
        [DataMember(Name = "SelectedPaths")]          public string[] SelectedPaths { get; set; }
        [DataMember(Name = "IncludeSubdirectories")]  public bool IncludeSubdirectories { get; set; }
        [DataMember(Name = "WatchRemovableMedia")]    public bool WatchRemovableMedia { get; set; }
        [DataMember(Name = "SuggestRemovableMedia")]  public bool SuggestRemovableMedia { get; set; }
        [DataMember(Name = "SlideshowIntervalSeconds")] public int SlideshowIntervalSeconds { get; set; }
        [DataMember(Name = "PlayMode")]               public int PlayMode { get; set; }
        [DataMember(Name = "AutoStart")]              public bool AutoStart { get; set; }
        [DataMember(Name = "LoopSlideshow")]          public bool LoopSlideshow { get; set; }
        [DataMember(Name = "ShowDirectoryOverlay")]   public bool ShowDirectoryOverlay { get; set; }
        [DataMember(Name = "ShowDateOverlay")]        public bool ShowDateOverlay { get; set; }
        [DataMember(Name = "ShowLocationOverlay")]    public bool ShowLocationOverlay { get; set; }
        [DataMember(Name = "OverlayFontSize")]        public double OverlayFontSize { get; set; }
        [DataMember(Name = "TransitionType")]         public int TransitionType { get; set; }
        [DataMember(Name = "TransitionDurationSeconds")] public double TransitionDurationSeconds { get; set; }
        [DataMember(Name = "Theme")]                  public int Theme { get; set; }
        [DataMember(Name = "EnableMicaEffect")]       public bool EnableMicaEffect { get; set; }
        [DataMember(Name = "MinimizeToTray")]         public bool MinimizeToTray { get; set; }
        [DataMember(Name = "RegisterAsScreensaver")]  public bool RegisterAsScreensaver { get; set; }
        [DataMember(Name = "ScreensaverDelayMinutes")] public int ScreensaverDelayMinutes { get; set; }
        [DataMember(Name = "PreventSleep")]           public bool PreventSleep { get; set; }
        [DataMember(Name = "MonitorOffAfterMinutes")] public int MonitorOffAfterMinutes { get; set; }
        [DataMember(Name = "SleepAfterMinutes")]      public int SleepAfterMinutes { get; set; }
    }

    public static class SettingsService
    {
        private static StorageFolder Folder =>
            ApplicationData.Current.LocalFolder;

        private const string FileName = "settings.json";

        // ─── Конвертация домен ↔ wire ─────────────────────────────────────────────

        private static AppSettingsWire ToWire(AppSettings s) => new AppSettingsWire
        {
            SelectedPaths            = ToArray(s.SelectedPaths),
            IncludeSubdirectories    = s.IncludeSubdirectories,
            WatchRemovableMedia      = s.WatchRemovableMedia,
            SuggestRemovableMedia    = s.SuggestRemovableMedia,
            SlideshowIntervalSeconds = s.SlideshowIntervalSeconds,
            PlayMode                 = (int)s.PlayMode,
            AutoStart                = s.AutoStart,
            LoopSlideshow            = s.LoopSlideshow,
            ShowDirectoryOverlay     = s.ShowDirectoryOverlay,
            ShowDateOverlay          = s.ShowDateOverlay,
            ShowLocationOverlay      = s.ShowLocationOverlay,
            OverlayFontSize          = s.OverlayFontSize,
            TransitionType           = (int)s.TransitionType,
            TransitionDurationSeconds = s.TransitionDurationSeconds,
            Theme                      = (int)s.Theme,
            EnableMicaEffect           = s.EnableMicaEffect,
            MinimizeToTray             = s.MinimizeToTray,
            RegisterAsScreensaver      = s.RegisterAsScreensaver,
            ScreensaverDelayMinutes    = s.ScreensaverDelayMinutes,
            PreventSleep               = s.PreventSleep,
            MonitorOffAfterMinutes     = s.MonitorOffAfterMinutes,
            SleepAfterMinutes          = s.SleepAfterMinutes,
        };

        private static AppSettings FromWire(AppSettingsWire w)
        {
            var s = new AppSettings();
            if (w == null) return s;
            s.SelectedPaths            = w.SelectedPaths != null ? new System.Collections.Generic.List<string>(w.SelectedPaths) : s.SelectedPaths;
            s.IncludeSubdirectories    = w.IncludeSubdirectories;
            s.WatchRemovableMedia      = w.WatchRemovableMedia;
            s.SuggestRemovableMedia    = w.SuggestRemovableMedia;
            s.SlideshowIntervalSeconds = w.SlideshowIntervalSeconds;
            s.PlayMode                 = (PlayMode)w.PlayMode;
            s.AutoStart                = w.AutoStart;
            s.LoopSlideshow            = w.LoopSlideshow;
            s.ShowDirectoryOverlay     = w.ShowDirectoryOverlay;
            s.ShowDateOverlay          = w.ShowDateOverlay;
            s.ShowLocationOverlay      = w.ShowLocationOverlay;
            s.OverlayFontSize          = w.OverlayFontSize;
            s.TransitionType           = (TransitionType)w.TransitionType;
            s.TransitionDurationSeconds = w.TransitionDurationSeconds;
            s.Theme                    = (AppTheme)w.Theme;
            s.EnableMicaEffect         = w.EnableMicaEffect;
            s.MinimizeToTray           = w.MinimizeToTray;
            s.RegisterAsScreensaver    = w.RegisterAsScreensaver;
            s.ScreensaverDelayMinutes  = w.ScreensaverDelayMinutes;
            s.PreventSleep             = w.PreventSleep;
            s.MonitorOffAfterMinutes   = w.MonitorOffAfterMinutes;
            s.SleepAfterMinutes        = w.SleepAfterMinutes;
            return s;
        }

        private static string[] ToArray(System.Collections.Generic.List<string> l)
            => l == null ? new string[0] : l.ToArray();

        // ─── Публичный API (совпадает с WPF-версией по семантике) ──────────────────

        public static async Task<AppSettings> LoadAsync()
        {
            try
            {
                var file = await Folder.GetFileAsync(FileName);
                using (var stream = await file.OpenStreamForReadAsync())
                {
                    var ser = new DataContractJsonSerializer(typeof(AppSettingsWire));
                    var wire = (AppSettingsWire)ser.ReadObject(stream);
                    var s = FromWire(wire);
                    if (IsValid(s)) return s;
                }
            }
            catch { /* повреждённый файл / нет файла */ }
            return new AppSettings();
        }

        /// <summary>Синхронный вход при старте (через кэш — см. InitCacheAsync).</summary>
        public static AppSettings Load()
        {
            if (_cache != null) return Clone(_cache);
            return new AppSettings();
        }

        public static async Task SaveAsync(AppSettings s)
        {
            _cache = Clone(s);
            try
            {
                var wire = ToWire(s);
                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(typeof(AppSettingsWire));
                    ser.WriteObject(ms, wire);
                    string json = Encoding.UTF8.GetString(ms.ToArray(), 0, (int)ms.Length);
                    var file = await Folder.CreateFileAsync(FileName,
                        CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(file, json);
                }
            }
            catch { }
        }

        public static void Save(AppSettings s)
            => SaveAsync(s).GetAwaiter().GetResult();

        /// <summary>Загружает настройки в кэш (вызывается из App.OnLaunched до UI).</summary>
        public static async Task InitCacheAsync()
        {
            _cache = await LoadAsync();
        }

        private static AppSettings _cache;

        private static bool IsValid(AppSettings s)
            => s.SlideshowIntervalSeconds >= 1 || s.SelectedPaths.Count > 0
               || s.SlideshowIntervalSeconds == 0; // пустой дефолт тоже валиден

        public static AppSettings Clone(AppSettings src) => FromWire(ToWire(src));

        public static string StoragePath => Folder.Path;
    }
}
