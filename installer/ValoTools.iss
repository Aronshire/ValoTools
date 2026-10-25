#define AppName "ValoTools"
#define AppVersion "1.2.2"
#define AppExe "ValoTools.exe"

[Setup]
AppId={{A79A221F-41E8-4EE9-9B2F-B9E4524B381E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Aronshire
DefaultDirName={localappdata}\Programs\{#AppName}
UsePreviousAppDir=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=ValoTools-Setup-{#AppVersion}
SetupIconFile=..\assets\ValoTools.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern dark hidebevels
WizardBackColor=#10181C
WizardImageFile=wizard-side.png
WizardImageBackColor=#10181C
WizardSmallImageFile=..\assets\ValoTools.png
WizardSmallImageBackColor=#10181C
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
DisableWelcomePage=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{app}\ValoInsta.exe"
Type: files; Name: "{app}\ValoInsta.dll"
Type: files; Name: "{app}\ValoInsta.deps.json"
Type: files; Name: "{app}\ValoInsta.runtimeconfig.json"
Type: files; Name: "{app}\ValoInsta.pdb"
Type: files; Name: "{autoprograms}\ValoInsta.lnk"
Type: files; Name: "{autodesktop}\ValoInsta.lnk"

[Files]
Source: "..\publish\ValoTools\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
