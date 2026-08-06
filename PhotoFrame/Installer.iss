; Installer.iss — InnoSetup 6 script для PhotoFrame v1.2.2.5 (build 56)
; Запуск (x86, по умолчанию — работает и на x86, и на x64 Windows через WOW64):
;   iscc "Installer.iss"
;   dotnet publish PhotoFrame.csproj -c Release -p:PublishProfile=FolderProfile.x86
; Запуск (нативный x64):
;   iscc "/DAppArch=x64" "Installer.iss"
;   dotnet publish PhotoFrame.csproj -c Release -p:PublishProfile=FolderProfile.x64
; Результат: Output\PhotoFrame_v1.2.2.5_{arch}_Setup.exe
;
; build 56: тот же AppId (см. ниже) на КАЖДОЙ версии — при установке новой
; поверх старой InnoSetup сам распознаёт существующую установку по этому
; GUID и обновляет её на месте (не ставит вторую параллельную копию).
; Именно на этом основана автоматическая часть обновления через
; SystemIntegration.Updates.cs: приложение только СКАЧИВАЕТ и ЗАПУСКАЕТ
; актуальный Setup.exe, а корректный апгрейд поверх старой версии — штатное
; поведение самого InnoSetup, ничего специального в нём для этого не нужно.
;
; Совместимость:
;   • ClickOnce (основной канал): авто-обновление, установка per-user без UAC
;   • InnoSetup (этот файл): портативный/оффлайн установщик, параллельный канал
; Оба артефакта публикуются в одном GitHub Release — не конфликтуют.
;
; build 53:
;  • Добавлена поддержка двух архитектур из ОДНОГО скрипта через препроцессорный
;    параметр AppArch (x86 по умолчанию — см. Properties/PublishProfiles/
;    FolderProfile.x86.pubxml и .x64.pubxml). Раньше PublishDir указывал на
;    bin\Release\net8.0-windows10.0.19041.0\publish\ без учёта архитектуры —
;    единственный путь, который к тому же не совпадал с тем, что публикует
;    .github/workflows/build-release.yml (публиковал в отдельную папку
;    publish\ в корне репозитория) — при последовательном запуске CI собрал
;    бы установщик из пустой/устаревшей папки. Теперь путь строится из
;    стандартной раскладки `dotnet publish -r <rid>` (с RID-суффиксом),
;    единой для локальной сборки и CI.
;  • Убрана проверка "не найден .NET 8 Desktop Runtime": сборка теперь
;    self-contained (см. publish-профили) и не требует отдельно установленного
;    рантайма — старая проверка либо ничего не давала, либо могла показать
;    пугающее диалоговое окно там, где рантайм в принципе не нужен.

#define AppName "PhotoFrame"
#define AppVersion "1.2.2.5"
#define AppPublisher "Ochkin Artyom (ArtemITuser)"
#define AppURL "https://github.com/ArtemITuser/PhotoFrame"
#define AppExeName "PhotoFrame.exe"

#ifndef AppArch
  #define AppArch "x86"
#endif

#if AppArch == "x64"
  #define PublishDir "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\"
#else
  #define PublishDir "bin\Release\net8.0-windows10.0.19041.0\win-x86\publish\"
#endif

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=PhotoFrame_v{#AppVersion}_{#AppArch}_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
; x86-сборка намеренно НЕ ограничивается по архитектуре — x86-бинарники
; исполняются и на x86, и на x64 Windows (через WOW64), это и даёт
; "универсальный" установщик. Для x64-сборки ограничиваем явно — на чистой
; x86-системе нативный x64-код просто не запустится, лучше не предлагать
; такой установщик там в принципе.
#if AppArch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.19041

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";  Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительные значки:"
Name: "startupentry"; Description: "Запускать PhotoFrame при входе в Windows"; GroupDescription: "При запуске Windows:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";               Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} — скринсейвер"; Filename: "{app}\{#AppExeName}"; Parameters: "/c"
Name: "{group}\Удалить {#AppName}";       Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}";         Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#AppName}"; \
  ValueData: """{app}\{#AppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Запустить PhotoFrame"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Удалить .scr из System32 если был зарегистрирован как скринсейвер
Filename: "{sys}\cmd.exe"; Parameters: "/c del /f /q ""{sys}\PhotoFrame.scr"""; \
  Flags: runhidden

[UninstallDelete]
; Стоп-лист путей и настройка отказа от MDL2-шрифта — не нужны после удаления
Type: dirifempty; Name: "{localappdata}\PhotoFrame"
