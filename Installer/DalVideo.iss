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
OutputBaseFilename=DalVideo-Setup
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
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Main application files (self-contained publish output)
Source: "..\bin\publish\installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; FFmpeg binary (optional - place ffmpeg.exe in Installer\ffmpeg\ before building)
Source: "ffmpeg\ffmpeg.exe"; DestDir: "{app}\Assets"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\DalVideo"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{#MyAppName} 실행"; Flags: nowait postinstall skipifsilent

[Code]
function GetUninstallString(): String;
var
  UninstallKey: String;
  UninstallString: String;
begin
  Result := '';
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8A3D2F1-7C4E-4A9B-8D6F-1E2C3B4A5D6E}_is1';
  if RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', UninstallString) then
    Result := UninstallString
  else if RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', UninstallString) then
    Result := UninstallString;
end;

function InitializeSetup(): Boolean;
var
  UninstallString: String;
  ResultCode: Integer;
begin
  Result := True;
  UninstallString := GetUninstallString();
  if UninstallString <> '' then
  begin
    if MsgBox('DalVideo가 이미 설치되어 있습니다.' + #13#10 +
              '기존 버전을 제거한 후 설치를 계속하시겠습니까?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      Exec(RemoveQuotes(UninstallString), '/SILENT /NORESTART', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
    end
    else
      Result := False;
  end;
end;

