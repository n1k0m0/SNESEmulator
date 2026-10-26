# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See LICENSE and NOTICE for permissions and attribution.
#
# Compiles a version-matched setup from an already verified self-contained package.
#

param([Parameter(Mandatory=$true)][string]$PackageDirectory, [string]$OutputDirectory, [switch]$IsolatedTest, [string]$CompilerPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$package=[IO.Path]::GetFullPath($PackageDirectory)
if(!(Test-Path -LiteralPath (Join-Path $package 'package-manifest.json'))){throw 'Package manifest missing'}
& (Join-Path $package 'Updater/SNESEmulator.Updater.exe') verify $package
if($LASTEXITCODE -ne 0){throw 'Package verification failed'}
$manifest=Get-Content -Raw -LiteralPath (Join-Path $package 'package-manifest.json') | ConvertFrom-Json
$version=$manifest.version
if($version -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$'){throw 'Invalid package version'}
$numericVersion=$matches[1]+'.'+$matches[2]+'.'+$matches[3]+'.0'
$info=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package 'SNESEmulator.exe'))
if($info.FileVersion -ne $numericVersion -or $info.ProductVersion.Split('+')[0] -ne $version){throw 'App version and package manifest disagree'}
if(!$CompilerPath){
    $candidates=@((Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'))
    foreach($base in @(${env:ProgramFiles(x86)},$env:ProgramFiles)){if($base){$candidates+=Join-Path $base 'Inno Setup 6/ISCC.exe'}}
    $command=Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if($command){$candidates+=$command.Source}
    $CompilerPath=$candidates | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
}
if(!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)){throw 'Inno Setup 6 compiler not found; pass -CompilerPath or install Inno Setup 6'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/installer-'+[guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Output directory already exists'}
New-Item -ItemType Directory -Path $output | Out-Null
$arguments=@(('/DPackageDir='+$package),('/DOutputDir='+$output),('/DAppVersion='+$version),('/DAppVersionInfo='+$numericVersion))
if($IsolatedTest){$arguments+='/DTestBuild=1'}
$arguments+=Join-Path $root 'installer/SNESEmulator.iss'
& $CompilerPath @arguments
if($LASTEXITCODE -ne 0){throw 'Installer compile failed'}
@{compiler=$CompilerPath;sha256=(Get-FileHash -LiteralPath $CompilerPath -Algorithm SHA256).Hash;isolatedTest=[bool]$IsolatedTest;version=$version;sourceCommit=$manifest.sourceCommit} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'provenance.json') -Encoding utf8
