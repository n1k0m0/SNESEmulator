#ifndef PackageDir
  #error PackageDir must point to a verified self-contained package
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
#ifndef TestBuild
  #define ProductId "SNESEmulator.Independent.CSharp"
  #define DisplayName "SNESEmulator"
#else
  #define ProductId "SNESEmulator.Isolated.DistributionTest"
  #define DisplayName "SNESEmulator Distribution Test"
#endif
#ifndef AppVersion
  #error AppVersion must match the verified package version
#endif
#ifndef AppVersionInfo
  #error AppVersionInfo must be the numeric Windows file version
#endif
[Setup]
AppId={#ProductId}
AppName={#DisplayName}
AppVersion={#AppVersion}
AppVerName={#DisplayName} {#AppVersion}
AppPublisher=Nils Kopal
AppPublisherURL=https://github.com/n1k0m0/SNESEmulator
AppSupportURL=https://github.com/n1k0m0/SNESEmulator/issues
AppUpdatesURL=https://github.com/n1k0m0/SNESEmulator/releases
DefaultDirName={localappdata}\Programs\SNESEmulator
DefaultGroupName={#DisplayName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=SNESEmulator-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\SNESEmulator.exe
LicenseFile=..\LICENSE
DisableProgramGroupPage=yes
WizardStyle=modern
VersionInfoVersion={#AppVersionInfo}
VersionInfoProductVersion={#AppVersionInfo}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription=SNESEmulator Setup
VersionInfoCompany=Nils Kopal
[Tasks]
#ifndef TestBuild
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
#endif
[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
#ifndef TestBuild
Name: "{group}\SNESEmulator"; Filename: "{app}\SNESEmulator.exe"
Name: "{group}\SNESEmulator Manager"; Filename: "{app}\Manager\SNESEmulatorManager.exe"
Name: "{autodesktop}\SNESEmulator"; Filename: "{app}\SNESEmulator.exe"; WorkingDir: "{app}"; Tasks: desktopicon
#endif
[Run]
#ifndef TestBuild
Filename: "{app}\SNESEmulator.exe"; Description: "{cm:LaunchProgram,SNESEmulator}"; Flags: nowait postinstall skipifsilent runasoriginaluser
#endif
; Keep the established per-user location, stable AppId and all user data on update/uninstall.
; Never copy C64's recursive previous-install cleanup or AppData deletion.
