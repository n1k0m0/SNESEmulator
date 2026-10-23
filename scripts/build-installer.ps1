param([Parameter(Mandatory=$true)][string]$PackageDirectory, [string]$OutputDirectory, [switch]$IsolatedTest)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$package=[IO.Path]::GetFullPath($PackageDirectory)
if(!(Test-Path -LiteralPath (Join-Path $package 'package-manifest.json'))){throw 'Package manifest missing'}
& (Join-Path $package 'Updater/SNESEmulator.Updater.exe') verify $package
if($LASTEXITCODE -ne 0){throw 'Package verification failed'}
$compiler=Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'
if(!(Test-Path -LiteralPath $compiler)){throw 'Install Inno Setup6 separately or configure the documented compiler path'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/installer-'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Output directory already exists'}
New-Item -ItemType Directory -Path $output | Out-Null
$arguments=@(('/DPackageDir='+$package),('/DOutputDir='+$output))
if($IsolatedTest){$arguments+='/DTestBuild=1'}
$arguments+=Join-Path $root 'installer/SNESEmulator.iss'
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Installer compile failed'}
@{compiler=$compiler;sha256=(Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash;isolatedTest=[bool]$IsolatedTest;sourceCommit=(git -C $root rev-parse HEAD)} | ConvertTo-Json | Set-Content (Join-Path $output 'provenance.json') -Encoding utf8
