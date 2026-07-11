// Services/AutoOffScheduler.cs — v1.1 (build 52)
//
// Автоотключение рамки по расписанию — три независимых режима (выбирается
// один в Настройках → Система → Автоотключение):
//
//   1. SmartUsage      — системный интеллектуальный подсчёт: если за
//      последние N часов (по умолчанию 4) система была активна МЕНЬШЕ
//      порогового процента времени (по умолчанию 15%), считаем что
//      "обычно в это время никто не смотрит" и гасим экран. Использует
//      скользящее окно из отметок активности (через GetSystemIdleTime,
//      без отдельного сервиса — лёгкий опрос раз в 5 минут).
//   2. ManualSchedule  — простое расписание "с XX:XX до XX:XX" (может
//      охватывать полночь, напр. 23:00–07:00).
//   3. SunsetToSunrise — автоматически по закату/рассвету, либо по
//      сохранённым координатам пользователя, либо по приблизительной
//      IP-геолокации (см. SystemIntegration.TryGetApproxLocationAsync).
//
// Сервис не управляет питанием экрана напрямую (это раздел "Питание" —
// SetPowerTimeouts) — вместо этого решает, должно ли слайдшоу/окно
// показываться ("рамка активна") в данный момент, и нотифицирует через
// событие ShouldBeActiveChanged. MainWindow подписывается и сворачивает/
// показывает окно соответственно.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using PhotoFrame.Models;

namespace PhotoFrame.Services
{
    public class AutoOffScheduler
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
        private readonly Queue<(DateTime time, bool wasActive)> _activityLog = new();
        private (double lat, double lon)? _cachedLocation;
        private DateTime _lastLocationFetchUtc = DateTime.MinValue;

        private bool _lastShouldBeActive = true;
        private bool _started;

        public event Action<bool>? ShouldBeActiveChanged;

        public AppSettings Settings { get; set; } = new();

        /// <summary>
        /// Идемпотентен (build 52): повторный вызов без предшествующего
        /// <see cref="Stop"/> больше не добавляет дублирующийся обработчик
        /// Tick — раньше это могло привести к тому, что TickAsync выполнялся
        /// по несколько раз за один тик таймера при повторном вызове Start().
        /// </summary>
        public void Start()
        {
            if (_started) return;
            _started = true;
            _timer.Tick += OnTimerTick;
            _timer.Start();
            _ = TickAsync(); // немедленная первая проверка
        }

        private async void OnTimerTick(object? sender, EventArgs e) => await TickAsync();

        public void Stop()
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _started = false;
        }

        private async Task TickAsync()
        {
            try
            {
                RecordActivitySample();

                bool shouldBeActive = Settings.AutoOffMode switch
                {
                    AutoOffMode.Disabled        => true,
                    AutoOffMode.SmartUsage      => EvaluateSmartUsage(),
                    AutoOffMode.ManualSchedule  => EvaluateManualSchedule(),
                    AutoOffMode.SunsetToSunrise => await EvaluateSunsetSunriseAsync(),
                    _ => true
                };

                if (shouldBeActive != _lastShouldBeActive)
                {
                    _lastShouldBeActive = shouldBeActive;
                    ShouldBeActiveChanged?.Invoke(shouldBeActive);
                }
            }
            catch { /* расписание не должно ронять приложение */ }
        }

        // ─── Режим 1: Smart Usage ───────────────────────────────────────────────

        private const int SmartWindowHours = 4;
        private const double SmartActiveThresholdPct = 15.0;

        private void RecordActivitySample()
        {
            bool active = !SystemIntegration.IsSystemIdle(TimeSpan.FromMinutes(4));
            _activityLog.Enqueue((DateTime.UtcNow, active));

            var cutoff = DateTime.UtcNow.AddHours(-SmartWindowHours);
            while (_activityLog.Count > 0 && _activityLog.Peek().time < cutoff)
                _activityLog.Dequeue();
        }

        private bool EvaluateSmartUsage()
        {
            // Недостаточно данных за окно — по умолчанию считаем активным
            // (не гасим экран необдуманно в первые часы после установки).
            if (_activityLog.Count < 6) return true;

            int activeCount = _activityLog.Count(e => e.wasActive);
            double activePct = 100.0 * activeCount / _activityLog.Count;

            // Если обычно (за последние 4 часа) система активна МЕНЬШЕ
            // порога — считаем что сейчас "нерабочее" время и гасим рамку.
            return activePct >= SmartActiveThresholdPct;
        }

        // ─── Режим 2: Manual Schedule ────────────────────────────────────────────

        private bool EvaluateManualSchedule()
        {
            var now = DateTime.Now.TimeOfDay;
            var from = TimeSpan.FromMinutes(Settings.AutoOffFromMinutes);
            var to   = TimeSpan.FromMinutes(Settings.AutoOffToMinutes);

            bool inOffWindow = from <= to
                ? now >= from && now < to            // обычный диапазон в течение дня
                : now >= from || now < to;             // диапазон через полночь

            return !inOffWindow; // active = НЕ в окне отключения
        }

        // ─── Режим 3: Sunset → Sunrise ────────────────────────────────────────────

        private async Task<bool> EvaluateSunsetSunriseAsync()
        {
            var loc = await GetLocationAsync();
            if (loc == null) return true; // нет координат — не гасим наугад

            var (sunrise, sunset) = SystemIntegration.CalculateSunTimes(
                loc.Value.lat, loc.Value.lon, DateTime.Now);

            var now = DateTime.Now;
            // Активна (показывается) рамка ДНЁМ: от рассвета до заката.
            // Ночью (после заката до следующего рассвета) — гасим.
            bool isDaytime = now.TimeOfDay >= sunrise.TimeOfDay && now.TimeOfDay < sunset.TimeOfDay;
            return isDaytime;
        }

        private async Task<(double lat, double lon)?> GetLocationAsync()
        {
            if (Settings.AutoOffUseManualCoords
                && Settings.AutoOffLatitude.HasValue && Settings.AutoOffLongitude.HasValue)
                return (Settings.AutoOffLatitude.Value, Settings.AutoOffLongitude.Value);

            // Кешируем IP-геолокацию на 12 часов — не дёргаем сервис каждую минуту
            if (_cachedLocation.HasValue
                && (DateTime.UtcNow - _lastLocationFetchUtc) < TimeSpan.FromHours(12))
                return _cachedLocation;

            var loc = await SystemIntegration.TryGetApproxLocationAsync();
            if (loc.HasValue)
            {
                _cachedLocation = loc;
                _lastLocationFetchUtc = DateTime.UtcNow;
            }
            return _cachedLocation;
        }
    }
}
