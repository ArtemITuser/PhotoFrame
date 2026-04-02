# 📋 Ответы на вопросы — PhotoFrame

*Язык: ru-RU | Версия приложения: 1.0.8.3*

---

## 1. UAC и права администратора

### Как это реализовано

Приложение запускается **без прав администратора** (`asInvoker` в `app.manifest`).
При операциях, требующих повышения:

- **Скринсейвер** (копирование `.exe` в `System32`) — сначала пробуем напрямую.
  Если `UnauthorizedAccessException` → запускаем `cmd.exe /c copy` с `Verb = "runas"`.
  Windows покажет стандартный диалог UAC. Пользователь видит что именно копируется.

- **Автозагрузка** (`HKCU\Software\...\Run`) — прав администратора **не требует**
  совсем, т.к. ключ пользовательский.

- **Параметры скринсейвера** (`HKCU\Control Panel\Desktop`) — тоже `HKCU`, без UAC.

- **Настройки питания** (`PowerWriteACValueIndex`) — иногда требует admin на
  корпоративных машинах. Если не получается — покажет сообщение.

### Почему не `requireAdministrator` в манифесте

Если поставить `requireAdministrator` — Windows **всегда** будет спрашивать UAC
при каждом запуске. Это плохой UX для фоторамки. Лучше запрашивать только когда нужно.

---

## 2. Кнопка PlayMode (режим воспроизведения)

Кнопка в тулбаре теперь **только 2 состояния**:

| Состояние | Иконка | Подпись | Поведение |
|---|---|---|---|
| Shuffle | ⇄ (E8B1) | Случайно | Перемешать без повторов |
| Sequential | ☰ (E8AC) | По порядку | По имени файла |

Расширенные режимы (по дате, TrueRandom) доступны в **Настройки → Воспроизведение**.

---

## 3. Touch / сенсорный экран

**Что изменено:**
- `IsManipulationEnabled="True"` перенесён на `Grid` (не `Window`) — более надёжно
- Все кнопки тулбара получили **подписи снизу** (9pt, белый полупрозрачный)
- Touch-target кнопок увеличен до 56×56px (норма для touch — минимум 44px)
- Тулбар высотой 76px с подписями — удобно нажимать пальцем

**Свайп:** вправо = назад, влево = вперёд, порог 70px.

---

## 4. Тёмная тема — видимость текста в настройках

**Проблема:** `TextBlock` без явного `Foreground` наследовал цвет от `Window`,
а в тёмной теме `DialogBgBrush` тёмный — текст сливался с фоном.

**Решение:** в `CommonStyles.xaml` базовый стиль `TextBlock` теперь явно задаёт
`Foreground="{DynamicResource TextPrimary}"`. Оба словаря темы определяют
`TextPrimary` корректно: `#F3F3F3` (тёмная) и `#1A1A1A` (светлая).

---

## 5. Номер версии

Версия задаётся **один раз** в `PhotoFrame.csproj`:
```xml
<Version>1.0.8.3</Version>
<AssemblyVersion>1.0.8.3</AssemblyVersion>
<FileVersion>1.0.8.3</FileVersion>
```

Во всём приложении читается автоматически:
```csharp
var ver = Assembly.GetExecutingAssembly().GetName().Version;
// → Major.Minor.Build.Revision
```
Показывается в заголовке окна и в Настройки → О программе.

---

## 6. GitHub и проверка обновлений

### Способы проверки

В **Настройки → О программе** добавлено:

1. **Кнопка «Проверить обновления»** — запрашивает
   `https://api.github.com/repos/ArtemITuser/PhotoFrame/releases/latest`
   и сравнивает `tag_name` с текущей версией. Без внешних зависимостей (System.Net.Http).

2. **Кнопка «Открыть страницу релизов»** — открывает браузер.

3. **Ссылка на репозиторий** — кликабельная, открывает `github.com/ArtemITuser/PhotoFrame`.

### Использование raw.githubusercontent.com

Для простой текстовой проверки версии можно создать файл
`version.txt` в репозитории и читать его:
```
https://raw.githubusercontent.com/ArtemITuser/PhotoFrame/main/version.txt
```
Содержимое: `1.0.8.3`. Это проще и быстрее чем GitHub API (нет rate-limits для
анонимных запросов).

---

## 7. ClickOnce — советы

### Как это работает

ClickOnce — технология развёртывания Microsoft. При публикации VS создаёт:
- `PhotoFrame.application` — файл-манифест для установки/обновления
- `Application Files/PhotoFrame_X_X_X_X/` — папка с версией приложения
- `setup.exe` — bootstrapper

Если разместить эти файлы на GitHub Pages или любом веб-хостинге, пользователь
кликает один раз — и приложение устанавливается. При следующем запуске оно
**автоматически проверяет обновления** на том же URL.

### Публикация через VS 2022

1. Правой кнопкой на проект → **Публикация**
2. Выбрать **ClickOnce**
3. Расположение публикации: папка (например `docs/` для GitHub Pages)
4. URL установки: `https://artemiтuser.github.io/PhotoFrame/`
5. **Build → Publish** при каждой новой версии

### GitHub Pages для ClickOnce

```
PhotoFrame репозиторий/
├── docs/           ← папка публикации, включить в Settings → Pages
│   ├── PhotoFrame.application
│   ├── setup.exe
│   └── Application Files/
│       └── PhotoFrame_1_0_8_3/
│           └── ...
└── src/            ← исходный код
```

В настройках репозитория: **Settings → Pages → Source: main / /docs**.

### Проблема с бинарниками на GitHub

WPF-приложение с .NET 8 в Release имеет ~60-120 МБ зависимостей.
**Варианты решения:**

| Вариант | Размер | Сложность |
|---|---|---|
| Framework-dependent (`--self-contained false`) | ~5-15 МБ | Нужен .NET 8 у пользователя |
| Self-contained single file | ~100+ МБ | Проблема с GitHub LFS |
| ClickOnce (framework-dependent) | ~5-10 МБ | ✅ Рекомендую |
| GitHub Releases + отдельный .zip | Любой | Вручную при каждой версии |

**Рекомендую:** ClickOnce (framework-dependent) + GitHub Pages для `docs/`.
Файлы весят мало, не нужен Git LFS, обновления автоматические.

Команда для публикации из командной строки:
```bash
dotnet publish -c Release -p:PublishProfile=FolderProfile
```

---

## 8. Итоговые изменения v3.1

| # | Что | Как |
|---|---|---|
| 1 | UAC для скринсейвера | `ShellExecute runas` через `cmd /c copy` |
| 2 | PlayMode кнопка | 2 состояния: Shuffle ↔ Sequential с подписью |
| 3 | Touch подписи | 56px кнопки + текст под иконкой |
| 4 | Тёмная тема текст | `TextPrimary` через `DynamicResource` в базовом стиле |
| 5 | Версия из Assembly | `csproj → AssemblyVersion → GetName().Version` |
| 6 | GitHub / обновления | Настройки → О программе; GitHub API без зависимостей |
| 7 | Стиль LinkButton | Для кликабельной ссылки на GitHub |

