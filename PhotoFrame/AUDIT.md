# PhotoFrame v1.2.0.9 (build 51) — отчёт аудита

*(Дополнение по build 52 — см. в конце файла.)*

Дата: 04.07.2026 · Автор изменений: Claude (по заданию Ochkin Artyom / ArtemITuser)
Область: код, унаследованный от build 46 + повторно применённый функционал
build 50, плюс новые изменения build 51.

Компиляция и запуск в этом окружении **не проверялись** — здесь нет
.NET SDK / доступа к NuGet. Ниже — статический просмотр кода: баланс
скобок, валидность XAML (проверено `python3 xml.etree`), последовательный
разбор владения ресурсами. Перед публикацией собрать и прогнать в Visual
Studio 2026 (net8.0-windows10.0.19041.0) — см. `DEPLOYMENT.md`.

## 1. Сборка мусора / управление ресурсами

| Место | Было | Стало | Причина |
|---|---|---|---|
| `SystemIntegration.DownloadAndInstallMdl2FontAsync` | `using var http = new HttpClient(...)` | общий статический `_sharedHttp` + `CancellationTokenSource` | Каждый `new HttpClient()` держит собственный пул сокетов; при `Dispose()` сокет уходит в `TIME_WAIT` — при частых вызовах (проверка обновлений при каждом старте, повторные попытки геолокации) это истощает порты. Классическая для .NET ошибка ("HttpClient is intended to be instantiated once"). |
| `SystemIntegration.TryGetApproxLocationAsync` | то же | то же | То же |
| `SystemIntegration.CheckUpdateAsync` | `http.DefaultRequestHeaders.Add(...)` на одноразовом клиенте | `HttpRequestMessage` с собственными заголовками на общем клиенте | На общем клиенте `DefaultRequestHeaders.Add` при повторных вызовах накапливал бы дублирующиеся `User-Agent`. |
| `DeviceBrowserService` (все методы) | `dynamic shell/folder/item` без освобождения | `Marshal.FinalReleaseComObject` в `finally` на каждом уровне рекурсии | Поздне-связанные COM RCW (Shell.Application/Folder/FolderItem) не финализируются немедленно — GC не знает об истинной "тяжести" нативного COM-объекта. При частом открытии браузера устройств (глубокая рекурсия по дереву папок телефона) это может надолго удерживать shell-namespace хэндлы на нативной стороне explorer.exe. |
| `App.ApplyTheme` | цикл поиска словаря `*Theme.xaml` мог задеть `AeroTheme.xaml`/`AeroDarkTheme.xaml` | эти два источника явно исключены | Не GC-баг, но смежная логическая ошибка, найденная при аудите того же модуля (могла привести к тому, что смена Светлая/Тёмная тема при активном Aero7 вместо замены базовой темы случайно удаляла Aero-оверлей). |

### Проверено — замечаний не найдено
- `LoadBitmapSafe` (MainWindow) и `LoadThumbSafe` (SettingsWindow) —
  `BitmapCacheOption.OnLoad` + `.Freeze()` уже применяются: файл
  закрывается сразу после декодирования, а замороженный `BitmapImage`
  безопасно живёт в общем/UI-потоке без дополнительных ссылок на поток.
- `MainWindow.OnClosing` — все `DispatcherTimer` (`_slideTimer`,
  `_hideTimer`, `_counterTimer`, `_idleCheckTimer`) останавливаются,
  `AutoOffScheduler` останавливается и отписывается
  (`ShouldBeActiveChanged -=`), `FileSystemWatcher` (`_driveWatcher`)
  останавливается и освобождается, `NotifyIcon` (`_tray`) освобождается.
- `App.ThemeChanged` (статическое событие) — подписка в `MainWindow`
  симметрично снимается в `OnClosing`; без этого статический источник
  держал бы окно живым до выхода из процесса (типичная утечка WPF при
  подписке на статические события из недолговечных окон).
- `ReverseGeocodeService` — уже был реализован образцово: один
  статический `HttpClient`, `SemaphoreSlim` для rate-limit (1 запрос/сек,
  требование Nominatim ToS), `ConcurrentDictionary` как кеш по координатам.
- `TransitionEngine` — обработчики `Storyboard.Completed` анонимны и
  одноразовы (замыкание на локальный `Storyboard`), после завершения
  анимации объект и обработчик становятся недостижимы естественным
  образом — утечки не создают.

## 2. Функциональные проверки по пунктам ТЗ

