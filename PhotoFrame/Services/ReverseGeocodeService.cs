// Services/ReverseGeocodeService.cs — v1.1 (build 57)
// Обратное геокодирование GPS → название места на русском, через Nominatim/OSM.
// Бесплатно, без ключа; rate-limit 1 запрос/сек; кеш в памяти по координатам.
// Включается только через AppSettings.GpsReverseGeocodeEnabled.
//
// build 57: опциональная более точная детализация (улица/дом/район), а не
// только город — управляется AppSettings.GpsPrecisionLevel (см.
// PhotoFrame.Models.LocationPrecision). Технически достаточно было
// расширить УЖЕ используемый вызов Nominatim: параметр zoom напрямую
// задаёт уровень детализации ответа (10 ≈ город, 14 ≈ район/пригород,
// 18 ≈ дом на конкретной улице) — отдельный сервис или API не нужен. По
// умолчанию поведение НЕ меняется (City = zoom=10, как раньше) — точное
// позиционирование раскрывает больше личной информации о месте съёмки
// фото, поэтому осталось опцией, а не новым поведением по умолчанию.
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PhotoFrame.Models;

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

        // build 57: ключ кеша включает уровень точности — иначе переключение
        // настройки не приводило бы к новому результату для уже опрошенных
        // координат (в кеше лежал бы старый, менее точный ответ).
        private static string Key(double lat, double lon, LocationPrecision p)
            => $"{Math.Round(lat,3):F3},{Math.Round(lon,3):F3},{p}";

        private static int ZoomFor(LocationPrecision p) => p switch
        {
            LocationPrecision.Street   => 18, // дом на конкретной улице
            LocationPrecision.District => 14, // район/пригород
            _                          => 10, // город (было единственным поведением)
        };

        public static Task<string?> ResolveAsync(double lat, double lon, CancellationToken ct = default)
            => ResolveAsync(lat, lon, LocationPrecision.City, ct);

        public static async Task<string?> ResolveAsync(
            double lat, double lon, LocationPrecision precision, CancellationToken ct = default)
        {
            string key = Key(lat, lon, precision);
            if (_cache.TryGetValue(key, out var hit)) return hit;
            await _lock.WaitAsync(ct);
            try
            {
                if (_cache.TryGetValue(key, out hit)) return hit;
                var since = DateTime.UtcNow - _lastReqUtc;
                if (since < TimeSpan.FromSeconds(1)) await Task.Delay(TimeSpan.FromSeconds(1) - since, ct);
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                string url = $"https://nominatim.openstreetmap.org/reverse?format=json" +
                             $"&lat={lat.ToString(ic)}&lon={lon.ToString(ic)}&accept-language=ru" +
                             $"&zoom={ZoomFor(precision)}&addressdetails=1";
                _lastReqUtc = DateTime.UtcNow;
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) { _cache[key] = null; return null; }
                string json = await resp.Content.ReadAsStringAsync(ct);
                string? name = ExtractName(json, precision);
                _cache[key] = name;
                return name;
            }
            catch { _cache[key] = null; return null; }
            finally { _lock.Release(); }
        }

        private static string? ExtractName(string json, LocationPrecision precision)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("address", out var addr)) return Str(root, "display_name");

                string? city = Str(addr,"city") ?? Str(addr,"town") ?? Str(addr,"village")
                             ?? Str(addr,"municipality") ?? Str(addr,"county");
                string? country = Str(addr,"country");

                if (precision == LocationPrecision.City)
                    return (city != null && country != null) ? $"{city}, {country}"
                         : city ?? country ?? Str(root, "display_name");

                // District/Street: добавляем более мелкие единицы к городу.
                string? district = Str(addr,"suburb") ?? Str(addr,"city_district")
                                  ?? Str(addr,"borough") ?? Str(addr,"neighbourhood");
                string? road = Str(addr,"road") ?? Str(addr,"pedestrian");
                string? house = Str(addr,"house_number");

                var parts = new System.Collections.Generic.List<string>();
                if (precision == LocationPrecision.Street && road != null)
                    parts.Add(house != null ? $"{road}, {house}" : road);
                if (district != null) parts.Add(district);
                if (city != null) parts.Add(city);
                if (parts.Count == 0) return city ?? country ?? Str(root, "display_name");
                return string.Join(", ", parts);
            }
            catch { return null; }
        }

        private static string? Str(JsonElement obj, string prop)
            => obj.TryGetProperty(prop, out var v) && v.ValueKind==JsonValueKind.String ? v.GetString() : null;
    }
}
