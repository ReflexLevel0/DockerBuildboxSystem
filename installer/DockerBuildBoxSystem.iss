; DockerBuildBoxSystem Inno Setup installer (wizard-style .exe)
; Build steps (quick):
;   1) dotnet publish ..\src\DockerBuildBoxSystem.App -c Release -r win-x64 --self-contained true -o ..\publish\win-x64
;   2) Compile this script with Inno Setup 6 (ISCC.exe)

#define MyAppName "Docker BuildBox System"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "FER & MDU team"
#define MyAppExeName "DockerBuildBoxSystem.App.exe"
#define MySourceDir "..\publish\win-x64"

[Setup]
; NOTE: Generate your own GUID once and keep it stable for upgrades:
AppId={{2C7B80A4-5FB3-45E8-9DB0-8C9D7F8DE2D1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

LicenseFile=LICENCE.txt
DisableWelcomePage=no

; Per-user install by default (no admin required)
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no

OutputDir=.\Output
OutputBaseFilename=DockerBuildBoxSystem_Setup_{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  WarningPage: TWizardPage;
  WarningText: TRichEditViewer;
  AcknowledgeCheck: TNewCheckBox;
  PrivacyButton: TNewButton;
  
procedure CreateWarningPage;
begin
  WarningPage :=
    CreateCustomPage(
      wpLicense,
      'Important Usage Terms',
      'Please read and acknowledge before continuing'
    );

  WarningText := TRichEditViewer.Create(WarningPage);
  WarningText.Parent := WarningPage.Surface;
  WarningText.Left := 0;
  WarningText.Top := 0;
  WarningText.Width := WarningPage.SurfaceWidth;
  WarningText.Height := WarningPage.SurfaceHeight - 70;
  WarningText.ScrollBars := ssVertical;
  WarningText.ReadOnly := True;
  WarningText.WantTabs := False;
  WarningText.LoadFromFile(ExpandConstant('{src}\EULA.rtf'));

  AcknowledgeCheck := TNewCheckBox.Create(WarningPage);
  AcknowledgeCheck.Parent := WarningPage.Surface;
  AcknowledgeCheck.Caption :=
    'I understand that containers may be stopped and data may be modified or deleted.';
  AcknowledgeCheck.Left := 0;
  AcknowledgeCheck.Top := WarningText.Top + WarningText.Height + 8;
  AcknowledgeCheck.Width := WarningPage.SurfaceWidth;
  AcknowledgeCheck.Checked := False;

  PrivacyButton := TNewButton.Create(WarningPage);
  PrivacyButton.Parent := WarningPage.Surface;
  PrivacyButton.Caption := 'View Privacy Policy';
  PrivacyButton.Left := 0;
  PrivacyButton.Top := AcknowledgeCheck.Top + AcknowledgeCheck.Height + 8;
  PrivacyButton.OnClick := @PrivacyButtonClick;
end;

procedure PrivacyButtonClick(Sender: TObject);
begin
  ShellExec(
    'open',
    'PRIVACY.txt',
    '',
    '',
    SW_SHOWNORMAL,
    ewNoWait,
    ResultCode
  );
end;

function DockerDesktopExeExists(): Boolean;
begin
  Result :=
    FileExists(ExpandConstant('{pf}\Docker\Docker\Docker Desktop.exe')) or
    FileExists(ExpandConstant('{pf64}\Docker\Docker\Docker Desktop.exe'));
end;

function DockerCliExists(): Boolean;
begin
  Result :=
    FileExists(ExpandConstant('{pf}\Docker\Docker\resources\bin\docker.exe')) or
    FileExists(ExpandConstant('{pf64}\Docker\Docker\resources\bin\docker.exe')) or
    FileExists(ExpandConstant('{sys}\docker.exe'));
end;

function IsDockerInstalled(): Boolean;
begin
  Result := DockerDesktopExeExists() or DockerCliExists();
end;

procedure InitializeWizard;
begin
  CreateWarningPage;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = WarningPage.ID then
  begin
    if not AcknowledgeCheck.Checked then
    begin
      MsgBox(
        'You must acknowledge the warning to continue installation.',
        mbError,
        MB_OK
      );
      Result := False;
      Exit;
    end;
  end;

  { your existing Docker check }
  if CurPageID = wpReady then
  begin
    if not IsDockerInstalled() then
    begin
      if MsgBox(
           'Docker Desktop does not appear to be installed.'#13#10#13#10 +
           'This application will not function correctly without Docker.'#13#10 +
           'Continue installation anyway?',
           mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
      end;
    end;
  end;
end;

