@echo off
setlocal
if exist "%~dp0SNESEmulator.exe" goto standalone
if exist "%~dp0publish\package-manifest.json" if exist "%~dp0publish\SNESEmulator.exe" goto published
pushd "%~dp0"
dotnet run --project SNESEmulator -c Release -- %*
set "result=%errorlevel%"
popd
exit /b %result%
:standalone
"%~dp0SNESEmulator.exe" %*
exit /b %errorlevel%
:published
"%~dp0publish\SNESEmulator.exe" %*
exit /b %errorlevel%
