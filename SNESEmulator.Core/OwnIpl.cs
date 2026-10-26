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
// Provides the project-authored APU boot routine without Nintendo firmware.
// Its reset epoch is intentionally distinguished from original-IPL reference tests.
//

namespace SNESEmulator.Core;

/// <summary>Own open boot program, assembled from tools/own-ipl.py. Compatible upload protocol, different timing from Nintendo IPL.</summary>
public static class OwnIpl
{
    public static byte[] Bytes=>Convert.FromHexString(Hex);
    public const string Hex="20CDEFBDE800C61DD0FC8FAAF48FBBF578CCF4D0FBBAF6DA00BAF4C4F4DD5DF019EBF4D0FC7EF4D00DE4F5D700CBF4FCD0F3AB012FEF30DD2FEB1F000000C0FF";
}
