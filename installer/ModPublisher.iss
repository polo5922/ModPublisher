; Installateur de ModPublisher (Inno Setup 6). Ne pas compiler à la main : lancer installer\build.ps1,
; qui publie l'app puis appelle ISCC avec la bonne version.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "ModPublisher"
#define AppExe "ModPublisher.exe"
#define PublishDir "..\artifacts\publish"

[Setup]
AppId={{37759E95-0DE0-4FFE-B0E9-4A549F79546A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=GameAndDev
VersionInfoVersion={#AppVersion}
; Installation pour l'utilisateur courant : pas besoin des droits administrateur
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\src\ModPublisher\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=Output
OutputBaseFilename=ModPublisher-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
; Ferme l'app si elle tourne pendant une mise à jour
CloseApplications=yes
RestartApplications=no

[Languages]
; La langue de Windows est choisie automatiquement ; la première sert de repli
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Mise à jour : on retire d'abord l'ancienne version pour ne pas garder de fichiers périmés
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Les réglages, jetons, sessions et sauvegardes (%APPDATA%\ModPublisher, %LOCALAPPDATA%\ModPublisher)
; ne sont pas supprimés à la désinstallation.
