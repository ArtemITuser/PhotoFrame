# PhotoFrame — ответы и пояснения (v1.2.0.3, build 46)

## Слияние с форком v1.2.0.2 (build 45)

Из форка перенесены и доработаны:

- **`Themes/AeroTheme.xaml`** — полная Aero7-тема (784 строки): градиенты
  `AeroBlueFaceGrad`/`AeroSilverFaceGrad`/`AeroGlossGrad`, переопределение
  `AccentButton`, `SecondaryButton`, `NavItem`, `SectionHeader`, плюс новые
  стили `Aero7ToolbarButton`, `Aero7CaptionButton`, `Aero7CloseButton`
  (красный hover у крестика), `Aero7ScrollBar`, кастомный `Slider`.
- **`Helpers/IconHelper.cs`** v4.2 — `MakeAeroStateIcon()`/`SwapAeroStateIcon()`:
  создаёт `Image` со стилем из двух `DataTrigger` (`IsMouseOver`→hover PNG,
  `IsPressed`→pressed PNG), привязанных к `RelativeSource FindAncestor Button`.
  **Никаких обработчиков событий** — переключение состояний полностью на
  биндингах WPF.
- **`Models/AppSettings.cs`** — поле `AutostartEnabled` подтверждено.

## Что сделано в этой сборке

### 1. Все состояния кнопок подключены как изображения

| Кнопка | Aero7 (по состоянию) | Modern (Segoe MDL2) |
|---|---|---|
| Play/Stop (центр) | `play/playHover/playClicked/playDisabled` ↔ `stop/stopHover/stopClicked` | `\uE768`/`\uE769` |
| Назад | `start/startHover/startClicked/startUnavailable` | `\uE892` |
| Вперёд | `end/endHover/endClicked/endUnavailable` | `\uE893` |

`startUnavailable`/`endUnavailable` показываются автоматически, когда
`LoopSlideshow=false` и текущее фото — первое/последнее. `playDisabled` —
когда плейлист пуст. Обновляется в `ApplyToolbarIcons()`, вызывается из
`AdvanceAsync`/`GoBackAsync`/`OnLoaded`/после применения настроек.

### 2. Aero-тема улучшена

- `App.ApplyUiMode(UiMode)` подключает/отключает `AeroTheme.xaml` в
  `MergedDictionaries` **после** `CommonStyles.xaml` — ключи Aero побеждают
  по правилам поиска ресурсов WPF без дублирования словарей.
- `MainWindow.ApplyUiModeStyles()`:
  - Aero7 → `BtnWinMinimize/Maximize.Style = Aero7CaptionButton`,
    `BtnWinClose.Style = Aero7CloseButton`, тулбар → `Aero7ToolbarButton`.
  - Modern → `ClearValue(StyleProperty)` для кнопок заголовка (исходный вид
    WPF-чрома) и `SetResourceReference(StyleProperty, "ToolbarButton")` для
    тулбара — восстанавливает `{DynamicResource ToolbarButton}` из XAML.
- Live-превью в Настройках: `CmbUiMode.SelectionChanged` сразу зовёт
  `App.ApplyUiMode()` — видно до нажатия «Применить».

### 3. WinUI 3 (Modern) — теперь дефолт

`AppSettings.UiMode = UiMode.Modern` по умолчанию. Aero7 — опция в
Настройки → Внешний вид → «Режим интерфейса (экспериментально)».

### 4. Заголовок без номера версии

`Title = "PhotoFrame"`, `TbTitleVersion.Text = "PhotoFrame"` — без `v1.2.0.3`.
Номер версии остался в Настройки → О программе (читается из
`Assembly.GetExecutingAssembly().GetName().Version`).

### 5. MDL2-иконки — сохранено рабочее

Modern-режим не тронут: те же глифы `\uE892/\uE893/\uE768/\uE769` и т.д.
`IconHelper.RestoreMdl2()` теперь работает по `sp.Children[0]`, а не по
поимённым `TbPrevIcon/TbNextIcon/TbPlayIcon` — корректно восстанавливает
глиф даже после переключений Aero7↔Modern в одной сессии (раньше был риск
«осиротевшего» TextBlock).

---

## ClickOnce vs InnoSetup (NetFix) — сравнение для PhotoFrame

Ваш коллега из NetFix (`rupleide/NetFix`) использует **полностью ручной**
цикл: `dotnet build -c Release` → `iscc Setup.iss` → загрузка `.exe` в
GitHub Releases вручную → собственный `UpdateService.cs` дёргает
`/releases/latest`, сравнивает `tag_name` с
`Assembly.GetExecutingAssembly().GetName().Version`, скачивает `.exe`,
`Process.Start` + `Application.Current.Shutdown()`.

**PhotoFrame уже на ступень выше** — у вас есть GitHub Actions
(`.github/workflows/`), который при пуше в `master`: собирает Release →
публикует ClickOnce-манифест на `gh-pages` → создаёт GitHub Release.
Пользователь получает обновление **автоматически** при следующем запуске
(ClickOnce сам проверяет манифест) — без отдельного `UpdateService`.

| | **ClickOnce (текущий)** | **InnoSetup (NetFix-стиль)** |
|---|---|---|
| Сборка | GitHub Actions, автоматически | Ручной `dotnet build` локально |
| Публикация | Авто на `gh-pages` | Ручная загрузка `.exe` в Releases |
| Обновление у юзера | Автоматическая проверка манифеста при запуске | Свой `UpdateService` + кнопка «Обновить» |
| Установка | Без прав администратора (per-user) | Может требовать UAC (`requireAdministrator`) |
| Скринсейвер/автозагрузка | Работает (HKCU, без UAC) | Может ставить в `Program Files` → нужен UAC |
| Размер репозитория | Бинарники не хранятся в git (gh-pages — отдельная ветка) | `.exe` коммитится в Releases (не в код) |
| Подходит для | Soло-разработчик, минимум обслуживания | Нужен кастомный UI обновления, доп. компоненты (как Zapret) |

**Рекомендация:** оставить ClickOnce как основной канал — он у вас уже
автоматизирован через Actions и не требует ручных шагов. InnoSetup стоит
добавлять **только** если понадобится:
1. Распространять доп. бинарники (как Zapret/TgWsProxy у NetFix), которые
   ClickOnce не умеет ставить вне `%LocalAppData%`.
2. Кастомный экран обновления с прогресс-баром скачивания (ClickOnce даёт
   только системный диалог).
3. Установка в `Program Files` с правами администратора (например, для
   глобальной регистрации `.scr` без `cmd /c copy /y` — но текущий
   `ElevatedCopy` в `SystemIntegration.cs` уже решает это через UAC-промпт
   точечно, без полной переустановки).

Если всё же захотите портативный `.exe` параллельно с ClickOnce — добавьте
второй workflow-джоб: `dotnet publish -r win-x64 --self-contained false -c
Release` → `iscc Installer.iss` → отдельный артефакт в том же Release.
Манифест ClickOnce и `.exe` от Inno Setup не конфликтуют — это два разных
файла в одном GitHub Release.
