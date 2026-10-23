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
[Setup]
AppId={#ProductId}
AppName={#DisplayName}
AppVersion=0.1.0
AppPublisher=Nils Kopal
DefaultDirName={localappdata}\Programs\SNESEmulator
DefaultGroupName={#DisplayName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=SNESEmulator-0.1.0-preview-win-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\SNESEmulator.exe
LicenseFile=..\LICENSE
DisableProgramGroupPage=yes
[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
#ifndef TestBuild
Name: "{group}\SNESEmulator"; Filename: "{app}\SNESEmulator.exe"
Name: "{group}\SNESEmulator Manager"; Filename: "{app}\Manager\SNESEmulatorManager.exe"
#endif
; Deliberately no associations, Run, UninstallDelete, AppData deletion or user-data cleanup.
