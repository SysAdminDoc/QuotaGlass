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
; L-04 / R4-N2 / R5-P1-03 - stable CLSID of the ToastActivator COM class.
; Hardcoded as a literal below (not a #define) because Inno expands constants
; inside [Icons] / [Registry] quoted values, so the opening brace must be
; doubled with `{{` to escape - which a #define value can't carry safely
; through `{#name}` substitution. Source of truth lives in
; src/QuotaGlass.Widget/Services/ToastActivator.cs#Clsid - keep these in sync.

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
; correctly in Action Center. AppUserModelToastActivatorCLSID couples
; the shortcut's AUMID to the COM-registered ToastActivator class so
; clicked action buttons route through it.
Name: "{commonprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; \
  AppUserModelID: "{#MyAppUserModelId}"; \
  AppUserModelToastActivatorCLSID: "4F3CDEA3-8CB0-4C7F-8243-7ACA5F8B77CE"

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

; L-04 / R4-N2 — Toast Activator CLSID registration. Maps clicks on toast
; action buttons in Action Center back to the widget EXE (the running
; instance via CoRegisterClassObject when alive, otherwise a cold launch
; with --toast-activator).
Root: HKCU; Subkey: "Software\Classes\CLSID\{{4F3CDEA3-8CB0-4C7F-8243-7ACA5F8B77CE}"; \
  ValueType: string; ValueName: ""; ValueData: "QuotaGlass Toast Activator"; \
  Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{4F3CDEA3-8CB0-4C7F-8243-7ACA5F8B77CE}\LocalServer32"; \
  ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --toast-activator"; \
  Flags: uninsdeletekey

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  // No Win10 < 1809 check here — the .NET runtime version check happens
  // on first launch.
end;
