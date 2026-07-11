# PhotoFrame — упаковка в MSIX / MSIXBUNDLE (build 52)

Три канала распространения теперь сосуществуют:

| Канал | Файл | Кому подходит |
|---|---|---|
| InnoSetup | `Installer.iss` → `PhotoFrame_v*_Setup.exe` | Прямая установка, максимум контроля (System32 для скринсейвера, HKCU\Run) |
| ClickOnce | `Publish/` (VS "Publish" мастер) | Автообновление в один клик, без прав администратора |
| **MSIX / MSIXBUNDLE** | `Package/Package.appxmanifest` | Публикация в Microsoft Store либо корпоративный sideload через Intune/SCCM |

## Через Visual Studio 2026 (рекомендуется)

1. Открыть решение → ПКМ на решении → **Добавить → Новый проект** →
   "Windows Application Packaging Project" (`.wapproj`), назвать
   `PhotoFrame.Package`.
2. В новом проекте: ПКМ на **Applications** → **Add Reference** →
   выбрать `PhotoFrame.csproj`.
3. Удалить автосозданный `Package.appxmanifest` из `.wapproj` и подключить
   вместо него `Package/Package.appxmanifest` из этого архива
   (Add → Existing Item, либо скопировать содержимое).
4. Скопировать `Package/Assets/*.png` в `Assets/` вашего `.wapproj`.
5. ПКМ на `.wapproj` → **Publish → Create App Packages...**:
   - **Sideloading** → генерирует `.msix` + самоподписанный сертификат
     (`.cer`) для тестовой установки без Store.
   - **Microsoft Store** (после ассоциации приложения через Partner Center)
     → генерирует `.msixupload`/`.msixbundle` для загрузки в Store.

## Из командной строки (без VS, только Windows SDK)

```powershell
# 1. Публикация самодостаточного билда приложения
dotnet publish PhotoFrame.csproj -c Release -r win-x64 `
    -p:SelfContained=true -p:PublishSingleFile=false -o Package\Layout

# 2. Манифест и ассеты в макет пакета
Copy-Item Package\Package.appxmanifest Package\Layout\AppxManifest.xml
Copy-Item -Recurse Package\Assets Package\Layout\Assets

# 3. Упаковка (путь к SDK может отличаться — см. установленную версию)
$sdk = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64"
& "$sdk\makeappx.exe" pack /d Package\Layout /p Output\PhotoFrame.msix

# 4. Подпись (для sideload — самоподписанный; для Store — сертификат издателя)
& "$sdk\signtool.exe" sign /fd SHA256 /a Output\PhotoFrame.msix
```

Для **MSIXBUNDLE** (несколько архитектур в одном файле — актуально, если
позже добавится сборка ARM64) используется `makeappx bundle`:

```powershell
& "$sdk\makeappx.exe" bundle /d Output\BundleInput /p Output\PhotoFrame.msixbundle
```
где `Output\BundleInput` содержит по одному `.msix` на каждую архитектуру
(`PhotoFrame_x64.msix`, `PhotoFrame_arm64.msix`, ...), названным согласно
требованиям `makeappx` (суффикс архитектуры в имени файла).

## Особенности MSIX относительно InnoSetup/ClickOnce

- **Автозапуск** — в манифесте объявлен `windows.startupTask`
  (`Package.appxmanifest`), это современный аналог `HKCU\Run` для
  упакованных приложений: путь к exe стабилен (в отличие от ClickOnce,
  где он меняется при каждом обновлении), но включение/выключение видно
  пользователю в Диспетчере задач → «Автозагрузка» — тот же UX, что и у
  остальных Store-приложений. `SystemIntegration.SetAutostart` при
  MSIX-упаковке можно оставить как резервный путь (HKCU\Run по-прежнему
  работает и под MSIX), но `windows.startupTask` — предпочтительный способ,
  если приложение публикуется в Store.
- **broadFileSystemAccess** — объявлен в `rescap:Capability`, необходим для
  доступа к произвольным папкам (источники фото, USB-браузер устройств);
  при публикации в Store требует обоснования в Partner Center
  ("restricted capability").
- **Live Tiles** — брендинг (`branding="nameAndLogo"`) корректно отображает
  логотип поверх плитки только при наличии package identity — то есть
  именно под MSIX (для ClickOnce/InnoSetup Windows может показывать
  плитку без логотипа — известное ограничение классических Win32-приложений
  без упаковки).

Ассеты в `Package/Assets/*.png` сгенерированы автоматически из
`Resources/Icons/AppIcon.ico` как заглушка (синий фон `#2F7FCC` + логотип
по центру). Перед публикацией в Store рекомендуется заменить их на
дизайнерские варианты через **Assets Generator** в Visual Studio
(ПКМ на `.wapproj` → Add → New Item → "App Icons and Logos").
