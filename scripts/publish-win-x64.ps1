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
# Publishes all three applications with pinned runtime versions, file inventory and license notices.
#

param([string]$OutputDirectory, [string]$DotnetPath = "dotnet", [string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (!$Version) { $Version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim() }
    if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') { throw 'Invalid release version' }
    $commit = (git rev-parse HEAD).Trim()
    $dirty = @(git status --porcelain)
    if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/packages/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
    $destination = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $destination) { throw 'Output must be a new directory; existing data is never overwritten.' }
    New-Item -ItemType Directory -Path $destination | Out-Null
    foreach ($application in @(@('SNESEmulator/SNESEmulator.csproj',$destination), @('SNESEmulatorManager/SNESEmulatorManager.csproj',(Join-Path $destination 'Manager')), @('SNESEmulator.Updater/SNESEmulator.Updater.csproj',(Join-Path $destination 'Updater')))) {
        & $DotnetPath publish $application[0] -c Release -r win-x64 --self-contained true -o $application[1] "-p:Version=$Version" -p:RuntimeFrameworkVersion=10.0.13 -p:SkipManagerDevelopmentCopy=true -p:RestorePackagesWithLockFile=true -p:RestoreLockedMode=true -p:NuGetLockFilePath=packages.win-x64.lock.json --nologo
        if ($LASTEXITCODE -ne 0) { throw ('Publish failed: '+$application[0]) }
    }
    Copy-Item -LiteralPath LICENSE,NOTICE,THIRD-PARTY-NOTICES.md -Destination $destination
    Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination (Join-Path $destination 'licenses') -Recurse
    Copy-Item -LiteralPath Start-SNESEmulator.cmd -Destination $destination
    New-Item -ItemType Directory -Path (Join-Path $destination 'dependency-locks') | Out-Null
    Get-ChildItem -LiteralPath $root -Directory | ForEach-Object {
        foreach ($lockName in @('packages.lock.json','packages.win-x64.lock.json')) {
            $lock = Join-Path $_.FullName $lockName
            if (Test-Path -LiteralPath $lock) { Copy-Item -LiteralPath $lock -Destination (Join-Path $destination ('dependency-locks/'+$_.Name+'.'+$lockName)) }
        }
    }
    $notices = Join-Path $destination 'third-party-notices'
    New-Item -ItemType Directory -Path $notices | Out-Null
    $assets = @('SNESEmulator/obj/project.assets.json','SNESEmulatorManager/obj/project.assets.json')
    $seen = @{}
    foreach ($asset in $assets) {
        $graph = Get-Content -LiteralPath $asset -Raw | ConvertFrom-Json
        foreach ($package in $graph.libraries.PSObject.Properties) {
            if ($package.Value.type -ne 'package' -or $seen.ContainsKey($package.Name)) { continue }
            $seen[$package.Name] = $true
            foreach ($cache in $graph.packageFolders.PSObject.Properties.Name) {
                $directory = Join-Path $cache $package.Value.path
                if (!(Test-Path -LiteralPath $directory)) { continue }
                $target = Join-Path $notices ($package.Name.Replace('/','-'))
                New-Item -ItemType Directory -Path $target -Force | Out-Null
                Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object { $_.Name -match '(?i)license|copying|copyright|notice|\.nuspec$' -and $_.Extension -notin '.dll','.pdb','.xml' } | ForEach-Object {
                    $relative = $_.FullName.Substring($directory.TrimEnd('\','/').Length+1)
                    $copy = Join-Path $target $relative
                    New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null
                    Copy-Item -LiteralPath $_.FullName -Destination $copy
                }
            }
        }
    }
    # Framework packs are not ordinary project.assets libraries. Their full MIT
    # and third-party notices must also travel with this self-contained package.
    $cacheRoots=@((Get-Content -LiteralPath $assets[0] -Raw | ConvertFrom-Json).packageFolders.PSObject.Properties.Name)
    foreach($pack in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')){
        $packDirectory=$cacheRoots | ForEach-Object {Join-Path $_ ($pack+'/10.0.13')} | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
        if(!$packDirectory){throw ('Runtime license pack missing: '+$pack)}
        $target=Join-Path $notices ($pack+'-10.0.13')
        New-Item -ItemType Directory -Path $target | Out-Null
        $packNotices=@(Get-ChildItem -LiteralPath $packDirectory -File | Where-Object {$_.Name -match '(?i)license|notice|\.nuspec$'})
        if(!$packNotices.Count){throw ('Runtime notices missing: '+$pack)}
        $packNotices | Copy-Item -Destination $target
    }
    # This legacy ZIP policy is independent of the normal GUI's official GitHub setup check.
    @{ schema=1; enabled=$false; reason='No custom ZIP manifest is configured. Normal GUI startup checks official GitHub setup releases.'; manifestUrl=$null } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'update-policy.json') -Encoding utf8
    if(!$dirty.Count -and @(git status --porcelain).Count){throw 'Publishing changed tracked source files; refusing a falsely clean package'}
    $files = @(Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName | ForEach-Object {
        @{ path=$_.FullName.Substring($destination.TrimEnd('\','/').Length+1).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    @{ schema='snes-package-v1'; product='SNESEmulator'; version=$Version; sourceCommit=$commit; sourceDirty=($dirty.Count -gt 0); sourceChanges=$dirty; sdk='10.0.402'; runtime='10.0.13'; rid='win-x64'; selfContained=$true; manager='Manager/SNESEmulatorManager.exe'; files=$files } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'package-manifest.json') -Encoding utf8
    Write-Host "Built isolated package: $destination"
} finally { Pop-Location }
