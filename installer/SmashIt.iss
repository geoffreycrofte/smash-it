; Smash It installer (Inno Setup 6). Built by tools\publish.ps1, which passes:
;   AppVersion, NumericVersion, SourceDir (published app folder), OutputDir, RepoDir

#ifndef SourceDir
  #error Build it with tools\publish.ps1.
#endif

#define AppName "Smash It"
#define AppExe "SmashIt.exe"

[Setup]
; Never change the AppId: it is how upgrades and the uninstaller find an existing install.
AppId={{4EC1BBF0-F818-4C1E-94F9-224EF273FD5D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Geoffrey Crofte
AppPublisherURL=https://geoffreycrofte.com/project/smash-it
AppSupportURL=https://github.com/geoffreycrofte/smash-it/issues
AppUpdatesURL=https://github.com/geoffreycrofte/smash-it/releases
VersionInfoVersion={#NumericVersion}
; Per-user install by default (no admin prompt, goes to %LOCALAPPDATA%\Programs); the dialog offers all users.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile={#RepoDir}\src\SmashIt.App\Assets\SmashIt.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
OutputDir={#OutputDir}
OutputBaseFilename=SmashIt-Setup-x64
; Closes a running Smash It before replacing its files, and broadcasts the PATH change.
CloseApplications=yes
ChangesEnvironment=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
en.AddToPath=Add the "smashit" command to the PATH
fr.AddToPath=Ajouter la commande « smashit » au PATH
en.CommandLine=Command line:
fr.CommandLine=Ligne de commande :
en.ContextMenu=Add "Compress with Smash It" to the Explorer right-click menu
fr.ContextMenu=Ajouter « Compresser avec Smash It » au menu clic droit de l'Explorateur
en.Integration=Windows Explorer:
fr.Integration=Explorateur Windows :

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "contextmenu"; Description: "{cm:ContextMenu}"; GroupDescription: "{cm:Integration}"
Name: "addtopath"; Description: "{cm:AddToPath}"; GroupDescription: "{cm:CommandLine}"; Flags: unchecked

[InstallDelete]
; Files a previous version shipped but this one no longer does would otherwise stay behind.
Type: filesandordirs; Name: "{app}\*"; Check: IsUpgradeOfSmashIt

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Registers for the user who started setup, even when it runs elevated for all users.
Filename: "{app}\{#AppExe}"; Parameters: "--register-context-menu"; Tasks: contextmenu; Flags: runhidden waituntilterminated runasoriginaluser
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Update started from the app ("/SILENT /RELAUNCH=1"): the app closed itself, start it again.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: RelaunchRequested

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-context-menu"; RunOnceId: "UnregisterContextMenu"; Flags: runhidden waituntilterminated

[Code]
const
  UserEnvKey = 'Environment';
  MachineEnvKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

// Only wipe the folder when it really holds Smash It, never a folder the user picked by mistake.
function IsUpgradeOfSmashIt: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\{#AppExe}')) and FileExists(ExpandConstant('{app}\SmashIt.pri'));
end;

function RelaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

function EnvRoot: Integer;
begin
  if IsAdminInstallMode then Result := HKEY_LOCAL_MACHINE else Result := HKEY_CURRENT_USER;
end;

function EnvKey: String;
begin
  if IsAdminInstallMode then Result := MachineEnvKey else Result := UserEnvKey;
end;

function CommandDir: String;
begin
  Result := ExpandConstant('{app}\cmd');
end;

procedure AddToPath(Dir: String);
var
  Path: String;
begin
  if not RegQueryStringValue(EnvRoot, EnvKey, 'Path', Path) then Path := '';
  if Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Path) + ';') > 0 then Exit;
  if (Path <> '') and (Path[Length(Path)] <> ';') then Path := Path + ';';
  RegWriteExpandStringValue(EnvRoot, EnvKey, 'Path', Path + Dir);
end;

procedure RemoveFromPath(Dir: String);
var
  Path, Wrapped: String;
  P: Integer;
begin
  if not RegQueryStringValue(EnvRoot, EnvKey, 'Path', Path) then Exit;
  Wrapped := ';' + Path + ';';
  P := Pos(';' + Uppercase(Dir) + ';', Uppercase(Wrapped));
  if P = 0 then Exit;
  // Drop ";<Dir>", keep the separator after it, then remove the two wrapping semicolons.
  Delete(Wrapped, P, Length(Dir) + 1);
  RegWriteExpandStringValue(EnvRoot, EnvKey, 'Path', Copy(Wrapped, 2, Length(Wrapped) - 2));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then AddToPath(CommandDir);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then RemoveFromPath(CommandDir);
end;
