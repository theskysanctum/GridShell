#define MyAppName "GridShell"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "GridShell"
#define MyAppURL "https://github.com/theskysanctum/GridShell"
#define MyAppExeName "GridShell.exe"

; GridShell's own process-launching code (SiblingExePath.Resolve) finds its sibling exes
; (GridShell.Grid.exe, GridShell.Watchdog.exe) by assuming the exact dev-time
; src/<Project>/bin/<Config>/<TFM>/ folder layout (3 levels under the project folder)
; relative to whichever exe is currently running - so this installer deliberately
; reproduces that same folder structure under the install directory instead of a flat
; Program Files layout, to avoid touching that logic right before a release. This is also
; why the build is published framework-dependent rather than self-contained - a
; self-contained/RID-specific publish adds an extra "win-x64" folder level that breaks
; SiblingExePath's fixed-depth assumption (confirmed by testing: GridShell.Grid.exe
; couldn't be found at all with that extra folder in place). Framework-dependent means a
; compatible .NET Desktop Runtime must already be on the target machine - Windows shows
; its own download prompt automatically if GridShell.exe is launched without one.
#define MyAppRelDir "src\GridShell\bin\Release\net10.0-windows10.0.19041.0"

[Setup]
AppId={{B2E1F4A0-5C3D-4E8B-9F2A-1D6C7E8A9B0C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName=C:\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=GridShell-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppRelDir}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\GridShell\*"; DestDir: "{app}\src\GridShell\bin\Release\net10.0-windows10.0.19041.0"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\GridShell.Grid\*"; DestDir: "{app}\src\GridShell.Grid\bin\Release\net10.0-windows10.0.19041.0"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\GridShell.Watchdog\*"; DestDir: "{app}\src\GridShell.Watchdog\bin\Release\net10.0-windows10.0.19041.0"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppRelDir}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppRelDir}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
