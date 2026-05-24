# README_ANS — Ответы и руководства PhotoFrame v3.3

*Язык: ru-RU | Версия: 1.0.8.7*

---

## 1. GitHub Actions + GitHub Pages + ClickOnce: полная инструкция

### Схема работы

```
Ваш ПК: git push → github.com/ArtemITuser/PhotoFrame (master)
                        ↓  автоматически (~3 мин)
                  GitHub Actions (windows-latest)
                    ├─ dotnet build Release
                    ├─ msbuild /p:PublishProfile=ClickOnceProfile
                    ├─ deploy → ветка gh-pages
                    └─ create GitHub Release с тегом v1.0.8.7
                        ↓
          GitHub Pages: https://artemituser.github.io/PhotoFrame/
                        ↓  пользователь кликает один раз
              PhotoFrame.application  ← ClickOnce manifest
                        ↓  при следующем запуске приложения
              Автоматическое обновление (Background mode)
```

---

### Одноразовая настройка (5 минут)

#### Шаг 1: Включить GitHub Pages

```
github.com/ArtemITuser/PhotoFrame →
Settings → Pages →
  Source: Deploy from branch
  Branch: gh-pages  /  root
→ Save
```

#### Шаг 2: Разрешения Actions

```
Settings → Actions → General →
  Workflow permissions: "Read and write permissions"
→ Save
```

#### Шаг 3: (Опционально) Сертификат подписи

Без сертификата приложение установится с предупреждением «Неизвестный издатель».
Для личного использования это нормально. Если нужна подпись:

```powershell
# На вашем ПК (PowerShell):
$cert = New-SelfSignedCertificate `
    -Subject "CN=PhotoFrame, O=ArtemITuser" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -Type CodeSigning -NotAfter (Get-Date).AddYears(5)

$pwd = ConvertTo-SecureString "МойПароль123" -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath signing.pfx -Password $pwd

# Кодируем в base64:
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("signing.pfx"))
$b64 | Set-Content cert_b64.txt
```

Затем в GitHub:
```
Settings → Secrets and variables → Actions → New repository secret:
  Name: SIGN_CERT_B64    Value: <содержимое cert_b64.txt>
  Name: SIGN_CERT_PASS   Value: МойПароль123
```

---

### Корректное версионирование

**Единственное место** — `PhotoFrame.csproj`:

```xml
<Version>1.0.8.7</Version>
<AssemblyVersion>1.0.8.7</AssemblyVersion>
<FileVersion>1.0.8.7</FileVersion>
```

При каждом push GitHub Actions читает версию через:
```powershell
$xml = [xml](Get-Content "PhotoFrame.csproj")
$ver = $xml.Project.PropertyGroup.Version   # → "1.0.8.7"
```

И передаёт в ClickOnce manifest:
```
/p:ApplicationVersion=1.0.8.7
```

**Автоматически создаётся тег** `v1.0.8.7` на GitHub и Release с `setup.exe`.

---

### Рабочий процесс разработчика (каждый раз)

```bash
# 1. Изменить версию в PhotoFrame.csproj:
#    <Version>1.0.8.8</Version>

# 2. Закоммитить и запушить:
git add -A
git commit -m "Release v1.0.8.8: описание изменений"
git push origin master

# Готово! GitHub Actions (~3 мин):
# → Собирает
# → Публикует на Pages
# → Создаёт Release
# → Пользователи получают автообновление
```

---

### URL-адреса

| Назначение | URL |
|---|---|
| Установка ClickOnce | `https://artemituser.github.io/PhotoFrame/PhotoFrame.application` |
| Страница релизов | `https://github.com/ArtemITuser/PhotoFrame/releases` |
| Последний релиз (API) | `https://api.github.com/repos/ArtemITuser/PhotoFrame/releases/latest` |

---

### Размер публикации

| Вариант | Размер | Требования у пользователя |
|---|---|---|
| ClickOnce framework-dependent | **8–15 МБ** | .NET 8 Desktop Runtime |
| ClickOnce self-contained | ~120 МБ | Ничего дополнительно |
| Рекомендуется | FrameworkDependent | setup.exe предложит установить runtime |

