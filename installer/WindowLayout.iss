; Inno Setup 6 — Window Layout installer
; Build: run ..\build-installer.ps1 (publishes GUI then compiles)

#define MyAppName "Window Layout"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "chrisflory"
#define MyAppURL "https://github.com/chrisflory/window-layout"
#define MyAppExeName "Window Layout.exe"

[Setup]
AppId={{A8F3C2E1-9B4D-4F6A-8E2C-1D7B5A9F0E33}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\WindowLayout
DefaultGroupName=Window Layout Tools
DisableProgramGroupPage=yes
AllowNoIcons=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=WindowLayoutSetup
SetupIconFile=assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
InfoBeforeFile=INFO-BEFORE.txt
LicenseFile=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartIfNeededByRun=no
; Always refresh Start Menu / desktop icons on upgrade
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "installmodule"; Description: "Install VirtualDesktop PowerShell module (required, needs internet)"; GroupDescription: "Components:"; Flags: checkedonce
Name: "logontask"; Description: "Restore window layout automatically at &logon"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; GUI (main Start Menu / desktop target)
Source: "..\dist\gui\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Application payload (kit scripts)
Source: "..\apply-window-layout.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\capture-window-layout.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\list-window-layout.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\register-logon-task.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\refresh-local-module.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\setup.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\window-layout.rules.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist
Source: "..\DISABLE-LAYOUT.example"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY.md"; DestDir: "{app}"; Flags: ignoreversion
; Optional CLI launchers
Source: "launchers\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "assets\app.ico"; DestDir: "{app}\assets"; Flags: ignoreversion

[Icons]
; Single top-level app entry (must NOT share a name with a Start Menu folder,
; or Windows All-apps shows a folder with no "Pin to Start")
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\assets\app.ico"; Comment: "Save and restore window layouts"; AppUserModelID: "chrisflory.WindowLayout"
; Helpers in a differently named folder
Name: "{userprograms}\Window Layout Tools\Capture layout"; Filename: "{app}\Capture Layout.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\assets\app.ico"; Comment: "Save current window positions"
Name: "{userprograms}\Window Layout Tools\Apply layout"; Filename: "{app}\Apply Layout.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\assets\app.ico"; Comment: "Restore saved window layout"
Name: "{userprograms}\Window Layout Tools\List windows"; Filename: "{app}\List Windows.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\assets\app.ico"; Comment: "Show open windows and desktops"
Name: "{userprograms}\Window Layout Tools\Open install folder"; Filename: "{app}"; IconFilename: "{app}\assets\app.ico"
Name: "{userprograms}\Window Layout Tools\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Optional desktop shortcut
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\assets\app.ico"; Comment: "Save and restore window layouts"; Tasks: desktopicon; AppUserModelID: "chrisflory.WindowLayout"

[Registry]
; Helps Win+R find the app as "win64" or "WindowLayout"
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\win64.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\win64.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\WindowLayout.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\WindowLayout.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"

[Run]
Filename: "{app}\run-pwsh.cmd"; Parameters: "-File ""{app}\setup.ps1"""; StatusMsg: "Installing VirtualDesktop module..."; Flags: runhidden waituntilterminated; Tasks: installmodule
Filename: "{app}\run-pwsh.cmd"; Parameters: "-File ""{app}\register-logon-task.ps1"""; StatusMsg: "Registering logon task..."; Flags: runhidden waituntilterminated; Tasks: logontask
Filename: "{app}\{#MyAppExeName}"; Description: "Open Window Layout now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\run-pwsh.cmd"; Parameters: "-File ""{app}\register-logon-task.ps1"" -Unregister"; Flags: runhidden waituntilterminated; RunOnceId: UnregLayoutTask

[UninstallDelete]
Type: files; Name: "{app}\apply-window-layout.log"
Type: files; Name: "{app}\DISABLE-LAYOUT"
Type: dirifempty; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
