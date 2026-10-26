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
// Builds a project-authored synthetic Dsp1 diagnostic cartridge.
// The program exercises implemented hardware; it is not commercial-game compatibility evidence.
//

using SNESEmulator.Core;
namespace SNESEmulator.Testing;

// Authored command/parameter/result exchange. This deliberately small protocol
// exercises the physical ports; it is not Nintendo firmware or a game oracle.
public static class Dsp1DiagnosticRom
{
    static uint L(int dest, int value) => 0xc00000u | ((uint)(ushort)value << 6) | (uint)dest;
    static uint J(int condition, int target) => 0x800000u | ((uint)condition << 13) | ((uint)target << 2);
    static uint O(int source, int dest, int alu = 0, int acc = 0, int p = 1) => ((uint)p << 20) | ((uint)alu << 16) | ((uint)acc << 15) | ((uint)source << 4) | (uint)dest;
    static byte[] Assemble(params uint[] words)
    {
        var bytes = new byte[8192]; for (int i = 0; i < words.Length; i++) { bytes[i * 3] = (byte)words[i]; bytes[i * 3 + 1] = (byte)(words[i] >> 8); bytes[i * 3 + 2] = (byte)(words[i] >> 16); }
        for (int i = 0; i < 1024; i++) { int v = (ushort)(i * 257 + 0x1234); bytes[6144 + i * 2] = (byte)v; bytes[6145 + i * 2] = (byte)(v >> 8); }
        return bytes;
    }
    public static byte[] Firmware() => Assemble(L(7, 0x400), O(8, 3), J(0xbe, 2), L(7, 0), O(8, 3), J(0xbe, 5), O(9, 1), L(3, 1), O(3, 0, 5), O(1, 6), J(0xbe, 10), J(0x100, 0));
    public static byte[] Create(Dsp1Board board)
    {
        byte[] original = DiagnosticRom.Create(), rom = board is Dsp1Board.LoRom1MiB or Dsp1Board.LoRom2MiB ? original : new byte[65536];
        bool low = board is Dsp1Board.LoRom1MiB or Dsp1Board.LoRom2MiB;
        int h = low ? 0x7fc0 : 0xffc0; if (!low) original.AsSpan(0x7fc0, 64).CopyTo(rom.AsSpan(h)); rom[h + 0x15] = (byte)(low ? 0x20 : 0x21); rom[h + 0x16] = 3;
        int port = board switch { Dsp1Board.LoRom1MiB => 0x308000, Dsp1Board.LoRom2MiB => 0x600000, _ => 0x006000 }; int status = port + (low ? 0x4000 : 0x1000);
        var code = new List<byte>(); void Long(byte op, int a) { code.Add(op); code.Add((byte)a); code.Add((byte)(a >> 8)); code.Add((byte)(a >> 16)); }
        void Wait() { Long(0xaf, status); code.Add(0x10); code.Add(0xfa); }
        Wait(); code.AddRange([0xa9, 0x42]); Long(0x8f, port); Wait(); code.AddRange([0xa9, 0x34]); Long(0x8f, port); code.AddRange([0xa9, 0x12]); Long(0x8f, port); Wait(); Long(0xaf, port); code.AddRange([0x85, 0x00]); Long(0xaf, port); code.AddRange([0x85, 0x01, 0xdb]); code.ToArray().CopyTo(rom, low ? 0 : 0x8000);
        return rom;
    }
}
