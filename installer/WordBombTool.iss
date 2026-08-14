; Inno Setup script for Word Bomb Tool (WPF/.NET 8 port).
; Bundles the ReadyToRun, self-contained build of the GUI (no .NET runtime
; prerequisite, faster cold start than the plain self-contained variant).
; Build the exe first (see publish.ps1 at the repo root), then:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\WordBombTool.iss
; Output lands in dist\installer\WordBombTool-Setup.exe.

#define MyAppName "Word Bomb Tool"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Word Bomb Tool"
#define MyAppExeName "WordBombGUI.exe"
; Path to this script is installer\WordBombTool.iss, so ..\ is the repo root.
#define RepoRoot "..\"
#define GuiSrc RepoRoot + "dist\gui-r2r"
#define IconSrc RepoRoot + "src\WordBombGui\Resources\appicon.ico"

[Setup]
AppId={{E6C1F6C0-6E7A-4C2E-9B1E-9B1C1F0D6A1D}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-machine by default, but allow a per-user install too (no admin needed)
; via the privileges dialog below.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist\installer
OutputBaseFilename=WordBombTool-Setup
SetupIconFile={#IconSrc}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#GuiSrc}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
