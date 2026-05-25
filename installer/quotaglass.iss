; QuotaGlass — Inno Setup script
;
; Builds a per-user installer that places the framework-dependent
; binaries in %LOCALAPPDATA%\Programs\QuotaGlass\, registers the
; native messaging host, sets AppUserModelID on a Start Menu shortcut
; (required for toast grouping), and optionally autostarts the widget.
;
; Build:   ISCC.exe /DAppArch=x64 installer\quotaglass.iss
; Outputs: installer\dist\QuotaGlass-Setup-v{Version}-{AppArch}.exe

#ifndef MyAppVersion
#define MyAppVersion "0.1.0"
#endif

#ifndef AppArch
#define AppArch "x64"
#endif

#define MyAppName "QuotaGlass"
#define MyAppPublisher "SysAdminDoc"
#define MyAppURL "https://github.com/SysAdminDoc/QuotaGlass"
#define MyAppExeName "QuotaGlass.Widget.exe"
#define MyAppUserModelId "com.sysadmindoc.QuotaGlass.Widget"

[Setup]
AppId={{4F1B3F6E-2D8C-4E83-9C12-9B0B17F8D2A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=dist
OutputBaseFilename=QuotaGlass-Setup-v{#MyAppVersion}-{#AppArch}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={#AppArch}
ArchitecturesInstallIn64BitMode={#AppArch}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
SetupIconFile=
WizardImageFile=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Launch QuotaGlass at user logon"; GroupDescription: "Startup:"; Flags: unchecked
Name: "registernmh"; Description: "Register native messaging host so the AI-Usage_Tracker extension can talk to QuotaGlass"; GroupDescription: "Integration:"; Flags: checkedonce

[Files]
; The release workflow publishes both projects into installer\payload\{arch}
; before invoking ISCC. Source path is parameterized on AppArch.
Source: "payload\{#AppArch}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu shortcut MUST carry the AppUserModelID so toasts group
; correctly in Action Center.
Name: "{commonprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; \
  AppUserModelID: "{#MyAppUserModelId}"

[Run]
; Register native messaging host (write HKCU registry keys for Chrome,
; Edge, Chromium, Firefox).
Filename: "{app}\QuotaGlass.NMH.exe"; Parameters: "--register"; Flags: runhidden; \
  Tasks: registernmh
; Auto-launch widget post-install.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch QuotaGlass"; \
  Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\QuotaGlass.NMH.exe"; Parameters: "--unregister"; Flags: runhidden

[Registry]
; Autostart via HKCU Run key. Conditional on the autostart task.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: autostart

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  // No Win10 < 1809 check here — the .NET runtime version check happens
  // on first launch.
end;
