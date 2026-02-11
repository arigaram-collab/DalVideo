; DalVideo Inno Setup Script
; Inno Setup 6.x required

#define MyAppName "DalVideo"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "DalVideo"
#define MyAppExeName "DalVideo.exe"

[Setup]
AppId={{B8A3D2F1-7C4E-4A9B-8D6F-1E2C3B4A5D6E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=DalVideo-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checked

[Files]
; Main application files (self-contained publish output)
Source: "..\bin\publish\installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; FFmpeg binary
Source: "ffmpeg\ffmpeg.exe"; DestDir: "{app}\Assets"; Flags: ignoreversion; Check: FFmpegExists

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} 실행"; Flags: nowait postinstall skipifsilent

[Code]
function FFmpegExists(): Boolean;
begin
  Result := FileExists(ExpandConstant('{src}\Installer\ffmpeg\ffmpeg.exe')) or
            FileExists(ExpandConstant('{src}\ffmpeg\ffmpeg.exe')) or
            FileExists('ffmpeg\ffmpeg.exe');
end;