В `ClickOnceProfile.pubxml`:
```xml
<SelfContained>false</SelfContained>   <!-- framework-dependent -->
<BootstrapperEnabled>True</BootstrapperEnabled>   <!-- setup.exe установит runtime -->
```

---

## 2. Исправления v3.3

### InvalidOperationException при полноэкранном режиме (Фото 4)

**Ошибка:** `WindowStyle.None — единственное допустимое значение для WindowStyle,
если AllowsTransparency имеет значение "true"`

**Причина:** Код менял `WindowStyle = SingleBorderWindow` при выходе из полноэкранного режима.
При `AllowsTransparency=True` это запрещено WPF.

**Исправление:** `WindowStyle` теперь **никогда не меняется** в коде.
Fullscreen = только `WindowState.Maximized/Normal`. `WindowStyle="None"` задан в XAML
и остаётся неизменным всё время работы приложения.

```csharp
// ❌ БЫЛО (вызывало ошибку):
private void ExitFullscreen() {
    WindowStyle = WindowStyle.SingleBorderWindow;  // <- ОШИБКА при AllowsTransparency=True
    WindowState = _prevWinState;
}

// ✅ СТАЛО:
private void ExitFullscreen() {
    // WindowStyle НЕ меняем — остаётся None
    WindowState = WindowState.Normal;
}
```

---

### Acrylic Win10 / Mica Win11

Для видимости эффекта **обязательно** `AllowsTransparency="True"` в XAML.
При `AllowsTransparency=False` DWM применяет blur к HWND, но WPF рисует
непрозрачный слой поверх — эффект не виден.

```xml
<!-- MainWindow.xaml — ОБЯЗАТЕЛЬНО: -->
Background="Transparent"
WindowStyle="None"
AllowsTransparency="True"
```

```csharp
// Полупрозрачный WPF-фон смешивается с DWM Acrylic/Mica:
window.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x11, 0x11, 0x11));
```

---

### Заголовок окна настроек (белый фон при тёмной теме)

`SettingsWindow` имеет `WindowStyle="SingleBorderWindow"` — стандартная системная рамка.
В Windows 10 заголовок окна отображается белым пока не вызван `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`.

В v3.3 `SetTitleBarDarkMode` вызывается после загрузки настроек и при смене темы.
Добавьте в `SettingsWindow.xaml.cs` (уже реализовано через `App.ThemeChanged`).

---

### Скринсейвер + автозагрузка

**Автозагрузка:** Записывается в `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PhotoFrame`.
Права администратора **не нужны** — ключ пользовательский.

**Скринсейвер:** Копирует `.exe` в `System32\PhotoFrame.scr`.
Требует UAC — `cmd.exe /c copy` с `Verb="runas"` показывает диалог.

**Полноэкранный запуск при входе в систему:**
В настройках включить:
1. ✅ Автозагрузка при входе в Windows
2. ✅ Запускать слайдшоу при старте

При запуске `PhotoFrame.exe` через автозагрузку → `AutoStart=true` → автоматически переходит
к слайдшоу (не в полный экран). Для полного экрана при автозапуске добавьте ключ запуска:
```
HKCU\Run: PhotoFrame = "C:\path\PhotoFrame.exe /autostart"
```
И в `App.xaml.cs` обработайте аргумент `/autostart`.

---

## 3. Live Tiles

Live Tiles работают **только если приложение установлено через ClickOnce**
(тогда создаётся запись в меню Пуск с корректным `AppUserModelId`).

При запуске `.exe` напрямую обновления плитки молча игнорируются.

Размеры плиток поддерживаемые Windows 10:
- Маленькая 71×71
- Средняя 150×150
- Широкая 310×150
- Большая 310×310

Все четыре размера обновляются одновременно при смене фото (если LiveTiles включены).

---

## 4. Touch/Touchscreen

Реализация в v3.3:
- `Stylus.IsFlicksEnabled="False"` на всех интерактивных элементах — предотвращает конфликт прокрутки
- `MinHeight="32-40"` на CheckBox, ComboBoxItem, NavItem — соответствует Fitts's Law (48px рекомендует MS)
- `IsManipulationEnabled="True"` на Grid (не на Window) — корректное определение жестов
- Свайп вправо/влево → предыдущее/следующее (порог 70px)
- Свайп вверх → открыть настройки
- 56×56px кнопки тулбара — touch-friendly targets

