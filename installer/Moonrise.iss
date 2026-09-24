#ifndef AppVersion
  #error AppVersion must be supplied with /DAppVersion=x.y.z
#endif
#ifndef SourceDir
  #error SourceDir must be supplied with /DSourceDir=path
#endif
#ifndef OutputDir
  #error OutputDir must be supplied with /DOutputDir=path
#endif
#ifndef UserDataDir
  #define UserDataDir "{localappdata}\Moonrise"
#endif

#define AppName "Moonrise"
#define AppPublisher "ZOONGG"
#define AppUrl "https://github.com/ZOONGG/Moonrise"
#define AppExeName "Moonrise.exe"

[Setup]
AppId={{0AFD97B2-2A8E-45C5-9297-FD7CE7C6CC88}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} per-user installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Moonrise
DefaultGroupName=Moonrise
DisableProgramGroupPage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
SetupIconFile=..\assets\branding\moonrise-icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=Moonrise-Setup-{#AppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
AppMutex=Local\Moonrise.SingleInstance
UsePreviousAppDir=yes
SetupLogging=yes
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
russian.DesktopShortcut=Создать ярлык на рабочем столе
english.RemoveDataTitle=Moonrise user data
russian.RemoveDataTitle=Пользовательские данные Moonrise
english.RemoveDataPrompt=Also remove Moonrise settings, library, downloaded packages, cache, sessions, and logs?
russian.RemoveDataPrompt=Также удалить настройки, библиотеку, загруженные пакеты, кеш, сессии и логи Moonrise?
english.RemoveDataWarning=Leave this unchecked to preserve your data for a future installation.
russian.RemoveDataWarning=Оставьте флажок снятым, чтобы сохранить данные для будущей установки.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Moonrise.portable,*.pdb,*.jar,*.log,*.dmp,*.mdmp"

[Icons]
Name: "{group}\Moonrise"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Moonrise"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\moonrise"; ValueType: string; ValueName: ""; ValueData: "URL:Moonrise Protocol"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\moonrise"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\moonrise\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKCU; Subkey: "Software\Classes\moonrise\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,Moonrise}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
var
  RemoveUserData: Boolean;

function CommandLineHasParameter(const Parameter: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
    if CompareText(ParamStr(Index), Parameter) = 0 then
    begin
      Result := True;
      Exit;
    end;

  { Uninstall may relaunch itself from a temporary copy. GetCmdTail preserves the
    complete uninstall command line, including that hand-off. Keep the exact
    ParamStr check above and use the raw tail as a fallback for our private flag. }
  Result := Pos(Uppercase(Parameter), Uppercase(GetCmdTail)) > 0;
end;

function InitializeUninstall(): Boolean;
var
  DataForm: TSetupForm;
  PromptLabel: TNewStaticText;
  WarningLabel: TNewStaticText;
  RemoveCheckBox: TNewCheckBox;
  ContinueButton: TNewButton;
  CancelButton: TNewButton;
begin
  Result := True;
  RemoveUserData := False;
  if UninstallSilent then
  begin
    RemoveUserData := CommandLineHasParameter('/REMOVEUSERDATA');
    Log(Format('Moonrise silent uninstall: RemoveUserData=%d', [Ord(RemoveUserData)]));
    Exit;
  end;

  DataForm := CreateCustomForm(ScaleX(500), ScaleY(190), False, True);
  try
    DataForm.Caption := ExpandConstant('{cm:RemoveDataTitle}');
    DataForm.Position := poScreenCenter;

    PromptLabel := TNewStaticText.Create(DataForm);
    PromptLabel.Parent := DataForm;
    PromptLabel.Left := ScaleX(20);
    PromptLabel.Top := ScaleY(20);
    PromptLabel.Width := DataForm.ClientWidth - ScaleX(40);
    PromptLabel.Height := ScaleY(52);
    PromptLabel.AutoSize := False;
    PromptLabel.WordWrap := True;
    PromptLabel.Caption := ExpandConstant('{cm:RemoveDataPrompt}');

    RemoveCheckBox := TNewCheckBox.Create(DataForm);
    RemoveCheckBox.Parent := DataForm;
    RemoveCheckBox.Left := ScaleX(20);
    RemoveCheckBox.Top := ScaleY(80);
    RemoveCheckBox.Width := DataForm.ClientWidth - ScaleX(40);
    RemoveCheckBox.Caption := ExpandConstant('{cm:RemoveDataTitle}');
    RemoveCheckBox.Checked := False;

    WarningLabel := TNewStaticText.Create(DataForm);
    WarningLabel.Parent := DataForm;
    WarningLabel.Left := ScaleX(20);
    WarningLabel.Top := ScaleY(108);
    WarningLabel.Width := DataForm.ClientWidth - ScaleX(40);
    WarningLabel.Height := ScaleY(34);
    WarningLabel.AutoSize := False;
    WarningLabel.WordWrap := True;
    WarningLabel.Font.Color := clGray;
    WarningLabel.Caption := ExpandConstant('{cm:RemoveDataWarning}');

    ContinueButton := TNewButton.Create(DataForm);
    ContinueButton.Parent := DataForm;
    ContinueButton.Width := ScaleX(100);
    ContinueButton.Height := ScaleY(28);
    ContinueButton.Left := DataForm.ClientWidth - ScaleX(220);
    ContinueButton.Top := DataForm.ClientHeight - ScaleY(42);
    ContinueButton.Caption := SetupMessage(msgButtonNext);
    ContinueButton.ModalResult := mrOk;

    CancelButton := TNewButton.Create(DataForm);
    CancelButton.Parent := DataForm;
    CancelButton.Width := ScaleX(100);
    CancelButton.Height := ScaleY(28);
    CancelButton.Left := DataForm.ClientWidth - ScaleX(110);
    CancelButton.Top := DataForm.ClientHeight - ScaleY(42);
    CancelButton.Caption := SetupMessage(msgButtonCancel);
    CancelButton.ModalResult := mrCancel;

    Result := DataForm.ShowModal = mrOk;
    if Result then
      RemoveUserData := RemoveCheckBox.Checked;
  finally
    DataForm.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
  begin
    if DelTree(ExpandConstant('{#UserDataDir}'), True, True, True) then
      Log('Moonrise user data removed.')
    else
      Log('Moonrise user data could not be removed completely.');
  end;
end;
