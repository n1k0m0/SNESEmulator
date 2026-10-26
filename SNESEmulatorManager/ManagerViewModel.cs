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
// Coordinates library, settings and live-session state for the WPF Manager.
// Asynchronous operations retain session/revision identity before publishing results.
//

using System.ComponentModel;
using SNESEmulator.Contracts;
using SNESEmulator.Management;

namespace SNESEmulatorManager;
/// <summary>Long-lived connection identity and unacknowledged drafts survive discovery refreshes.</summary>
public sealed class ManagerViewModel : INotifyPropertyChanged
{
    private InstanceInfo? selected;
    public InstanceInfo? Selected
    {
        get => selected;
        set
        {
            selected = value;
            PropertyChanged?.Invoke(this, new(nameof(Selected)));
        }
    }

    public InstanceInfo? SettingsInstance { get; set; }
    public SettingsInfo? LoadedSettings { get; set; }
    public bool SettingsDirty { get; set; }
    public long Revision { get; set; }
    public List<GameItem> Games { get; set; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
}
