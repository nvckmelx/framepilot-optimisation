; Inno Setup 6 script for TuneFPS (formerly FramePilot). Built by build\publish.ps1 -Installer.
; AppId, exe name and the data folder are kept from 1.0.x so an update installs over the old version and keeps the rollback journal.
#ifndef SourceDir
  #define SourceDir "..\artifacts\FramePilot"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#define AppVersion "1.1.0"

[Setup]
AppId={{6A3C1E52-8F1D-4C6B-9B7E-2F0D5A1C9E41}
AppName=TuneFPS
AppVersion={#AppVersion}
AppPublisher=TuneFPS
AppPublisherURL=https://tunefps.com
AppSupportURL=https://tunefps.com
AppVerName=TuneFPS {#AppVersion}
DefaultDirName={autopf}\TuneFPS
DefaultGroupName=TuneFPS
UsePreviousGroup=no
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=TuneFPS-Setup-x64
SetupIconFile=..\src\FramePilot.App\Assets\app.ico
UninstallDisplayIcon={app}\FramePilot.exe
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
PrivilegesRequired=admin
WizardStyle=modern

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Shortcuts created by FramePilot 1.0.x
Type: filesandordirs; Name: "{autoprograms}\FramePilot"
Type: files; Name: "{autodesktop}\FramePilot.lnk"

[Icons]
Name: "{group}\TuneFPS"; Filename: "{app}\FramePilot.exe"
Name: "{autodesktop}\TuneFPS"; Filename: "{app}\FramePilot.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FramePilot.exe"; Description: "{cm:LaunchProgram,TuneFPS}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Note: user data in %LOCALAPPDATA%\FramePilot (including the rollback journal) is intentionally kept on uninstall,
; so changes can still be rolled back after reinstalling. Users can delete it from Settings → Privacy.
