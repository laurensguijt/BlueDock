; Inno Setup script for BlueDock.
;
; Installs per user, so it never asks for administrator rights. The payload is the
; self-contained single file build, which carries its own .NET: nothing has to be
; installed first.
;
; Build:
;   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true ^
;       -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
;   iscc Installer\BlueDock.iss /DSourceExe=..\publish\BlueDock.exe /DAppVersion=0.1.0

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\BlueDock.exe"
#endif

#define AppName "BlueDock"
#define AppPublisher "Laurens Guijt"
#define AppUrl "https://github.com/laurensguijt/BlueDock"
; A stable application user model id on the Start Menu shortcut. Windows needs this to tie
; notifications to the app; see the Notifications section in README.md.
#define AppUserModelId "LaurensGuijt.BlueDock"

[Setup]
AppId={{8B2F4A17-6C93-4E5D-9A18-3F7C2D05E641}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; Per user: no administrator prompt, installs under %LocalAppData%\Programs
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

OutputDir=..\dist
OutputBaseFilename=BlueDock-{#AppVersion}-setup
SetupIconFile=..\Assets\bluedock.ico
UninstallDisplayIcon={app}\BlueDock.exe
UninstallDisplayName={#AppName}

WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; The app is a tray utility; keep the wizard short.
DisableWelcomePage=yes
DisableReadyPage=yes
LicenseFile=..\LICENSE

[Languages]
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "BlueDock.exe"; Flags: ignoreversion

[Icons]
; AppUserModelID is what lets Windows attribute notifications to BlueDock.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\BlueDock.exe"; \
    AppUserModelID: "{#AppUserModelId}"; Comment: "Bluetooth-apparaten in het systeemvak"

[Run]
Filename: "{app}\BlueDock.exe"; Description: "BlueDock nu starten"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; The app lives in the tray, so make sure it is gone before its file is removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM BlueDock.exe /F"; Flags: runhidden; \
    RunOnceId: "StopBlueDock"

[Code]
// Autostart is a setting inside the app (HKCU Run key), so the installer deliberately does
// not offer it as well: two places writing the same key is a bug waiting to happen.

function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunKey: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Leave %AppData%\BlueDock\settings.json alone, in case this is a reinstall, but do not
    // leave behind an autostart entry pointing at an executable that no longer exists.
    RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
    if RegValueExists(HKEY_CURRENT_USER, RunKey, 'BlueDock') then
      RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'BlueDock');
  end;
end;
