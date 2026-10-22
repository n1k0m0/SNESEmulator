$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet publish SNESEmulator/SNESEmulator.csproj -c Release -r win-x64 --self-contained true -o publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath LICENSE,NOTICE -Destination publish
    Write-Host "Ready: $((Get-Location).Path)\publish\SNESEmulator.exe"
} finally { Pop-Location }
