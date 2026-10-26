param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Compiler staging directory must be new'}
New-Item -ItemType Directory -Path $output | Out-Null
$installer=Join-Path $output 'innosetup-6.7.3.exe'
# Pin the official release, rather than a mutable third-party package-manager recipe.
$url='https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$expected='9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
Invoke-WebRequest -Uri $url -OutFile $installer
if((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'Official compiler installer hash mismatch'}
$install=Join-Path $output 'compiler'
$arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS',('/DIR="'+$install+'"'))
$process=Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0){throw ('Compiler installation failed: '+$process.ExitCode)}
$compiler=Join-Path $install 'ISCC.exe'
if(!(Test-Path -LiteralPath $compiler)){throw 'Installed compiler missing'}
@{version='6.7.3';source=$url;installerSha256=$expected;compilerSha256=(Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant()} | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $output 'provenance.json')
if($env:GITHUB_OUTPUT){('compiler='+$compiler) | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append}
Write-Host ('Compiler ready: '+$compiler)
