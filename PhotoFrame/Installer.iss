; Installer.iss — InnoSetup 6 script для PhotoFrame v1.2.1.0 (build 52)
; Запуск: iscc "Installer.iss"   (из папки PhotoFrame\, после dotnet publish)
; Результат: Output\PhotoFrame_v1.2.1.0_Setup.exe
;
; Совместимость:
;   • ClickOnce (основной канал): авто-обновление, установка per-user без UAC
;   • InnoSetup (этот файл): портативный/оффлайн установщик, параллельный канал
; Оба артефакта публикуются в одном GitHub Release — не конфликтуют.

#define AppName "PhotoFrame"
#define AppVersion "1.2.1.0"
#define AppPublisher "Ochkin Artyom (ArtemITuser)"
#define AppURL "https://github.com/ArtemITuser/PhotoFrame"
#define AppExeName "PhotoFrame.exe"
#define PublishDir "publish"

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
OutputBaseFilename=PhotoFrame_v{#AppVersion}_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
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

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not DirExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0')) then
  begin
    if MsgBox(
      'Не обнаружен .NET 8 Desktop Runtime.' + #13#10 +
      'PhotoFrame требует .NET 8.0 или новее.' + #13#10 + #13#10 +
      'Продолжить установку? (Скачать Runtime можно на https://dot.net)',
      mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
