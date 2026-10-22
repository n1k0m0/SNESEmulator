@echo off
cd /d "%~dp0"
if exist "publish\SNESEmulator.exe" (
    start "" "publish\SNESEmulator.exe" %*
) else (
    dotnet run --project SNESEmulator -c Release -- %*
)
