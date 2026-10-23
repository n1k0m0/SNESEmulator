param([string]$OutputDirectory, [string]$DotnetPath = "dotnet")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $commit = (git rev-parse HEAD).Trim()
    $dirty = @(git status --porcelain)
    if (!$OutputDirectory) { $OutputDirectory = Join-Path $root ('artifacts/packages/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
    $destination = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $destination) { throw 'Output must be a new directory; existing data is never overwritten.' }
    New-Item -ItemType Directory -Path $destination | Out-Null
    foreach ($application in @(@('SNESEmulator/SNESEmulator.csproj',$destination), @('SNESEmulatorManager/SNESEmulatorManager.csproj',(Join-Path $destination 'Manager')), @('SNESEmulator.Updater/SNESEmulator.Updater.csproj',(Join-Path $destination 'Updater')))) {
        & $DotnetPath publish $application[0] -c Release -r win-x64 --self-contained true -o $application[1] -p:RuntimeFrameworkVersion=10.0.13 -p:SkipManagerDevelopmentCopy=true -p:RestorePackagesWithLockFile=true -p:RestoreLockedMode=true -p:NuGetLockFilePath=packages.win-x64.lock.json --nologo
        if ($LASTEXITCODE -ne 0) { throw ('Publish failed: '+$application[0]) }
    }
    Copy-Item -LiteralPath LICENSE,NOTICE -Destination $destination
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
                Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object { $_.Name -match '(?i)license|copyright|notice|\.nuspec$' -and $_.Extension -notin '.dll','.pdb','.xml' } | ForEach-Object {
                    $relative = $_.FullName.Substring($directory.TrimEnd('\','/').Length+1)
                    $copy = Join-Path $target $relative
                    New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null
                    Copy-Item -LiteralPath $_.FullName -Destination $copy
                }
            }
        }
    }
    @{ schema=1; enabled=$false; reason='No release source is configured. No network update check or executable download is performed.'; manifestUrl=$null } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'update-policy.json') -Encoding utf8
    $files = @(Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName | ForEach-Object {
        @{ path=$_.FullName.Substring($destination.TrimEnd('\','/').Length+1).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    @{ schema='snes-package-v1'; product='SNESEmulator'; version='0.1.0-preview'; sourceCommit=$commit; sourceDirty=($dirty.Count -gt 0); sourceChanges=$dirty; sdk='10.0.402'; runtime='10.0.13'; rid='win-x64'; selfContained=$true; manager='Manager/SNESEmulatorManager.exe'; files=$files } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'package-manifest.json') -Encoding utf8
    Write-Host "Built isolated package: $destination"
} finally { Pop-Location }
