param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$DotnetPath='dotnet',
    [string]$CompilerPath,
    [string]$Repository='n1k0m0/SNESEmulator',
    [switch]$AllowDirty
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $version=(Get-Content -LiteralPath VERSION -Raw).Trim()
    if($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'){throw 'Invalid release repository'}
    if(!$AllowDirty -and @(git status --porcelain).Count){throw 'Release builds require a clean working tree'}
    $output=[IO.Path]::GetFullPath($OutputDirectory)
    if(Test-Path -LiteralPath $output){throw 'Release output directory already exists'}
    New-Item -ItemType Directory -Path $output | Out-Null
    $package=Join-Path $output 'package'
    & (Join-Path $PSScriptRoot 'publish-win-x64.ps1') -OutputDirectory $package -DotnetPath $DotnetPath -Version $version
    $manifest=Get-Content -Raw -LiteralPath (Join-Path $package 'package-manifest.json') | ConvertFrom-Json
    if(!$AllowDirty -and $manifest.sourceDirty){throw 'Refusing to release a dirty package'}
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -PackageDirectory $package -OutputDirectory (Join-Path $output 'installer') -CompilerPath $CompilerPath
    $assets=Join-Path $output 'assets'
    New-Item -ItemType Directory -Path $assets | Out-Null
    $stem='SNESEmulator-'+$version+'-win-x64'
    $setup=$stem+'-setup.exe'
    Copy-Item -LiteralPath (Join-Path $output ('installer/'+$setup)) -Destination (Join-Path $assets $setup)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath=Join-Path $assets ($stem+'.zip')
    # Write files only: the existing update extractor deliberately rejects directory entries.
    $zip=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($file in (Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName)){
            $relative=$file.FullName.Substring($package.TrimEnd('\','/').Length+1).Replace('\','/')
            if($file.Extension -in '.sfc','.smc','.rom','.srm','.snessav','.pfx','.pem','.key'){throw ('Runtime package must not distribute media, saves or keys: '+$relative)}
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {$zip.Dispose()}
    $zipFile=Get-Item -LiteralPath $zipPath
    $tag='v'+$version
    @{schema=1;version=$version;packageUrl=('https://github.com/'+$Repository+'/releases/download/'+$tag+'/'+$stem+'.zip');bytes=$zipFile.Length;sha256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()} | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $assets 'update.json')
    $sums=@(Get-ChildItem -LiteralPath $assets -File | Sort-Object Name | ForEach-Object {(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name})
    [IO.File]::WriteAllLines((Join-Path $assets 'SHA256SUMS.txt'),$sums,[Text.UTF8Encoding]::new($false))
    $verifyArgs=@((Join-Path $root 'tools/verify-release-assets.py'),'--assets',$assets,'--version',$version,'--repository',$Repository,'--commit',$manifest.sourceCommit)
    if($AllowDirty){$verifyArgs+='--allow-dirty'}
    & python @verifyArgs
    if($LASTEXITCODE -ne 0){throw 'Release asset verification failed'}
    @{version=$version;tag=$tag;sourceCommit=$manifest.sourceCommit;sourceDirty=$manifest.sourceDirty;repository=$Repository;assets=$assets;selfContained=$true;prerelease=$version.Contains('-')} | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $output 'release-info.json')
    Write-Host ('Verified release assets: '+$assets)
} finally {Pop-Location}
