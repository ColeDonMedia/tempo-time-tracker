; Tempo installer (Inno Setup 6).
; Built by CI:  iscc /DAppVersion=2.6.0 /DSourceExe=..\publish\Tempo.exe installer\tempo.iss
; Per-user install: no admin prompt, nothing written outside the user's profile.
; Uninstalling never touches your data in %LOCALAPPDATA%\TempoStudio.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\Tempo.exe"
#endif

[Setup]
AppId={{6B0E2F7C-3C1D-4F4A-9E2B-7A1D3C5E9F10}
AppName=Tempo
AppVersion={#AppVersion}
AppVerName=Tempo {#AppVersion}
AppPublisher=Tempo (open source)
AppPublisherURL=https://github.com/ColeDonMedia/tempo-time-tracker
AppSupportURL=https://github.com/ColeDonMedia/tempo-time-tracker/issues
AppUpdatesURL=https://github.com/ColeDonMedia/tempo-time-tracker/releases
DefaultDirName={localappdata}\Programs\Tempo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Tempo-Setup-{#AppVersion}
SetupIconFile=..\Assets\tempo.ico
UninstallDisplayIcon={app}\Tempo.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
LicenseFile=..\LICENSE

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Tempo"; Filename: "{app}\Tempo.exe"
Name: "{userdesktop}\Tempo"; Filename: "{app}\Tempo.exe"; Tasks: desktopicon

[Registry]
; Tempo's own "Start with Windows" setting writes this value; remove it on uninstall so nothing is left behind.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Tempo"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\Tempo.exe"; Description: "{cm:LaunchProgram,Tempo}"; Flags: nowait postinstall skipifsilent; Check: HasDesktopRuntime
; an in-app update runs the installer with /SILENT: start Tempo again afterwards
Filename: "{app}\Tempo.exe"; Flags: nowait; Check: WizardSilent and HasDesktopRuntime

[Code]
// Tempo.exe is framework-dependent: it needs the .NET 8 Desktop Runtime (x64).
function HasDesktopRuntime: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep = ssPostInstall) and (not HasDesktopRuntime) and (not WizardSilent) then
    if MsgBox('Tempo needs the free Microsoft .NET 8 Desktop Runtime (x64), which is not installed yet.' + #13#10#13#10 +
              'Open the download page now?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;
