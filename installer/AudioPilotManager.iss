; Audio Pilot Manager installer (Inno Setup 6)
;
; Built by build.ps1, which passes:
;   /DAppVersion=1.0.0
;   /DSourceDir=<folder containing the published AudioPilotManager.exe>
;
; Installs per user by default (no administrator prompt). Users can choose "all users" in the
; first page if they prefer; that one needs admin rights, as any Program Files install does.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif

#define AppName "Audio Pilot Manager"
#define AppExe "AudioPilotManager.exe"
#define AppPublisher "GitHixy"
#define AppUrl "https://github.com/GitHixy/AudioPilotManager"

[Setup]
AppId={{6E4B7C1A-3F2D-4B8E-9A51-2C7D9F0E4A6B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputBaseFilename=AudioPilotManager-{#AppVersion}-Setup
SetupIconFile=..\src\AudioPilotManager\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Close a running copy before upgrading or uninstalling.
AppMutex=Local\AudioPilotManager.SingleInstance
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start Audio Pilot Manager when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same per-user Run value the app's own "Start with Windows" switch uses, removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AudioPilotManager"; \
    ValueData: """{app}\{#AppExe}"" --minimized"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings in %AppData% are kept on purpose (reinstalling keeps your profiles).
Type: filesandordirs; Name: "{localappdata}\AudioPilotManager\logs"

[Code]
// Make sure the Run value never survives an uninstall, even if it was created from inside the app.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AudioPilotManager');
end;
