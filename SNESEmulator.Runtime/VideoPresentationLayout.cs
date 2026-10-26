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
// Calculates FIT and STRETCH destination rectangles for the available viewport.
// Scaling changes presentation only; it never crops or alters the native PPU buffer.
//

namespace SNESEmulator.Runtime;

/// <summary>Presentation only: the SNES picture has a 4:3 display aspect, including hi-res fields.</summary>
public readonly record struct VideoPresentationLayout(int X, int Y, int Width, int Height)
{
    public static VideoPresentationLayout Calculate(int clientWidth, int clientHeight, bool stretch,
        int logicalWidth = 512, int logicalHeight = 288)
    {
        clientWidth = Math.Max(1, clientWidth);
        clientHeight = Math.Max(1, clientHeight);
        if (stretch) return new(0, 0, logicalWidth, logicalHeight);
        double scale = Math.Min(clientWidth / 4.0, clientHeight / 3.0);
        int width = Math.Clamp((int)Math.Round(scale * 4 * logicalWidth / clientWidth), 1, logicalWidth);
        int height = Math.Clamp((int)Math.Round(scale * 3 * logicalHeight / clientHeight), 1, logicalHeight);
        return new((logicalWidth - width) / 2, (logicalHeight - height) / 2, width, height);
    }
}
