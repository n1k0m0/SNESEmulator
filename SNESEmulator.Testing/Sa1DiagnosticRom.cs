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
// Builds a project-authored synthetic Sa1 diagnostic cartridge.
// The program exercises implemented hardware; it is not commercial-game compatibility evidence.
//

using SNESEmulator.Core;

namespace SNESEmulator.Testing;
/// <summary>Authored dual-65816 handshake and planar character conversion, without copyrighted game data.</summary>
public static class Sa1DiagnosticRom
{
    public static byte[] Create(bool pal = false, bool conversion1 = false)
    {
        byte[] rom = DiagnosticRom.Create();
        rom[0x7fd5] = 0x23;
        rom[0x7fd6] = 0x35;
        rom[0x7fd8] = 7;
        rom[0x7fd9] = (byte)(pal ? 2 : 1);
        static void Write(List<byte> code, int a, int v) => code.AddRange([0xa9, (byte)v, 0x8d, (byte)a, (byte)(a >> 8)]);
        var secondary = new List<byte>();
        // CIWP initially disables stack/direct writes. Enable every page before
        // using either, matching the real SA1-Root v1.8 hardware startup fix.
        Write(secondary, 0x222a, 255);
        Write(secondary, 0x220c, 0);
        Write(secondary, 0x220d, 0xa1);
        Write(secondary, 0x220e, 0);
        Write(secondary, 0x220f, 0xa0);
        Write(secondary, 0x2250, 0);
        Write(secondary, 0x2251, 7);
        Write(secondary, 0x2252, 0);
        Write(secondary, 0x2253, 9);
        Write(secondary, 0x2254, 0);
        secondary.AddRange([0xea, 0xea, 0xea, 0xad, 6, 0x23, 0x8d, 1, 0x30]);
        if (conversion1)
        {
            Write(secondary, 0x2227, 128);
            secondary.AddRange([0xa9, 0x0c, 0x8f, 0, 0, 0x40]);
            Write(secondary, 0x2230, 0xb1);
            Write(secondary, 0x2231, 2);
            Write(secondary, 0x2232, 0);
            Write(secondary, 0x2233, 0);
            Write(secondary, 0x2234, 0x40);
            Write(secondary, 0x2235, 0);
            Write(secondary, 0x2236, 1);
        }
        else
        {
            // BRF row: pixel1 color3, other pixels transparent. CC2 emits40/40.
            Write(secondary, 0x2230, 0xa0);
            Write(secondary, 0x2231, 2);
            Write(secondary, 0x2235, 0);
            Write(secondary, 0x2236, 1);
            for (int i = 0; i < 8; i++)
                Write(secondary, 0x2240 + i, i == 1 ? 3 : 0);
        }

        for (int i = 0; i < 10; i++)
            secondary.Add(0xea);
        Write(secondary, 0x3000, 0xa5);
        Write(secondary, 0x2209, 0xd0);
        secondary.Add(0xdb);
        secondary.ToArray().CopyTo(rom, 0x1000);
        new byte[]
        {
            0xa9,
            0xc7,
            0x85,
            0x10,
            0xa9,
            0x80,
            0x8d,
            2,
            0x22,
            0x40
        }.CopyTo(rom, 0x2000);
        new byte[]
        {
            0xa9,
            0xd8,
            0x85,
            0x12,
            0x40
        }.CopyTo(rom, 0x2100);
        var main = new List<byte>();
        main.Add(0x58);
        Write(main, 0x2201, 0x80);
        Write(main, 0x4200, 0x80);
        Write(main, 0x2229, 255);
        Write(main, 0x2203, 0);
        Write(main, 0x2204, 0x90);
        Write(main, 0x2200, 0);
        main.AddRange([0xad, 0, 0x30, 0xc9, 0xa5, 0xd0, 0xf9, 0xad, 1, 0x30, 0x85, 0]);
        if (conversion1)
            main.AddRange([0xad, 0, 0x23, 0x29, 0x20, 0xf0, 0xf9]);
        // CPU DMA reads the SA-1-produced planar row at00:3100/3101 into
        // PPU VRAM0100/0101. The stable next field contains one white pixel.
        Write(main, 0x2115, 0x80);
        Write(main, 0x2116, 0x80);
        Write(main, 0x2117, 0);
        Write(main, 0x4300, 1);
        Write(main, 0x4301, 0x18);
        Write(main, 0x4302, 0);
        Write(main, 0x4303, conversion1 ? 0 : 0x31);
        Write(main, 0x4304, conversion1 ? 0x40 : 0);
        Write(main, 0x4305, 2);
        Write(main, 0x4306, 0);
        Write(main, 0x420b, 1);
        Write(main, 0x2121, 3);
        Write(main, 0x2122, 255);
        Write(main, 0x2122, 127);
        Write(main, 0x2116, 0);
        Write(main, 0x2117, 0x10);
        Write(main, 0x2118, 0x10);
        Write(main, 0x2119, 0);
        Write(main, 0x2107, 0x10);
        Write(main, 0x210b, 0);
        Write(main, 0x2105, 0);
        Write(main, 0x210e, 255);
        Write(main, 0x210e, 255);
        Write(main, 0x212c, 1);
        Write(main, 0x2100, 15);
        main.Add(0xcb);
        main.Add(0xdb);
        main.ToArray().CopyTo(rom, 0);
        return rom;
    }
}