- **Счётчик кадров считает корректно** — использует
  `PlaylistManager.PlaybackPosition` (позиция в очереди воспроизведения),
  а не `CurrentIndex` (индекс в исходном списке) — исправление из build 50
  подтверждено в коде build 51, регрессий не внесено.
- **Центрирование кнопок воспроизведения** — навигационный `StackPanel`
  использует `Grid.ColumnSpan="3"`, что даёт истинный центр всего тулбара,
  а не центр средней колонки (как было до build 50).
- **Центрирование счётчика** — `CounterBadge` использует
  `HorizontalAlignment="Center"` на `RootGrid` верхнего уровня — центр
  окна целиком, симметрично навигации.

## 3. Известные ограничения (не исправлялись в build 51, отмечены для будущих версий)

- `Aero7ToolbarButton`/`Aero7CaptionButton`/`Aero7ScrollBar`/`Slider`-стили
  в `AeroTheme.xaml` не продублированы в `AeroDarkTheme.xaml` — они и так
  используют сине-стеклянную палитру, рассчитанную на тёмный `ToolbarBgBrush`,
  поэтому визуально приемлемы в обоих вариантах. Если потребуется более
  выраженный "чёрный Aero" для этих элементов — вынести их литеральные
  цвета в `DynamicResource` по аналогии с базовой палитрой.
