; Inno Setup script for Word Bomb Tool (WPF/.NET 8 port).
; Bundles the ReadyToRun, self-contained builds (no .NET runtime prerequisite,
; faster cold start than the plain self-contained variant) of both the GUI
; and the CLI. Build the exes first (see publish.ps1 at the repo root), then:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\WordBombTool.iss
; Output lands in dist\installer\WordBombTool-Setup.exe.

#define MyAppName "Word Bomb Tool"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Word Bomb Tool"
#define MyAppExeName "WordBombGUI.exe"
#define MyCliExeName "WordBombCLI.exe"
; Path to this script is installer\WordBombTool.iss, so ..\ is the repo root.
#define RepoRoot "..\"
#define GuiSrc RepoRoot + "dist\gui-r2r"
#define CliSrc RepoRoot + "dist\cli-r2r"
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
Name: "addtopath"; Description: "Add the CLI (WordBombCLI.exe) to PATH"; GroupDescription: "Command-line tool"; Flags: unchecked

[Files]
Source: "{#GuiSrc}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#CliSrc}\{#MyCliExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; User-scope PATH entry (no admin required) for the CLI, only if requested.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NeedsAddPath('{app}')

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Param + ';', ';' + OrigPath + ';') = 0;
end;
