[Setup]
AppName=PC CONTROL ULTIMATE
AppVersion=2.4.0
AppPublisher=PC Control Ultimate Team
DefaultDirName={autopf}\PC Control Ultimate
DefaultGroupName=PC Control Ultimate
OutputDir=..\publish\installer
OutputBaseFilename=PCControlUltimate_Setup_x64_v2.4
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\PCControlUltimate.App.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PC CONTROL ULTIMATE"; Filename: "{app}\PCControlUltimate.App.exe"
Name: "{autodesktop}\PC CONTROL ULTIMATE"; Filename: "{app}\PCControlUltimate.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PCControlUltimate.App.exe"; Description: "{cm:LaunchProgram,PC CONTROL ULTIMATE}"; Flags: nowait postinstall skipifsilent