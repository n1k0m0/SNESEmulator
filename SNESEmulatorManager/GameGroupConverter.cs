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
// Groups library entries for presentation in WPF bindings.
// Grouping is a view concern and does not modify cartridge metadata.
//

using System.Globalization;
using System.Windows.Data;
using SNESEmulator.Management;

namespace SNESEmulatorManager;
internal sealed class GameGroupConverter(Func<IReadOnlyList<GameItem>> games) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => games().FirstOrDefault(g => g.Hash == (string)value)?.Title ?? "Unassigned";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
