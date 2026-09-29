using System;
using System.Linq;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public static class ProfileScheduler
    {
        public static string? ResolveProfileId(AppSettings settings, DateTime localNow)
        {
            if (!settings.EnableProfileScheduling || settings.Profiles.Count == 0)
                return null;

            int now = Math.Clamp(localNow.Hour * 60 + localNow.Minute, 0, 1439);
            var validProfileIds = settings.Profiles
                .Select(p => p.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Сначала берём наиболее специфичное активное правило.
            // Если есть несколько пересекающихся правил, приоритет получает
            // правило с меньшим интервалом, затем порядок в файле настроек.
            var matches = settings.ProfileSchedule
                .Select((entry, index) => new { entry, index })
                .Where(x => x.entry.Enabled
                    && !string.IsNullOrWhiteSpace(x.entry.ProfileId)
                    && validProfileIds.Contains(x.entry.ProfileId)
                    && IsActive(x.entry.StartMinutes, x.entry.EndMinutes, now))
                .OrderBy(x => IntervalLength(x.entry.StartMinutes, x.entry.EndMinutes))
                .ThenBy(x => x.index)
                .ToList();

            return matches.Count == 0 ? null : matches[0].entry.ProfileId;
        }

        private static bool IsActive(int startMinutes, int endMinutes, int now)
        {
            startMinutes = Normalize(startMinutes);
            endMinutes = Normalize(endMinutes);

            // Равные границы означают круглосуточное правило. Это полезно
            // как безопасный fallback и избавляет UI от отдельного флага.
            if (startMinutes == endMinutes)
                return true;

            return startMinutes < endMinutes
                ? now >= startMinutes && now < endMinutes
                : now >= startMinutes || now < endMinutes;
        }

        private static int IntervalLength(int startMinutes, int endMinutes)
        {
            startMinutes = Normalize(startMinutes);
            endMinutes = Normalize(endMinutes);
            if (startMinutes == endMinutes) return 1440;
            return endMinutes > startMinutes
                ? endMinutes - startMinutes
                : 1440 - startMinutes + endMinutes;
        }

        private static int Normalize(int minutes)
            => Math.Clamp(minutes, 0, 1439);
    }
}
