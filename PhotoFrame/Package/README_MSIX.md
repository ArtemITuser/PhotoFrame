# PhotoFrame — упаковка в MSIX / MSIXBUNDLE (build 56)

Три канала распространения теперь сосуществуют, MSIX — третий,
**дополнительный**, не заменяющий два других:

| Канал | Файл | Кому подходит |
|---|---|---|
| InnoSetup | `Installer.iss` → `PhotoFrame_v*_x86_Setup.exe` / `_x64_Setup.exe` | Прямая установка, максимум контроля (System32 для скринсейвера, HKCU\Run) |
| ClickOnce | `Publish/` (VS "Publish" мастер) | Автообновление в один клик, без прав администратора |
| **MSIX / MSIXBUNDLE** | `PhotoFrame.Package.wapproj` + `Package/Package.appxmanifest` | Публикация в Microsoft Store либо корпоративный sideload через Intune/SCCM; **единственный канал, где реально работают Live Tiles** (см. ниже) |

Это по-прежнему официально рекомендуемый Microsoft подход для WPF —
single-project MSIX (тот, что не требует отдельного `.wapproj`) поддерживает
только WinUI 3 (по состоянию на май 2026,
learn.microsoft.com/windows/apps/package-and-deploy/publish-first-app,
раздел "Publish a WPF or WinForms app").

## build 56: теперь это уже готовый, а не создаваемый вручную проект

Раньше этот файл описывал, как СОЗДАТЬ `.wapproj` заново через мастер VS —
и именно в этом состоял риск: мастер VS генерирует "стандартный" `.wapproj`,
не рассчитанный на SDK-style self-contained публикацию, из-за чего сборка
падает с

```
Manifest references file 'PhotoFrame.exe' which is not part of the payload
```

**Причина** (если интересно) — устаревшие цели Desktop Bridge
(`Microsoft.DesktopBridge.targets`, на которые опирается `.wapproj`) писались
ещё для классических .NET Framework/UWP-проектов. Они умеют собирать payload
из ограниченного набора встроенных "output groups" ссылающегося проекта — и
НИ ОДНА из них не соответствует современной публикации в стиле SDK
(`dotnet publish`), где как раз и лежат self-contained рантайм-файлы и
нативный apphost. `.wapproj` в итоге ссылается в манифесте на exe, который
физически никогда не копируется в payload.

**Исправление** (уже в проекте, ничего создавать не нужно) — открытый,
задокументированный с эпохи .NET Core 3.0 приём (первоисточник: Claire
Novotny, "Packaging a .NET Core app with the Desktop Bridge", 2018; тот же
паттерн независимо подтверждается в разборах этой ошибки на современных
.NET 8 проектах, включая 2024 год): в `PhotoFrame.csproj` определена именованная
цель `__GetPublishItems`, отдающая именно ФАЙЛЫ ПУБЛИКАЦИИ
(`ComputeFilesToPublish`/`ResolvedFileToPublish` — стабильная, публичная
часть конвейера `dotnet publish`, тем же приёмом пользуются, например,
встроенные шаблоны SPA/Blazor), а `PhotoFrame.Package.wapproj` явно
запрашивает её через `PackageOutputGroups` и публикует референс ДО сборки
пакета (`PublishReferences`, `BeforeTargets="ExpandProjectReferences"`).

## Как собрать (Visual Studio 2026, SDK 10.0.19041+)

1. Открыть **`PhotoFrame.sln`** (НЕ `PhotoFrame.slnx` — тот описывает только
   сам `PhotoFrame.csproj` для повседневной разработки, см. комментарий в
   самом `.slnx`; НЕ открывать `PhotoFrame.Package.wapproj` отдельно от
   решения — ссылка на `PhotoFrame.csproj` резолвится в контексте решения).
2. В Configuration Manager решения выбрать **x86** либо **x64** (НЕ Any
   CPU — у `.wapproj` такой конфигурации нет и осмысленно быть не может:
   MSIX-пакету нужна конкретная архитектура). **Важно**: активная платформа
   должна быть ОДИНАКОВОЙ для ОБОИХ проектов решения одновременно — если
   `PhotoFrame.csproj` соберётся как x64, а `PhotoFrame.Package.wapproj`
   как x86 (или наоборот), вы получите ошибку вида "There was a mismatch
   between the processor architecture of the project being built...".
   PhotoFrame.sln уже настроен так, что оба проекта переключаются вместе —
   этот шаг просто на случай, если Configuration Manager был изменён вручную.
3. ПКМ на `PhotoFrame.Package` (в Solution Explorer) → **Set as Startup
   Project**, затем ПКМ → **Publish → Create App Packages...**:
   - **Sideloading** → генерирует `.msix` + самоподписанный сертификат
     (`.cer`) для тестовой установки без Store.
   - **Microsoft Store** (после ассоциации приложения через Partner Center)
     → генерирует `.msixupload`/`.msixbundle` для загрузки в Store.
   - Для ОБЫЧНОЙ отладки (F5) `.wapproj` тоже можно запускать напрямую —
     `DebuggerType=CoreClr` в файле уже настроен для этого.

Если сборка всё же падает — см. таблицу диагностики в конце файла.

## Из командной строки (без VS, только Windows SDK + MSBuild)

```powershell
# Восстановление и сборка пакета под x64 (замените на x86 при необходимости)
msbuild PhotoFrame.sln /t:Restore /p:Configuration=Release /p:Platform=x64
msbuild PhotoFrame.Package.wapproj /p:Configuration=Release /p:Platform=x64 `
    /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxPackageSigningEnabled=false
