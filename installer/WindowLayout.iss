; Inno Setup 6 — Window Layout installer
; Build: run ..\build-installer.ps1 (publishes GUI then compiles)

#define MyAppName "Window Layout"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Window Layout Kit"
#define MyAppURL "https://github.com/MScholtes/PSVirtualDesktop"
#define MyAppExeName "Window Layout.exe"

[Setup]
AppId={{A8F3C2E1-9B4D-4F6A-8E2C-1D7B5A9F0E33}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\WindowLayout
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "installmodule"; Description: "Install VirtualDesktop PowerShell module (required, needs internet)"; GroupDescription: "Components:"; Flags: checkedonce
Name: "logontask"; Description: "Restore window layout automatically at &logon"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; GUI
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
; Optional CLI launchers (still available)
Source: "launchers\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Window Layout control panel"
Name: "{group}\Capture layout"; Filename: "{app}\Capture Layout.cmd"; WorkingDir: "{app}"; Comment: "Save current window positions"
Name: "{group}\Apply layout"; Filename: "{app}\Apply Layout.cmd"; WorkingDir: "{app}"; Comment: "Restore saved window layout"
Name: "{group}\List windows"; Filename: "{app}\List Windows.cmd"; WorkingDir: "{app}"; Comment: "Show open windows and desktops"
Name: "{group}\Open install folder"; Filename: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Install PS module + ProgramData copy when selected
Filename: "{app}\run-pwsh.cmd"; Parameters: "-File ""{app}\setup.ps1"""; StatusMsg: "Installing VirtualDesktop module..."; Flags: runhidden waituntilterminated; Tasks: installmodule
; Optional logon task
Filename: "{app}\run-pwsh.cmd"; Parameters: "-File ""{app}\register-logon-task.ps1"""; StatusMsg: "Registering logon task..."; Flags: runhidden waituntilterminated; Tasks: logontask
; Offer to open the GUI
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
