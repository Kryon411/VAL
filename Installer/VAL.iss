; VAL per-user installer. Keep AppId stable so existing installations are recognized as upgrades.

#ifdef ValidationBuild
  #ifndef ValidationAppId
    #error ValidationAppId is required for an isolated validation installer.
  #endif
  #ifndef ValidationInstallRoot
    #error ValidationInstallRoot is required for an isolated validation installer.
  #endif
  #define AppName "VAL Validation"
  #define AppIdGuid "VAL.Validation." + ValidationAppId
#else
  #define AppName "VAL"
  #define AppIdGuid "{{d11fe636-f181-4cdf-bc7e-e4adcfe52237}}"
#endif
#ifndef AppVersion
#define AppVersion "5.1.0"
#endif
#define AppPublisher "Kerry Ryan"
#define AppUrl "https://github.com/Kryon411/VAL"
#define AppExeName "VAL.exe"
#ifndef PayloadDir
  #define PayloadDir "..\PRODUCT\Publish"
#endif

[Setup]
AppId={#AppIdGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}
PrivilegesRequired=lowest
#ifdef ValidationBuild
DefaultDirName={#ValidationInstallRoot}
#else
DefaultDirName={localappdata}\Programs\{#AppName}
#endif
DefaultGroupName={#AppName}
UsePreviousAppDir=no
DisableDirPage=auto
DisableProgramGroupPage=yes
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
OutputDir=..\InstallerOutput
OutputBaseFilename=VAL_Setup_v{#AppVersion}
SetupIconFile={#PayloadDir}\Icons\VAL_Blue_Lens.ico
UninstallDisplayName={#AppName} v{#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}
#ifdef ValidationBuild
AppMutex=Local\VAL.Validation.{#ValidationAppId}
#else
AppMutex=Local\VAL.Desktop.SingleInstance
#endif
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

#ifndef ValidationBuild
[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
#endif

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifndef ValidationBuild
[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
#endif

[Code]
#ifdef ValidationBuild
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CompareText(RemoveBackslashUnlessRoot(ExpandConstant('{app}')),
    RemoveBackslashUnlessRoot('{#ValidationInstallRoot}')) <> 0 then
  begin
    Result := 'Validation installers may only use their isolated test directory.';
  end;
end;
#endif

#ifndef ValidationBuild
procedure DeleteLegacyInstallerFiles(const LegacyRoot: String);
var
  Index: Integer;
  BaseName: String;
begin
  for Index := 0 to 9 do
  begin
    BaseName := AddBackslash(LegacyRoot) + 'unins' + Format('%.3d', [Index]);
    DeleteFile(BaseName + '.exe');
    DeleteFile(BaseName + '.dat');
    DeleteFile(BaseName + '.msg');
  end;
end;

procedure CleanupLegacyPayload;
var
  LegacyRoot: String;
  InstallRoot: String;
begin
  LegacyRoot := ExpandConstant('{localappdata}\VAL');
  InstallRoot := ExpandConstant('{app}');

  if CompareText(RemoveBackslashUnlessRoot(LegacyRoot),
    RemoveBackslashUnlessRoot(InstallRoot)) = 0 then
  begin
    Exit;
  end;

  if not FileExists(AddBackslash(LegacyRoot) + '{#AppExeName}') then
  begin
    Exit;
  end;

  DeleteFile(AddBackslash(LegacyRoot) + '{#AppExeName}');
  DeleteFile(AddBackslash(LegacyRoot) + '{#AppExeName}.sha256');
  DeleteFile(AddBackslash(LegacyRoot) + 'VAL.pdb');
  DelTree(AddBackslash(LegacyRoot) + 'Dock', True, True, True);
  DelTree(AddBackslash(LegacyRoot) + 'Icons', True, True, True);
  DelTree(AddBackslash(LegacyRoot) + 'Modules', True, True, True);
  DeleteLegacyInstallerFiles(LegacyRoot);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    CleanupLegacyPayload;
  end;
end;
#endif