- Импорт `.txt` полагается на формат дерева, генерируемый текущим
  экспортом (`📁 путь` / `   • файл`); произвольные пользовательские
  текстовые файлы поддерживаются, только если каждая строка — абсолютный
  путь (эвристика по наличию `:\` или `\\`).
- COM-релиз в `DeviceBrowserService` останавливает утечку RCW, но
  `Shell.Application.NameSpace(...).Items()` для очень больших MTP-папок
  (тысячи файлов) остаётся синхронным вызовом — для будущих версий стоит
  рассмотреть постраничную загрузку.

---

## Дополнение — build 52 (11.07.2026)

### Новые находки и исправления

| Место | Проблема | Исправление |
|---|---|---|
| `SystemIntegration.SetAutostart` | `Assembly.GetExecutingAssembly().Location` — пустая строка для single-file публикации, путь к `.dll` (не запускается напрямую) для обычной .NET 5+ публикации, нестабильный путь для ClickOnce (меняется при каждом апдейте) | `ResolveHostExecutablePath()`: ClickOnce → `.appref-ms` ярлык; иначе `Environment.ProcessPath`; MSIX → делегирует на `windows.startupTask`, реестр не трогает |
| `SystemIntegration.RegisterScreensaver` | Тот же `Assembly.Location` для копируемого в System32 файла | Заменён на `ResolveHostExecutablePath()`; добавлена детализация ошибок (`UnauthorizedAccessException`/`IOException`/`SecurityException` раздельно) |
| `Themes/CommonStyles.xaml` | `TextBox`/`ScrollBar` не имели стиля — рендерились системными (белое поле/серый скроллбар) вне зависимости от темы | Добавлены implicit-стили с `DynamicResource` на `InputBgBrush`/`InputFgBrush`/`InputBorderBrush`/`Surface2Brush`/`AccentBrush` |
| `LiveTileService` | Все 4 биндинга получали необработанное фото с `hint-crop="none"` при несовпадающих пропорциях (квадрат vs 2.07:1) | Предварительное кадрирование по центру в два файла (квадрат/широкий) с управляемым зумом (`TilePhotoDistance`); временные файлы перезаписываются, не накапливаются |
| `AutoOffScheduler.Start()` | Повторный вызов без `Stop()` добавил бы ещё один обработчик `Tick` (не проявлялось при текущем паттерне вызова — только 1 раз при старте) | Сделан идемпотентным (`_started` guard), `Stop()` симметрично отписывает обработчик |
| Обновления | Проверка только вручную — не было периодической автопроверки | `AutoCheckUpdatesEnabled` + `UpdateCheckPeriodDays` + `LastUpdateCheckUtc`; таймстемп обновляется только при успешном обращении к серверу — не наказывает пользователя за офлайн-запуски частыми повторами при следующем старте, но и не проверяет чаще заданного периода |

### Проверено — замечаний не найдено (build 52)
- Переход "Ночной режим" в/из активного состояния — расписание с
  переходом через полночь, событие только на смену состояния (не на
  каждый тик) — логика корректна и не изменялась.
- Общий `HttpClient` (введён в build 51) — использован и для нового
  периодического автообновления, отдельного клиента не создавалось.

### Явно не проверялось (нет доступа из этого окружения)
- Актуальность версий NuGet-пакетов (`System.Management 8.0.0`,
  `Microsoft.CSharp 4.7.0`) — сеть до nuget.org недоступна в песочнице,
  где выполнялась эта работа. `TargetFramework` оставлен без изменений
  (`net8.0-windows10.0.19041.0`, LTS). Рекомендация: `dotnet list package
  --outdated` локально перед релизом.
- Реальная сборка/запуск (`dotnet build`) — в этом окружении нет .NET SDK.
  Статическая проверка: баланс скобок всех `.cs` — без несовпадений; все
  `.xaml` (включая новый `Package/Package.appxmanifest`) — валидный XML.
- Работоспособность `Package.appxmanifest`/MSIX-упаковки при реальной
  сборке через `makeappx`/`.wapproj` — манифест составлен по
  задокументированной схеме AppxManifest, но не прогонялся через
  `makeappx pack` (инструмент недоступен в песочнице). Проверить в VS
  перед публикацией.

---

## Дополнение — build 54 (01.08.2026)

Продолжение работы поверх build 52 по конкретному заданию: живые плитки
(регистрация + все 4 размера), иерархический просмотр MTP-устройств,
причина COMException, сон/пробуждение с сохранением состояния,
touch-friendly аудит контролов, совместимость x86, снижение частоты
всплывающих ошибок и ложных срабатываний для съёмных USB-накопителей,
проверка на утечки памяти.

### Новые находки и исправления

| Место | Проблема | Исправление |
|---|---|---|
| `LiveTileService.GetUpdater()` | `CreateTileUpdaterForApplication("ArtemITuser.PhotoFrame")` — строковый Id не совпадал с `Application Id="PhotoFrame"` в манифесте (перегрузка ищет Id среди НЕСКОЛЬКИХ Application-записей пакета) — обновление тихо не находило приложение | Беспараметрный `CreateTileUpdaterForApplication()` — "текущее приложение пакета", не зависит от строки Id |
| `LiveTileService.TryPinTileAsync()` | `Activator.CreateInstance` вызывался с аргументом-строкой там, где `SecondaryTile`-конструктору нужен `Uri` логотипа — reflection не находил совпадающий конструктор, всегда падал в `catch` | Переписано на `SecondaryTile(tileId)` + установка свойств (`DisplayName`/`Arguments`/`VisualElements`/`DesiredSize`) по отдельности |
| `LiveTileService.IsPinningSupported()` | Проверялось только наличие WinRT-типов (доступны в ОС независимо от package identity) — кнопка закрепления показывалась и там, где закрепить физически невозможно (ClickOnce/InnoSetup) | Добавлена проверка `SystemIntegration.IsRunningAsMsixPackage()` |
| `PhotoFrame.csproj` | `WindowsPackageType=MSIX`/`EnableMsixTooling=true` включены БЕЗ УСЛОВИЯ — критический баг, ломающий абсолютно любую сборку (обычный `dotnet build`, Release-публикацию для InnoSetup), т.к. заставлял искать `Package.appxmanifest`/`Images\*.png` по несуществующим путям | Убраны из безусловной `PropertyGroup`; рабочий путь к MSIX остаётся прежним — отдельный `Package\Package.appxmanifest` |
| Корень проекта | Второй, битый манифест `package.appxmanifest` (строчная "p") — недействительные пространства имён `http://microsoft.com`, без `<Identity>`, неподставленный `$targetnametoken$.exe` | Удалён — единственный манифест теперь `Package\Package.appxmanifest` |
| `AppSettings.LiveTilesLargeEnabled` | Существовал, но нигде не читался/не записывался — "мёртвая" настройка, крупную плитку нельзя было выключить из интерфейса | Реально применяется в `UpdateInternal` (исключает биндинг `TileLarge`), добавлен флажок в разделе Live Tiles |
| `DeviceBrowserService` (весь файл) | Вложенные MTP-папки резолвились через `shell.NameSpace(node.ShellPath)` — ненадёжно для вложенных MTP-путей; дерево жёстко ограничено 3 уровнями (`GetDeviceTree(maxDepth: 3)`) | Переписан: `DeviceNode` хранит корень+путь имён, резолвинг — один `NameSpace()` на корне + пошаговый обход `.Items()/.GetFolder`; дерево строится лениво по одному уровню без ограничения глубины (см. `GetChildren`) |
| `DeviceBrowserService` (тайминги) | Блокирующие COM-вызовы (`Items()`/`GetFolder`/`NameSpace`) не имели таймаута — не отвечающее/уснувшее устройство могло подвесить окно браузера | `RunBlocking<T>`: работа на отдельном потоке + `Task.Wait(15s)`, при таймауте возвращается безопасное значение по умолчанию |
| `Views/DeviceBrowserWindow.xaml.cs` | Ленивое раскрытие (плейсхолдер "Загрузка…" + `Expanded`) было реализовано только для узла устройства верхнего уровня | Единый `OnTreeItemExpanded`, применяется рекурсивно на каждом уровне через `BuildLazyTreeItem` |
| `App.xaml.cs` `DispatcherUnhandledException` | Перехватывало и показывало диалог для ЛЮБОГО исключения, включая штатный, самоустраняющийся `UCEERR_RENDERTHREADFAILURE` (0x88980406) после resume/сброса видеодрайвера | Этот HRESULT распознаётся отдельно и подавляется без диалога (только Trace); остальные диалоги не чаще 1/5с |
| Сон/пробуждение | Не было обработки `SystemEvents.PowerModeChanged` вообще — состояние (полноэкранный режим, воспроизведение) не переживало ничего, кроме удержания процесса в памяти | Обработчик с сохранением состояния перед Suspend и мягким `InvalidateVisual()` после Resume; новые настройки `RestoreLastSessionState`/`WasFullscreen`/`WasPlaying` |
| `FileScanner.ScanAsync` | Отсутствующий путь (в т.ч. просто не воткнутая USB-флешка) всегда порождал `DiskError` — независимо от того, съёмный это носитель или нет | Новый параметр `removableRoots`; пути из `AppSettings.RemovableSourcePaths` при отсутствии тихо пропускаются, не считаются ошибкой |
| `Properties/PublishProfiles/FolderProfile1.pubxml` (факт. единственный рабочий профиль для InnoSetup) | `PublishTrimmed=true` — Microsoft документирует WPF как небезопасный для trimming; вместе с `dynamic` COM (`DeviceBrowserService`) и WinRT-reflection (`LiveTileService`) — реальный риск Release-only падений | `PublishTrimmed=false`; профили переименованы в явные `FolderProfile.x86.pubxml`/`FolderProfile.x64.pubxml`, удалены задвоенные/устаревшие `FolderProfile.pubxml` (на деле — копия ClickOnce-профиля с версией 1.2.0.3) и `FolderProfile1.pubxml` |
| `Installer.iss` / `.github/workflows/build-release.yml` | `PublishDir` в Installer.iss (`...\publish\`) не совпадал с тем, что публиковал CI (`-o publish` в корне репозитория) — при последовательном запуске в CI установщик собрался бы из пустой/устаревшей папки | `Installer.iss` параметризован по архитектуре (define `AppArch`, x86 или x64), CI публикует через `FolderProfile.x86`/`.x64` (тот же путь, что и локально), сборка обеих архитектур отдельными джобами |
| `Themes/CommonStyles.xaml` | Бегунок `Slider` — стандартный WPF-визуал (~11px), тяжело точно подцепить пальцем; подписи `HoverLabel` под кнопками тулбара видны только при `IsMouseOver` (недоступно на чистом тач-вводе) | Добавлен увеличенный `Thumb`-стиль (16px/20px при драге); `HoverLabel` дополнительно раскрывается по `IsPressed` |
| `Views/SettingsWindow.xaml` (импорт) | Кнопки «Список (.txt)» и «CSV» в разделе импорта путей обе получили глиф `&#xE896;` — потеря различия, введённого в build 51 (регрессия при переносе в отдельные `TextBlock`, судя по оставленному в файле комментарию) | Восстановлено: Список — `&#xE896;`, CSV — `&#xE8A1;` |
| `Views/DeviceBrowserWindow.xaml` | Иконка «Обновить» — `Height="13"` на `TextBlock` с MDL2-глифом без явного `FontSize` мог обрезать сам символ | `FontSize="13"` + `VerticalAlignment="Center"`, без жёсткой высоты — как и остальные MDL2-иконки в приложении |
| `Views/SettingsWindow.xaml.cs` | Два пустых, автосгенерированных дизайнером обработчика (`ListBoxItem_Selected`, `ListBoxItem_Selected_1`), второй — подключён к пункту «About» и ничего не делал | Удалены оба метода и привязка `Selected=` в XAML — выбор и так полностью обрабатывает `NavList.SelectionChanged` |
| `Services/UpdateService.cs`, `Views/UpdateWindow.xaml(.cs)` | Полностью осиротевший код (не вызывается из `App`/`MainWindow`/`SettingsWindow`) — более ранняя параллельная реализация, скачивающая и запускающая .exe с настраиваемого URL без подтверждения на уровне сервиса; заново вводит антипаттерн `new HttpClient()` на каждый вызов, уже исправленный в основном коде | Удалены — действующая, подключённая реализация (`SystemIntegration.CheckUpdateAsync`, открывает страницу релиза в браузере, ничего не скачивает/не запускает сама) не затронута |
| `Properties/PublishProfiles/ClickOnceProfile.pubxml` | `ApplicationVersion`/`MinimumRequiredVersion` = `1.0.8.6`/`1.0.8.5` — по всей видимости, не обновлялись с одной из самых первых версий приложения; иконка — `Media001.ico` вместо используемого везде `AppIcon.ico` | Версии приведены к текущему релизу (`MinimumRequiredVersion` на 1 ревизию позади — фоновое, не блокирующее обновление, как и было); иконка — `AppIcon.ico` |

### Проверено — замечаний не найдено (build 54)
- P/Invoke-сигнатуры в `SystemIntegration.cs` (хэндлы окон, память,
  спящий режим) — типы параметров (`IntPtr`, `uint`) корректны для
  x86/x64, доступ не через `int` там, где ожидается указатель/хэндл.
- `_driveWatcher` (`ManagementEventWatcher`) — уже был симметрично
  `Stop()`+`Dispose()` в `OnClosing` и при пересоздании в обработчике
  настроек; изменений не потребовалось.
- `_tray.BalloonTipClicked` — локальный самоотписывающийся обработчик;
  утечки не обнаружено, паттерн не менялся.
- COM-релиз (`Marshal.FinalReleaseComObject`) в переписанном
  `DeviceBrowserService` — сохранён на каждом уровне вложенности нового
  `ResolveNode`/`GetChildren` по аналогии с build 52.

### Явно не проверялось (нет доступа из этого окружения)
- Реальная сборка/запуск (`dotnet build`) — по-прежнему нет .NET SDK в
  этой песочнице. Статическая проверка: баланс скобок всех изменённых
  `.cs` — без несовпадений; все изменённые `.xaml`/`.appxmanifest` —
  валидный XML; `.github/workflows/build-release.yml` — валидный YAML.
  Отдельно прогнан скрипт поиска "смешанных" русских/латинских слов
  (частый источник опечаток при написании кириллических комментариев) —
  найденные реальные опечатки исправлены (ложные срабатывания вида
  `\nДиск` — это `\n` + слово, не опечатки).
- Реальное поведение `Windows.UI.StartScreen.SecondaryTile`/
  `TileUpdateManager` под MSIX — исправления в `LiveTileService.cs`
  основаны на задокументированной сигнатуре API (WinRT projection), но
  reflection-вызовы не выполнялись вживую (нет Windows-окружения с
  установленным MSIX-пакетом в песочнице). Проверить закрепление и
  обновление плитки на реальном MSIX-инсталле перед релизом.
- Реальная сборка через `makeappx pack`/`.wapproj`,
  `iscc "/DAppArch=x64" Installer.iss"` и `dotnet publish` с новыми
  `FolderProfile.x86`/`.x64` — инструменты недоступны в песочнице.
  Проверить оба архитектурных варианта в VS2026 перед публикацией.

---

## Дополнение — build 56 (04.08.2026)

MSIX(bundle) как рабочий, дополнительный канал сборки; исправление
реальной причины полного отказа MTP-браузера устройств; кроссканальная
система обновления с реальным скачиванием/запуском; слияние с независимо
доработанной пользователем веткой (см. CHANGELOG.md, раздел "Слияние
веток" — там же явно перечислено, что взято, а что сознательно нет, и
почему).

### Новые находки и исправления

| Место | Проблема | Исправление |
|---|---|---|
| `PhotoFrame.Package.wapproj` (не существовал) + `PhotoFrame.csproj` | `.wapproj`, создаваемый мастером VS, не умеет забирать publish- (только build-) вывод SDK-style проекта → "Manifest references file 'PhotoFrame.exe' which is not part of the payload" при любой попытке собрать MSIX | Именованная цель `__GetPublishItems` в `.csproj` (`ComputeFilesToPublish`/`ResolvedFileToPublish`) + готовый `.wapproj`, запрашивающий её через `PackageOutputGroups`, с явной предпубликацией референса (`PublishReferences` before `ExpandProjectReferences`) |
| `PhotoFrame.csproj` | `RuntimeIdentifier`, передаваемый через `ProjectReference/Properties` из `.wapproj`, не находил бы pre-restored assets | Добавлен `<RuntimeIdentifiers>win-x86;win-x64</RuntimeIdentifiers>` (множественное число) в основной `PropertyGroup` |
| `Services/DeviceBrowserService.cs`, `GetConnectedDevices()` | `path[1] == ':'` (фильтр "это диск, пропустить") ложно совпадал с shell parsing path MTP/AFC-устройств вида `::{GUID}...` — там символ [1] тоже `':'`. Отбрасывались ВСЕ настоящие устройства, что и было главной причиной полного отказа браузера устройств | Дополнительно требуется, чтобы первый символ пути был буквой: `char.IsLetter(path[0]) && path[1] == ':'` |
| `Services/SystemIntegration.cs` (обновления) | `CheckUpdateAsync` не читал `assets` релиза вообще — только `tag_name` вручную по строке; скачать/запустить обновление было нечем | Новый `Services/SystemIntegration.Updates.cs`: `System.Text.Json`-парсинг `assets`, подбор файла по архитектуре ОС (включая x86-32/x32/anycpu/universal токены) и каналу установки, распаковка zip при необходимости, запуск через `ShellExecute` без silent-флагов/принудительного UAC |
| `.github/workflows/build-release.yml` | MSIX не собирался в CI вообще | Добавлен отдельный, `continue-on-error: true` job — не может повлиять на публикацию ClickOnce/InnoSetup, даже если сам начнёт падать |

### Проверено — замечаний не найдено (build 56)
- STA-поточная модель `DeviceBrowserService` (введена ранее) — не
  требовала изменений, независимо подтверждена той же гипотезой в
  пользовательской ветке.
- `LiveTileService.cs` из пользовательской ветки сверен построчно на
  предмет регрессии — использует ту же беспараметрную
  `CreateTileUpdaterForApplication()` и тот же `SecondaryTile(tileId)` +
  раздельная установка свойств, что и текущая версия; содержательных
  отличий, кроме структуры кода, не найдено — оставлена текущая версия
  без слияния этого конкретного файла.
- `Installer.iss`/`AppId` — уже обеспечивает корректное обновление поверх
  старой версии средствами самого InnoSetup; отдельной логики "удалить
  старую версию" в коде не требуется и не добавлялось.

### Явно не проверялось (нет доступа из этого окружения)
- Реальная сборка `PhotoFrame.Package.wapproj` через MSBuild/VS2026 —
  логика цели `__GetPublishItems`/`PackageOutputGroups` основана на
  задокументированном и подтверждённом на практике (независимыми
  источниками, включая разбор той же ошибки на .NET 8 в 2024 году) приёме,
  но не выполнялась вживую в этой песочнице (нет Windows/MSBuild/Windows
  SDK). Проверить сборку под x86 И x64 в VS2026 перед публикацией — см.
  таблицу диагностики в `Package/README_MSIX.md`, если что-то пойдёт не так.
- Реальное поведение исправленного MTP-фильтра на конкретных устройствах
  пользователя (Lumia 950, POCO M8 Pro 5G, iPhone 13 mini, Coolpix S33) —
  исправление основано на анализе структуры shell parsing path, а не на
  тестировании с реальным оборудованием (недоступно в песочнице).
- Реальное поведение `SystemIntegration.Updates.cs` против настоящих
  GitHub Releases текущего репозитория — один URL из переписки
  (`user-attachments/files/.../PhotoFrame_v1.2.2.0_x86_Setup.zip`) удалось
  подтвердить как реально существующий и являющийся `.zip`
  (`content-type: application/x-zip-compressed`) через прямой запрос;
  остальные — по robots.txt GitHub недоступны для автоматического запроса
  из этого окружения. Структура ответа GitHub Releases API
  (`tag_name`/`assets[].name`/`assets[].browser_download_url`) — стабильный,
  документированный публичный контракт, а не то, что специфично для
  конкретного репозитория, так что парсинг не должен зависеть от того, что
  именно сейчас лежит в релизах.

