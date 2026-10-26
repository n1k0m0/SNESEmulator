@echo off
rem Copyright 2026 Nils Kopal
rem SPDX-License-Identifier: Apache-2.0
rem
rem Licensed under the Apache License, Version 2.0 (the "License");
rem you may not use this file except in compliance with the License.
rem You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
rem Unless required by applicable law or agreed to in writing, software
rem distributed under the License is distributed on an "AS IS" BASIS,
rem WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
rem See LICENSE and NOTICE for permissions and attribution.
rem
rem Launches the packaged emulator or development project while preserving quoted arguments.
rem

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
