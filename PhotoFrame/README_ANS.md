# PhotoFrame — техническая документация (v1.2.1.0, build 52, 2026)

Откат к базе v1.2.0.3 (build 46) с повторным применением всех ранее
согласованных исправлений и добавлением нового функционала расписания,
просмотра устройств и зеркал обновлений.

---

## 1. ClickOnce vs InnoSetup — рекомендации по упаковке

### Текущая схема (ClickOnce)

ClickOnce остаётся основным каналом распространения:

- **Установка без прав администратора** — разворачивается в
  `%LocalAppData%\Apps\2.0\...`, не требует UAC при первой установке.
- **Автообновление** — при каждом запуске проверяется манифест на
  сервере/gh-pages; при наличии новой версии — тихое обновление в фоне.
- **Подходит для однопользовательских ПК/ноутбуков**, где рамка запускается
  под тем же профилем Windows, что и был при установке.

### Когда добавить InnoSetup параллельно

InnoSetup — не замена ClickOnce, а **дополнительный канал** для сценариев,
которые ClickOnce не покрывает:

| Сценарий | Почему нужен InnoSetup |
|---|---|
| Регистрация скринсейвера (`.scr` в `System32`) | ClickOnce не даёт доступа к системным папкам вне песочницы приложения; сейчас это решается через `SystemIntegration.ElevatedCopy()` (UAC на лету), но встроенная в инсталлятор регистрация надёжнее |
| Установка на все профили компьютера (не только текущий пользователь) | ClickOnce — всегда per-user |
| Оффлайн/портативная установка без интернета | ClickOnce требует доступности манифеста при первом запуске |
| Кастомная логика деинсталляции (например, откат HKCU\Run, HKCU\Control Panel\Desktop) | `[UninstallRun]`/`[UninstallDelete]` в `.iss` дают точный контроль |

**Рекомендация:** оставить ClickOnce как основной путь для большинства
пользователей; предлагать `.exe`-инсталлятор (InnoSetup) как «Portable /
offline install» на странице релизов GitHub — оба артефакта собираются
одним пайплайном GitHub Actions (`.github/workflows/build-release.yml`)
и публикуются в одном Release, не конфликтуя друг с другом. Скрипт
инсталлятора — `Installer.iss` в корне проекта.

### Зеркало / своя ссылка для проверки обновлений (новое в build 52)

Добавлено поле **Настройки → О программе → «Зеркало / своя ссылка»**.
Реализация — `SystemIntegration.CheckUpdateAsync(Version current, string?
customUrl)`:

- Пусто (по умолчанию) → официальный `api.github.com/repos/.../releases/latest`
- Заполнено → GET-запрос на указанный URL; ожидается тот же JSON-формат,
  что отдаёт GitHub Releases API (поле `"tag_name"`)

Это позволяет:
1. Указать региональное зеркало GitHub API (для сетей с ограниченным доступом).
2. Развернуть собственный лёгкий эндпоинт (например, статический JSON-файл
   на любом хостинге), который отдаёт `{"tag_name": "v1.4.0.1"}` — без
   необходимости поднимать полноценный GitHub-совместимый сервер.

Метод `CheckGitHubUpdateAsync` сохранён для обратной совместимости и
вызывает `CheckUpdateAsync(cur, null)`.

---

## 2. Что переделано при откате к b46 → build 52

### 2.1 Исправления (повторно применены после отката)

- **P/Invoke**: все `[DllImport]` в `SystemIntegration.cs` переведены на
  `[LibraryImport]` (`static partial class` + `partial` методы) —
  устраняет info-сообщения VS2026 про рантайм-маршалинг.
- **`PreventSleep`**: возвращает `bool`, реально проверяя ненулевой
  результат `SetThreadExecutionState`, а не игнорируя его.
