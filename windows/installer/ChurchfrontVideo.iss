#define AppName "Churchfront Video"
#define AppVersion "0.1.0"
#define AppPublisher "Shores Community Church"
#define AppExe "CueVideo.App.exe"
#define PublishDir "..\dist\publish"

[Setup]
AppId={{6C0F5C63-6F8E-4F2A-9C39-3F0B4C7A1D21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Churchfront Video
DefaultGroupName=Churchfront Video
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=ChurchfrontVideo-Setup-{#AppVersion}
SetupIconFile=..\src\CueVideo.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0

[Tasks]
Name: "autostart"; Description: "Start Churchfront Video when Windows starts"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "Churchfront Video"; ValueData: """{app}\{#AppExe}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  Ndi: String;
begin
  Result := True;
  Ndi := ExpandConstant('{commonpf}\NDI\NDI 6 Tools\Runtime\Processing.NDI.Lib.x64.dll');
  if not FileExists(Ndi) then
  begin
    if MsgBox('NDI 6 Tools does not appear to be installed.' + #13#10#13#10 +
              'Churchfront Video needs the NDI runtime for camera and program pictures. ' +
              'Install NDI 6 Tools, then start the app.' + #13#10#13#10 +
              'Continue with the installation anyway?',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
