; Installer/PhotoFrame.iss — Inno Setup 6 скрипт для PhotoFrame Free
; Версия подставляется из командной строки CI:
;   iscc /DMyAppVersion=1.2.2.0-sp1 /DMyArch=x64 /DMyVariant=full ^
;        /DMyPublishDir=..\publish\win-x64-full Installer/PhotoFrame.iss
; full  = self-contained (включает .NET 8.0.31 runtime)
; slim  = framework-dependent (требует .NET 8.0.31 Desktop Runtime, ~7-10 МБ)

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0.0"
#endif
#ifndef MyArch
  #define MyArch "x64"
#endif
#ifndef MyVariant
  #define MyVariant "full"
#endif
#ifndef MyPublishDir
  #define MyPublishDir "..\publish\win-" + MyArch + "-" + MyVariant
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
OutputBaseFilename=PhotoFrame_v{#MyAppVersion}_{#MyArch}_{#MyVariant}_Setup
Compression=lzma2/max
SolidCompression=yes
; x64compatible — корректно и для нативного arm64-раннера Inno Setup 6.3+
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed={#MyArch}
; asInvoker в app.manifest: приложение не требует админа; установщик
; поднимает UAC сам (PrivilegesRequired=admin + overrides=dialog даёт
; пользователю выбор per-machine/per-user при необходимости)
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
DisableProgramGroupPage=yes

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Icons]
Name: "{group}\PhotoFrame"; Filename: "{app}\PhotoFrame.exe"
Name: "{group}\{cm:UninstallProgram,PhotoFrame}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PhotoFrame"; Filename: "{app}\PhotoFrame.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PhotoFrame.exe"; Description: "{cm:LaunchProgram,PhotoFrame}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\PhotoFrame"
