using System;
using System.Linq;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public static class ProfileService
    {
        public static PhotoFrameProfile EnsureCurrent(AppSettings settings)
        {
            var profile = settings.Profiles.FirstOrDefault(p => p.Id == settings.SelectedProfileId);
            if (profile != null)
            {
                // Миграция старых настроек Build 61: конструктор AppSettings уже
                // создаёт пустой профиль, но старые SelectedPaths приходят из JSON
                // отдельно. Не затираем ими старые папки пустым профилем.
                if (profile.SelectedPaths.Count == 0 && settings.SelectedPaths.Count > 0)
                    Capture(settings, profile);
                return profile;
            }
            profile = new PhotoFrameProfile
            {
                Name = "Основной",
                SelectedPaths = settings.SelectedPaths.ToList(),
                SlideshowIntervalSeconds = settings.SlideshowIntervalSeconds,
                PlayMode = settings.PlayMode,
                TransitionType = settings.TransitionType,
                TransitionDurationSeconds = settings.TransitionDurationSeconds,
                ScalingMode = settings.DisplayScalingMode,
                Loop = settings.LoopSlideshow,
                KenBurnsStartZoom = settings.KenBurnsStartZoom,
                KenBurnsEndZoom = settings.KenBurnsEndZoom,
                KenBurnsPanX = settings.KenBurnsPanX,
                KenBurnsPanY = settings.KenBurnsPanY,
                HighQualityDecode = settings.HighQualityDecode
            };
            settings.Profiles.Add(profile);
            settings.SelectedProfileId = profile.Id;
            return profile;
        }

        public static void Apply(AppSettings settings, PhotoFrameProfile profile)
        {
            settings.SelectedProfileId = profile.Id;
            settings.SelectedPaths = profile.SelectedPaths.ToList();
            settings.SlideshowIntervalSeconds = Math.Clamp(profile.SlideshowIntervalSeconds, 1, 3600);
            settings.PlayMode = profile.PlayMode;
            settings.TransitionType = profile.TransitionType;
            settings.TransitionDurationSeconds = Math.Clamp(profile.TransitionDurationSeconds, 0.2, 10);
            settings.DisplayScalingMode = profile.ScalingMode;
            settings.LoopSlideshow = profile.Loop;
            settings.KenBurnsStartZoom = Math.Clamp(profile.KenBurnsStartZoom, 1.0, 1.5);
            settings.KenBurnsEndZoom = Math.Clamp(profile.KenBurnsEndZoom, 1.0, 1.7);
            settings.KenBurnsPanX = Math.Clamp(Math.Abs(profile.KenBurnsPanX), 0.0, 0.5);
            settings.KenBurnsPanY = Math.Clamp(Math.Abs(profile.KenBurnsPanY), 0.0, 0.5);
            settings.HighQualityDecode = profile.HighQualityDecode;
        }

        public static void Capture(AppSettings settings, PhotoFrameProfile profile)
        {
            profile.SelectedPaths = settings.SelectedPaths.ToList();
            profile.SlideshowIntervalSeconds = settings.SlideshowIntervalSeconds;
            profile.PlayMode = settings.PlayMode;
            profile.TransitionType = settings.TransitionType;
            profile.TransitionDurationSeconds = settings.TransitionDurationSeconds;
            profile.ScalingMode = settings.DisplayScalingMode;
            profile.Loop = settings.LoopSlideshow;
            profile.KenBurnsStartZoom = Math.Clamp(settings.KenBurnsStartZoom, 1.0, 1.5);
            profile.KenBurnsEndZoom = Math.Clamp(settings.KenBurnsEndZoom, 1.0, 1.7);
            profile.KenBurnsPanX = Math.Clamp(Math.Abs(settings.KenBurnsPanX), 0.0, 0.5);
            profile.KenBurnsPanY = Math.Clamp(Math.Abs(settings.KenBurnsPanY), 0.0, 0.5);
            profile.HighQualityDecode = settings.HighQualityDecode;
        }
    }
}
