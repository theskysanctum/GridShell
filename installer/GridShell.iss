#define MyAppName "GridShell"
#define MyAppVersion "1.1.104"
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
; Use Inno Setup's built-in dark Windows 11 style regardless of the user's Windows theme.
WizardStyle=modern dark windows11 includetitlebar hidebevels
WizardBackColor=#1A1A1A
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppRelDir}\{#MyAppExeName}
SetupIconFile=..\src\icon.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to GridShell Setup
WelcomeLabel2=Install GridShell, a full-screen launcher for your apps, games, and websites.
FinishedHeadingLabel=GridShell is ready
FinishedLabel=GridShell has been installed. Click Finish, or select Launch GridShell to open it now.

[Files]
Source: "..\publish\GridShell\*"; DestDir: "{app}\src\GridShell\bin\Release\net10.0-windows10.0.19041.0\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\GridShell.Grid\*"; DestDir: "{app}\src\GridShell.Grid\bin\Release\net10.0-windows10.0.19041.0\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\GridShell.Watchdog\*"; DestDir: "{app}\src\GridShell.Watchdog\bin\Release\net10.0-windows10.0.19041.0\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
; The self-contained publish covers .NET itself, but LibGit2Sharp's and SDL3's native DLLs
; are still built against the Visual C++ runtime, which Windows doesn't ship by default -
; bundled here so nothing needs tracking down by hand on a fresh machine. Not committed to
; this repo - StoreCatalogSync clones it fresh on every Store open, and these binaries are
; too large for that to stay cheap. Pulled instead from this project's own "redist-assets"
; GitHub Release (see fetch-redist.ps1), which `git clone` never touches.
Source: "redist\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
; Several of the Store's own emulators (and older games generally) still link against the
; legacy DirectX 9 D3DX/XInput/XAudio2 DLLs that ship in this redistributable rather than in
; Windows itself - same "don't make the user go hunting for it" reasoning as the VC++ runtime
; above.
Source: "redist\directx_redist.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppRelDir}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
; The VC++ redist installer is idempotent and quick to no-op if an equal-or-newer runtime is
; already present, so this always runs it rather than trying to detect the installed version
; itself - simpler and just as correct. /quiet suppresses its own UI; waituntilterminated
; keeps GridShell's own install from finishing (and offering to launch) until this is done.
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Visual C++ runtime..."; Flags: waituntilterminated
; The DirectX redist is a self-extracting CAB, not a normal installer - it has to be extracted
; to a folder first (/Q /T:<dir>, silently, no setup run yet), then DXSETUP.exe run from
; there to actually install it. D3DX9_43.dll is one of its legacy components and is absent
; from Windows itself, so use it to skip both steps when the runtime is already installed.
Filename: "{tmp}\directx_redist.exe"; Parameters: "/Q /T:""{tmp}\dxredist"""; StatusMsg: "Extracting DirectX runtime..."; Flags: waituntilterminated; Check: not IsDirectXUserRuntimeInstalled
Filename: "{tmp}\dxredist\DXSETUP.exe"; Parameters: "/silent"; StatusMsg: "Installing DirectX runtime..."; Flags: waituntilterminated; Check: not IsDirectXUserRuntimeInstalled
Filename: "{app}\{#MyAppRelDir}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDirectXUserRuntimeInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\D3DX9_43.dll'))
    and FileExists(ExpandConstant('{syswow64}\D3DX9_43.dll'));
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