```

Готовый `.msix` окажется в `PhotoFrame.Package\AppPackages\...\`.

Для **MSIXBUNDLE** (обе архитектуры в одном файле — то, что уже настроено
по умолчанию через `AppxBundle=Always`/`AppxBundlePlatforms=x86|x64` в самом
`.wapproj`) соберите пакет x86 И x64 по очереди (см. команды выше с разными
`/p:Platform`) — `.wapproj` сам объединит оба `.msix` в один `.msixbundle` на
последнем шаге, если `AppxBundle` не переопределён в `Never`, как в CLI-
примере выше (там он отключён нарочно — для простого одноархитектурного
sideload-пакета бандл не нужен).

По умолчанию манифест собирается под x64
(`Identity/ProcessorArchitecture="x64"`, см. комментарий в самом файле).
Для x86-сайдлоада без MSIXBUNDLE поменяйте `ProcessorArchitecture` на
`"x86"` перед упаковкой — один манифест описывает одну архитектуру за раз
вне бандла.

## Особенности MSIX относительно InnoSetup/ClickOnce

- **Автозапуск** — в манифесте объявлен `windows.startupTask`
  (`Package.appxmanifest`), это современный аналог `HKCU\Run` для
  упакованных приложений: путь к exe стабилен (в отличие от ClickOnce,
  где он меняется при каждом обновлении), но включение/выключение видно
  пользователю в Диспетчере задач → «Автозагрузка» — тот же UX, что и у
  остальных Store-приложений.
- **broadFileSystemAccess** — объявлен в `rescap:Capability`, необходим для
  доступа к произвольным папкам (источники фото, USB-браузер устройств);
  при публикации в Store требует обоснования в Partner Center
  ("restricted capability").
- **Live Tiles — это НЕ просто "логотип не показывается" без MSIX.**
  И обновление содержимого плитки (`TileUpdateManager`), и её закрепление
  на начальном экране (`SecondaryTile`) — WinRT API, требующие package
  identity для СРАБАТЫВАНИЯ вообще, не только для брендинга. У ClickOnce и
  InnoSetup package identity нет ни в каком виде — оба вызова просто тихо
  ничего не делают. `LiveTileService.IsPinningSupported()` честно проверяет
  `SystemIntegration.IsRunningAsMsixPackage()` и скрывает кнопку закрепления
  плитки в Настройках, если приложение установлено не из MSIX.
- **Обновление MSIX-канала** — `SystemIntegration.Updates.cs` для MSIX
  ведёт себя так же, как для InnoSetup: скачивает подходящий `.msix`/
  `.msixbundle` из релиза и просто открывает его (`ShellExecute`) — это
  запускает системный App Installer, дальше пользователь ведёт диалог сам.
  Учтите: протокол `ms-appinstaller` (прямая установка по ссылке без
  скачивания файла) отключён Microsoft по умолчанию с декабря 2023 года
  из соображений безопасности — то есть ссылка на `.msix`/`.appinstaller`
  саму по себе больше не устанавливает пакет одним кликом из браузера,
  только через явное открытие уже скачанного файла, как и реализовано.
- **VCLibs**: self-contained публикация .NET 8 уже включает собственные
  копии нужных `vcruntime*`/`msvcp140*.dll` — отдельная
  `PackageDependency` на `Microsoft.VCLibs.140.00.UWPDesktop` (нужна
  классическим .NET Framework/UWP-приложениям) здесь НЕ объявлена. Если
  всё же увидите ошибку загрузки нативной библиотеки при запуске
  установленного пакета — см. закомментированную заготовку прямо в
  `Package.appxmanifest`.

Ассеты в `Package/Assets/*.png` сгенерированы автоматически из
`Resources/Icons/AppIcon.ico` как заглушка (синий фон `#2F7FCC` + логотип
по центру). Перед публикацией в Store рекомендуется заменить их на
дизайнерские варианты через **Assets Generator** в Visual Studio
(ПКМ на `.wapproj` → Add → New Item → "App Icons and Logos").

## Диагностика типичных ошибок

| Сообщение об ошибке | Причина | Что сделать |
|---|---|---|
| `Manifest references file 'PhotoFrame.exe' which is not part of the payload` | `.wapproj` не смог найти publish-вывод ссылающегося проекта | Не должно больше возникать — исправлено на уровне проекта (см. выше). Если всё же видите это, убедитесь что не открыли `PhotoFrame.Package.wapproj` в обход `PhotoFrame.sln` |
| `There was a mismatch between the processor architecture of the project being built "AMD64" and the processor architecture of the reference "x86"` | Configuration Manager собирает `PhotoFrame.csproj` и `.wapproj` под РАЗНЫЕ платформы | Проверить активную платформу решения — см. шаг 2 выше |
| `NETSDK1047: Assets file '...' doesn't have a target for '...RuntimeIdentifier...'` | `PhotoFrame.csproj` не восстанавливался с нужным RID | Убедиться, что `<RuntimeIdentifiers>win-x86;win-x64</RuntimeIdentifiers>` (множественное число!) присутствует в `PhotoFrame.csproj`, затем `msbuild /t:Restore` заново |
| `NU1702` предупреждение про несовпадающий TargetFramework | Штатная проверка совместимости референса, писавшаяся под .NET Framework | Безвредно — `SkipGetTargetFrameworkProperties="True"` на `ProjectReference` в `.wapproj` уже стоит именно для этого |
