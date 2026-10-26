; Inno Setup 6 script for FramePilot. Built by build\publish.ps1 -Installer.
#ifndef SourceDir
  #define SourceDir "..\artifacts\FramePilot"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#define AppVersion "1.0.1"

[Setup]
AppId={{6A3C1E52-8F1D-4C6B-9B7E-2F0D5A1C9E41}
AppName=FramePilot
AppVersion={#AppVersion}
AppPublisher=FramePilot
DefaultDirName={autopf}\FramePilot
DefaultGroupName=FramePilot
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=FramePilot-Setup-x64
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

[Icons]
Name: "{group}\FramePilot"; Filename: "{app}\FramePilot.exe"
Name: "{autodesktop}\FramePilot"; Filename: "{app}\FramePilot.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FramePilot.exe"; Description: "{cm:LaunchProgram,FramePilot}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; Note: user data in %LOCALAPPDATA%\FramePilot (including the rollback journal) is intentionally kept on uninstall,
; so changes can still be rolled back after reinstalling. Users can delete it from Settings → Privacy.
