// Services/ReverseGeocodeService.cs — v1.0 (build 52)
// Обратное геокодирование GPS → название места на русском, через Nominatim/OSM.
// Бесплатно, без ключа; rate-limit 1 запрос/сек; кеш в памяти по координатам.
// Включается только через AppSettings.GpsReverseGeocodeEnabled.
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoFrame.Services
{
    public static class ReverseGeocodeService
    {
        private static readonly HttpClient _http = CreateClient();
        private static readonly ConcurrentDictionary<string, string?> _cache = new();
        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static DateTime _lastReqUtc = DateTime.MinValue;

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            c.DefaultRequestHeaders.Add("User-Agent",
                "PhotoFrame-WPF/1.2 (github.com/ArtemITuser/PhotoFrame)");
            return c;
        }

        private static string Key(double lat, double lon) => $"{Math.Round(lat,3):F3},{Math.Round(lon,3):F3}";

        public static async Task<string?> ResolveAsync(double lat, double lon, CancellationToken ct = default)
        {
            string key = Key(lat, lon);
            if (_cache.TryGetValue(key, out var hit)) return hit;
            await _lock.WaitAsync(ct);
            try
            {
                if (_cache.TryGetValue(key, out hit)) return hit;
                var since = DateTime.UtcNow - _lastReqUtc;
                if (since < TimeSpan.FromSeconds(1)) await Task.Delay(TimeSpan.FromSeconds(1) - since, ct);
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                string url = $"https://nominatim.openstreetmap.org/reverse?format=json" +
                             $"&lat={lat.ToString(ic)}&lon={lon.ToString(ic)}&accept-language=ru&zoom=10";
                _lastReqUtc = DateTime.UtcNow;
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) { _cache[key] = null; return null; }
                string json = await resp.Content.ReadAsStringAsync(ct);
                string? name = ExtractName(json);
                _cache[key] = name;
                return name;
            }
            catch { _cache[key] = null; return null; }
            finally { _lock.Release(); }
        }

        private static string? ExtractName(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("address", out var addr)) return Str(root, "display_name");
                string? city = Str(addr,"city") ?? Str(addr,"town") ?? Str(addr,"village")
                             ?? Str(addr,"municipality") ?? Str(addr,"county");
                string? country = Str(addr,"country");
                if (city!=null && country!=null) return $"{city}, {country}";
                return city ?? country ?? Str(root, "display_name");
            }
            catch { return null; }
        }

        private static string? Str(JsonElement obj, string prop)
            => obj.TryGetProperty(prop, out var v) && v.ValueKind==JsonValueKind.String ? v.GetString() : null;
    }
}
