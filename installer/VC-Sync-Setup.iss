; VC Sync installer script
[Setup]
AppId={{4A7E3B5D-1E1A-45F0-99AC-B4F4F8B0A9B3}
AppName=VC Sync
AppVersion=2.0.2
AppPublisher=VChannel
DefaultDirName={autopf}\VC Sync
DefaultGroupName=VC Sync
DisableProgramGroupPage=yes
OutputDir=..\installer\dist
OutputBaseFilename=VC-Sync-Setup-2.0.2
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\VCSyncBackupApp\Assets\vcsync-logo.ico
UninstallDisplayIcon={app}\VCSyncBackupApp.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\VC Sync"; Filename: "{app}\VCSyncBackupApp.exe"
Name: "{autodesktop}\VC Sync"; Filename: "{app}\VCSyncBackupApp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\VCSyncBackupApp.exe"; Description: "Launch VC Sync"; Flags: nowait postinstall skipifsilent
