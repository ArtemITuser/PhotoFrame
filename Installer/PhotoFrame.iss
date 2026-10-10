; Installer/PhotoFrame.iss — Inno Setup 6 скрипт для PhotoFrame Free
; Версия подставляется из командной строки: iscc /DMyAppVersion=1.2.3.0 /DMyArch=x64 ...
; Собранная binary-папка: publish\win-x64 (или win-x86), см. .github/workflows/build.yml

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0.0"
#endif
#ifndef MyArch
  #define MyArch "x64"
#endif

[Setup]
AppName=PhotoFrame
AppVersion={#MyAppVersion}
AppVerName=PhotoFrame {#MyAppVersion}
AppPublisher=ArtemITuser
AppPublisherURL=https://github.com/ArtemITuser/PhotoFrame
AppSupportURL=https://github.com/ArtemITuser/PhotoFrame/issues
DefaultDirName={autopf}\PhotoFrame
DefaultGroupName=PhotoFrame
UninstallDisplayIcon={app}\PhotoFrame.exe
OutputDir=output
OutputBaseFilename=PhotoFrame_v{#MyAppVersion}_{#MyArch}_Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
; asInvoker в app.manifest: приложение не требует админа; установщик
; поднимает UAC сам (PrivilegesRequired=admin + overrides=dialog даёт
; пользователю выбор per-machine/per-user при необходимости)
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\win-{#MyArch}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PhotoFrame"; Filename: "{app}\PhotoFrame.exe"
Name: "{group}\{cm:UninstallProgram,PhotoFrame}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PhotoFrame"; Filename: "{app}\PhotoFrame.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PhotoFrame.exe"; Description: "{cm:LaunchProgram,PhotoFrame}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\PhotoFrame"