- **`.csproj`**: добавлен `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
  (требуется для `LibraryImport` source-generator) и пакет
  `Microsoft.CSharp` (нужен для `dynamic` в `DeviceBrowserService`).

### 2.2 Win7/8 — шрифт значков

`SystemIntegration.ShouldOfferMdl2Font()` возвращает `true`, только если:
`Environment.OSVersion.Version.Major < 10` **и** шрифт физически
отсутствует в `HKLM\...\CurrentVersion\Fonts` **и** пользователь не
отказался навсегда (`HKCU\Software\PhotoFrame\Mdl2FontDeclined`).

На Windows 10/11 первое условие всегда `false` — диалог не показывается
никогда, без дополнительных проверок в UI-коде.

Установка — временный `.cmd` с `copy` в `%windir%\Fonts` и `reg add` в
`HKLM\...\CurrentVersion\Fonts`, запущенный через `Verb=runas` (единый
UAC-промпт вместо двух отдельных операций).

### 2.3 Стоп-лист недоступных путей

`OnCloseDiskError` (крестик) добавляет путь в
`HKCU\Software\PhotoFrame\DismissedPaths` (список через `|`). При
следующем сканировании `ReloadPhotosAsync` фильтрует `DiskErrors` через
`_dismissedPaths.Contains(err.Path)` — баннер для уже закрытых путей не
появляется повторно.

`OnRemoveErrorPath` («Удалить из списка») — путь удаляется из
`SelectedPaths` **и** из стоп-листа (если диск/путь добавят снова вручную,
ошибка должна показаться заново).

### 2.4 О программе — память и кеш

`SystemIntegration.GetMemoryUsageInfo()` возвращает `WorkingSet64`,
`PrivateMemorySize64`, `GC.GetTotalMemory()`, и размер/количество файлов в
`%TEMP%\PhotoFrameCache`. Кнопка «Безопасная очистка» вызывает
`SafeCleanup()`: `GC.Collect(2, Forced, blocking:true, compacting:true)` +
`GCSettings.LargeObjectHeapCompactionMode = CompactOnce`, затем удаление
файлов кеша превью.

### 2.5 Живые плитки

- Частота обновления картинки — независимый слайдер (0–120 сек, 0 = как
  слайдшоу), хранится в `AppSettings.LiveTileCycleIntervalSeconds`.
  `ShowCurrentAsync` throttle-ит вызов `LiveTileService.UpdateTile()` по
  `_lastTileUpdateUtc`.
- Кнопка «Закрепить плитку» — `LiveTileService.TryPinTileAsync()` через
  `Windows.UI.StartScreen.SecondaryTile` (WinRT-рефлексия, без MSIX).
  Видна только если `IsPinningSupported()` истинно (Windows 10/11).

### 2.6 Скринсейвер — исправлена логика запуска из трея

Новый `_idleCheckTimer` (интервал 20 сек) в `MainWindow` проверяет
`SystemIntegration.IsSystemIdle(TimeSpan)` — обёртку над `GetLastInputInfo`
(**системный**, не оконный простой: клавиатура/мышь/тач по всей ОС).
Полноэкранный показ запускается, только если:

1. Окно скрыто в трей (`Visibility == Hidden`, а не просто `Minimized`).
2. `ScreensaverDelayMinutes > 0`.
3. Система реально простаивает ≥ этого порога.

Это отдельно от **системной** регистрации `.scr` (та продолжает работать
как раньше через `RegisterScreensaver`/`Control Panel\Desktop`).

### 2.7 Автоотключение рамки по расписанию (новое)

`Services/AutoOffScheduler.cs` — три независимых режима
(`AppSettings.AutoOffMode`):

| Режим | Логика |
|---|---|
| `SmartUsage` | Скользящее окно 4 часа: если система была активна < 15% времени — считаем "нерабочим" временем, сворачиваем |
| `ManualSchedule` | Простое "с ЧЧ:ММ до ЧЧ:ММ" (`AutoOffFromMinutes`/`AutoOffToMinutes`), поддерживает переход через полночь |
| `SunsetToSunrise` | `SystemIntegration.CalculateSunTimes()` — упрощённая формула NOAA (без интернета); координаты — вручную или через `TryGetApproxLocationAsync()` (ip-api.com, кеш 12ч) |

Не путать с системным скринсейвером/питанием — `AutoOffScheduler` просто
скрывает/показывает окно PhotoFrame (`Hide()`/`Show()`), не трогая
настройки Windows.

### 2.8 Просмотр USB-устройств (Lumia/Android/iOS)

`Services/DeviceBrowserService.cs` + `Views/DeviceBrowserWindow.xaml(.cs)`.

Технически: устройства без буквы диска под "Этот компьютер" видны только
через `Shell.Application` COM (`Windows.Storage`/MTP API в WPF/.NET 8 без
UWP-упаковки не тривиален). Используется позднее связывание (`dynamic`,
`Type.GetTypeFromProgID`) — не требует COM-референса в `.csproj`, только
пакет `Microsoft.CSharp` для поддержки `dynamic`.

Импорт копирует файлы через `Shell.Folder.CopyHere()` в
`%LocalAppData%\PhotoFrame\DeviceImports\{Устройство}\`, после чего этот
путь добавляется в `SelectedPaths` как обычная папка — `FileScanner`
работает с ним без изменений.

**Ограничение**: `CopyHere` — асинхронная shell-операция без промиса;
используется поллинг количества файлов (таймаут 5 минут).

### 2.9 GPS → название места на русском

`Services/ReverseGeocodeService.cs` — OpenStreetMap Nominatim, без ключа,
rate-limit 1 запрос/сек (политика Nominatim), кеш в памяти по округлённым
координатам. Опция **Настройки → Наложения → «Определять название места
через интернет»** (`AppSettings.GpsReverseGeocodeEnabled`, по умолчанию
выключено — без согласия пользователя сетевые запросы не выполняются).

### 2.10 Баг: счётчик кадров показывал неверную позицию

`PlaylistManager.CurrentIndex` возвращал индекс в **исходном** списке
фотографий (`_source`), а не позицию в **очереди воспроизведения**
(`_order`). В режимах `Shuffle`/`TrueRandom` счётчик "N / всего" в
интерфейсе показывал случайное число, не совпадающее с реальным порядком
показа, а проверки "это первое/последнее фото" (`Start`/`EndUnavailable`
иконки) срабатывали неправильно.

**Исправление**: добавлены `PlaybackPosition` (0-based позиция в очереди),
`IsAtStart`, `IsAtEnd` — используются в `SyncCounter()` и
`ApplyToolbarIcons()` вместо `CurrentIndex`. `CurrentIndex` сохранён для
`JumpToSource()` (не изменяет публичный контракт).

### 2.11 Центрирование тулбара

Блок навигации (Назад/Play/Вперёд) был `Grid.Column="1"` внутри
трёхколоночного `Grid` — центрировался только относительно средней "*"
колонки. Левая группа (PlayMode + интервал, ≈194px) и правая (Тема +
Настройки + Полный экран, ≈168px) не равны по ширине → визуальное смещение
≈13px от истинного центра окна.

**Исправление**: `Grid.Column="0" Grid.ColumnSpan="3" Panel.ZIndex="1"` —
блок теперь центрируется относительно всей ширины тулбара независимо от
асимметрии боковых групп.

### 2.12 Иконки экспорта (список/CSV)

Было: `&#xE896;` (Download — семантически неверно для «список файлов») и
`&#xE8A1;` (нечёткий смысл для CSV). Заменены на `&#xE8E5;` (документ со
строками — список) и `&#xE9D9;` (таблица/сетка — CSV).

---

## 3. Известные ограничения build 52

- `DeviceBrowserService` — доступ к MTP-устройствам через Shell COM может
  требовать, чтобы устройство было разблокировано и в режиме "Передача
  файлов" (для Android) / доверия компьютеру (для iOS через iTunes/Apple
  Mobile Device Support).
- `SunsetToSunrise` без ручных координат зависит от IP-геолокации
  (`ip-api.com`) — точность на уровне города, не GPS.
- `AutoOffScheduler.SmartUsage` требует ~24 минут работы (6 семплов по 4
  мин) для накопления данных, прежде чем начнёт принимать решения — до
  этого считает рамку активной.
