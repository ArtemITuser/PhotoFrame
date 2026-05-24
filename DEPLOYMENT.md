# 🚀 Развёртывание и обновления PhotoFrame

## Анализ скриншота ClickOnce (что исправить)

### ❌ Неверные URL в настройках публикации

| Параметр | Было (неверно) | Надо |
|---|---|---|
| Расположение установки | `https://raw.githubusercontent.com/ArtemITuser/PhotoFrame/` | `https://artemituser.github.io/PhotoFrame/` |
| Расположение обновления | `https://github.com/ArtemITuser/PhotoFrame-WPF/releases/` | *(то же что установки)* |

**Почему это важно:**
- `raw.githubusercontent.com` отдаёт исходный код (`.cs`, `.csproj`), а не ClickOnce манифесты
- `github.com/.../releases/` — это HTML-страница для людей, а не сервер ClickOnce
- ClickOnce ищет файл `.application` (XML-манифест) по указанному URL

### ✅ Что верно в настройках
- Подписанные манифесты: True ✓
- Минимальная версия: 1.0.8.5 ✓  
- Целевой фреймворк: net8.0-windows ✓
- Конфигурация: Release ✓

### ⚠️ win-x86 vs AnyCPU
- `win-x86` создаёт 32-битный бинарник, работает на x86 и x64 Windows
- `win-x64` создаёт 64-битный бинарник (быстрее на x64, не работает на x86)
- `AnyCPU` — компилятор выбирает в зависимости от ОС (рекомендуется)

---

## Правильная схема GitHub Pages + ClickOnce

```
Репозиторий: ArtemITuser/PhotoFrame (master)
               ↓  push
         GitHub Actions
               ↓  собирает + публикует
         gh-pages branch
               ↓  автоматически
GitHub Pages: https://artemituser.github.io/PhotoFrame/
               ↓  пользователь кликает
         PhotoFrame.application   ← ClickOnce manifest
```

### URL для установки пользователями
```
https://artemituser.github.io/PhotoFrame/PhotoFrame.application
```
Поделитесь этой ссылкой — по ней установится приложение и будет автообновляться.

---

## GitHub Actions — автоматическая публикация

Файл `.github/workflows/clickonce-publish.yml` уже создан в проекте.

### Что происходит при каждом `git push`:
1. GitHub Actions запускается на `windows-latest`
2. Собирает `.NET 8` Release
3. Публикует ClickOnce в папку `app.publish`
4. Деплоит в ветку `gh-pages` → GitHub Pages обновляется
5. Создаёт GitHub Release с тегом версии

### Одноразовая настройка (в браузере на github.com):

**1. Включить GitHub Pages:**
```
Settings → Pages → Source: Deploy from branch → gh-pages → root
```

**2. (Опционально) Добавить сертификат для подписи:**
```powershell
# На вашей машине: создать самоподписанный PFX
$cert = New-SelfSignedCertificate `
    -Subject "CN=PhotoFrame" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -Type CodeSigning
Export-PfxCertificate -Cert $cert -FilePath signing.pfx -Password (Read-Host -AsSecureString)

# Закодировать в base64
[Convert]::ToBase64String([IO.File]::ReadAllBytes("signing.pfx")) | Set-Content cert_b64.txt
```
```
Settings → Secrets → Actions → New repository secret:
  Name: SIGN_CERT_B64   Value: <содержимое cert_b64.txt>
  Name: SIGN_CERT_PASS  Value: <пароль от PFX>
```

**Без сертификата** приложение тоже установится, но Windows покажет предупреждение «Неизвестный издатель». Для личного использования это приемлемо.

---

## Обновления в приложении

### ClickOnce (если установлено через .application)
Переменная среды `ClickOnce_IsNetworkDeployed=true` доступна в приложении.
Обновление происходит **автоматически при следующем запуске** согласно настройкам публикации (UpdateMode=Background).

Раздел **Настройки → О программе** показывает:
- Текущую версию (из Assembly)
- Статус ClickOnce развёртывания
- Кнопку «Проверить (GitHub)» — сравнивает с последним Release на GitHub

### Размер публикации
- Framework-dependent (`--self-contained false`): **~8-15 МБ** ✓
- Self-contained: ~120 МБ (не рекомендуется для GitHub Pages)
- Пользователю нужен **.NET 8 Desktop Runtime** (ClickOnce setup.exe установит автоматически)

---

## Быстрый старт (шаги разработчика)

```bash
# 1. Изменить версию в PhotoFrame.csproj
<Version>1.0.8.7</Version>

# 2. Commit + Push
git add -A
git commit -m "Release v1.0.8.7"
git push origin master

# → GitHub Actions автоматически:
#   - Собирает
#   - Публикует ClickOnce на Pages
#   - Создаёт GitHub Release
```

Больше ничего делать не нужно! 🎉
