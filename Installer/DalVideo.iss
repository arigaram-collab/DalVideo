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

; FFmpeg binary - place ffmpeg.exe in Installer\ffmpeg\ before building
#ifexist "ffmpeg\ffmpeg.exe"
Source: "ffmpeg\ffmpeg.exe"; DestDir: "{app}\Assets"; Flags: ignoreversion
#else
  #pragma message "WARNING: ffmpeg\ffmpeg.exe not found - installer will be built without FFmpeg"
#endif

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

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not FileExists(ExpandConstant('{app}\Assets\ffmpeg.exe')) then
      MsgBox('FFmpeg가 포함되지 않았습니다.' + #13#10 +
             'DalVideo 사용을 위해 ffmpeg.exe를 설치해 주세요:' + #13#10#13#10 +
             '1. winget install Gyan.FFmpeg' + #13#10 +
             '2. 또는 ffmpeg.exe를 설치 폴더의 Assets 디렉토리에 복사',
             mbInformation, MB_OK);
  end;
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

