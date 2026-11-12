#define MyAppName "GridShell"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "GridShell"
#define MyAppURL "https://github.com/theskysanctum/GridShell"
#define MyAppExeName "GridShell.exe"

; GridShell's own process-launching code (SiblingExePath.Resolve) finds its sibling exes
; (GridShell.Grid.exe, GridShell.Watchdog.exe) by walking up from its own exe folder to the
; project folder and reapplying that same relative path against the sibling project - so this
; installer reproduces the real dev-time src/<Project>/bin/<Config>/<TFM>/<RID>/ layout under
; the install directory instead of a flat Program Files layout. The build is self-contained
; (RuntimeIdentifier win-x64 on all four projects), so the target machine needs no .NET
; Desktop Runtime pre-installed - this adds the "win-x64" folder level, which
; SiblingExePath.Resolve now handles via a self-locating walk instead of a fixed depth.
#define MyAppRelDir "src\GridShell\bin\Release\net10.0-windows10.0.19041.0\win-x64"

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
SetupIconFile=..\src\icon.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\GridShell\*"; DestDir: "{app}\src\GridShell\bin\Release\net10.0-windows10.0.19041.0\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\GridShell.Grid\*"; DestDir: "{app}\src\GridShell.Grid\bin\Release\net10.0-windows10.0.19041.0\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\GridShell.Watchdog\*"; DestDir: "{app}\src\GridShell.Watchdog\bin\Release\net10.0-windows10.0.19041.0\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppRelDir}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppRelDir}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
