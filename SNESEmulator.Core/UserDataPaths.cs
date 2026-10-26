// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE and NOTICE for permissions and attribution.
//
// Resolves user-owned state and configuration paths outside installed binaries.
// Environment overrides support isolated tests without touching normal user data.
//

namespace SNESEmulator.Core;

public static class UserDataPaths
{
    public static string GetBaseDirectory() => Environment.GetEnvironmentVariable("SNESEMULATOR_DATA_DIR") is { Length: > 0 } custom
        ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SNESEmulator");
    public static string GetSaveDirectory() => Path.Combine(GetBaseDirectory(), "saves");
    public static string GetMediaDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SNESEmulator");
    public static string GetSettingsPath() => Path.Combine(GetBaseDirectory(), "settings.json");
}
