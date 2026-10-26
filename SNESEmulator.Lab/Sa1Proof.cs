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
// Checks authored SA-1 board integration, state continuation and independent reference differences.
//

using System.Text.Json;
using System.IO.Compression;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;
using SNESEmulator.Testing;

static class Sa1Proof
{
    static void Check(bool value, string message)
    {
        if (!value)
            throw new InvalidDataException(message);
    }

    static Sa1CartridgeHardware Chip(out byte[] rom, out byte[] ram)
    {
        rom = new byte[0x20000];
        ram = new byte[0x20000];
        return new(new(), rom, ram);
    }

    static void Write(Sa1CartridgeHardware c, int a, int v) => ((ICpuBus)c).Write(a, (byte)v);
    static byte Read(Sa1CartridgeHardware c, int a) => ((ICpuBus)c).Read(a);
    static SnesSystem Machine(HardwareProfile p, bool conversion1 = false)
    {
        var m = new SnesSystem(p);
        m.Insert(new Cartridge(Sa1DiagnosticRom.Create(p.Pal, conversion1), hardware: new Sa1Configuration()));
        return m;
    }

    static SnesSystem Load(Cartridge c, byte[] raw)
    {
        using var stream = new MemoryStream();
        using (var zip = new GZipStream(stream, CompressionLevel.Fastest, true))
            zip.Write(raw);
        stream.Position = 0;
        return StateSerializer.Load(c, stream);
    }

    static string State(Sa1CartridgeHardware c) => JsonSerializer.Serialize(c.Capture(c.Clock, HardwareProfile.Ntsc));
    public static int Export(string[] args)
    {
        string dir = args.FirstOrDefault() ?? "testdata/sa1";
        Directory.CreateDirectory(dir);
        foreach (bool pal in new[]
        {
            false,
            true
        }

        )
            foreach (bool cc1 in new[]
            {
                false,
                true
            }

            )
                File.WriteAllBytes(Path.Combine(dir, $"own-{(pal ? "pal" : "ntsc")}-{(cc1 ? "cc1" : "cc2")}.sfc"), Sa1DiagnosticRom.Create(pal, cc1));
        return 0;
    }

    public static int Run(string[] args)
    {
        var report = new TestReport("SA1-functional-digital-model");
        using (var manifest = JsonDocument.Parse(File.ReadAllBytes("testdata/sa1/manifest.json")))
        {
            foreach (var pair in manifest.RootElement.GetProperty("files").EnumerateObject())
                Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes("testdata/sa1/" + pair.Name))).Equals(pair.Value.GetString(), StringComparison.OrdinalIgnoreCase), "Changed authored SA1 fixture: " + pair.Name);
        }

        foreach (bool pal in new[]
        {
            false,
            true
        }

        )
            foreach (bool cc1 in new[]
            {
                false,
                true
            }

            )
                Check(File.ReadAllBytes($"testdata/sa1/own-{(pal ? "pal" : "ntsc")}-{(cc1 ? "cc1" : "cc2")}.sfc").SequenceEqual(Sa1DiagnosticRom.Create(pal, cc1)), "Authored ROM generator changed without reviewed fixture generation");
        void Test(string name, Action action)
        {
            try
            {
                action();
                report.Add(name, "pass", "");
            }
            catch (Exception e)
            {
                report.Add(name, "fail", e.ToString());
            }
        }

        Test("manufacturer SIWP/CIWP disabled at reset and independent writer permissions", () =>
        {
            var c = Chip(out _, out _);
            c.TryWrite(0x3000, 0x11, 0, HardwareProfile.Ntsc);
            Write(c, 0, 0x22);
            Check(c.Iram[0] == 0, "Reset must protect both writers");
            c.TryWrite(0x2229, 1, 0, HardwareProfile.Ntsc);
            c.TryWrite(0x3000, 0x33, 0, HardwareProfile.Ntsc);
            Write(c, 0, 0x44);
            Check(c.Iram[0] == 0x33, "SIWP must not enable CIWP");
            Write(c, 0x222a, 2);
            Write(c, 0x100, 0x55);
            Write(c, 0, 0x66);
            Check(c.Iram[0x100] == 0x55 && c.Iram[0] == 0x33, "Literal per256-byte pages");
        });
        Test("manufacturer signed products and five-cycle visibility", () =>
        {
            foreach (var pair in new[]
            {
                (-7, 9),
                (-32768, -32768),
                (32767, -32768),
                (0, 123),
                (123, -456)
            }

            )
            {
                var c = Chip(out _, out _);
                Write(c, 0x2251, pair.Item1);
                Write(c, 0x2252, pair.Item1 >> 8);
                Write(c, 0x2253, pair.Item2);
                Write(c, 0x2254, pair.Item2 >> 8);
                c.Synchronize(9, HardwareProfile.Ntsc);
                Check(Read(c, 0x2306) == 0, "Result changed before five clocks");
                c.Synchronize(10, HardwareProfile.Ntsc);
                uint got = 0;
                for (int i = 0; i < 4; i++)
                    got |= (uint)Read(c, 0x2306 + i) << (i * 8);
                Check(got == unchecked((uint)(pair.Item1 * pair.Item2)), "Signed product");
            }
        });
        Test("division floor identity unsigned remainder and divide-by-zero", () =>
        {
            foreach (int dividend in new[]
            {
                -32768,
                -32767,
                -7,
                -1,
                0,
                1,
                7,
                32767
            }

            )
                foreach (int divisor in new[]
                {
                    0,
                    1,
                    2,
                    3,
                    32768,
                    65535
                }

                )
                {
                    var c = Chip(out _, out _);
                    Write(c, 0x2250, 1);
                    Write(c, 0x2251, dividend);
                    Write(c, 0x2252, dividend >> 8);
                    Write(c, 0x2253, divisor);
                    Write(c, 0x2254, divisor >> 8);
                    c.Synchronize(10, HardwareProfile.Ntsc);
                    int q = (short)(Read(c, 0x2306) | Read(c, 0x2307) << 8), r = Read(c, 0x2308) | Read(c, 0x2309) << 8;
                    Check(divisor == 0 ? q == 0 && r == 0 : q * divisor + r == dividend && r >= 0 && r < divisor, "Division invariant");
                }
        });
        Test("cumulative modes2 and3 plus six-cycle visibility", () =>
        {
            foreach (int mode in new[]
            {
                2,
                3
            }

            )
            {
                var c = Chip(out _, out _);
                Write(c, 0x2250, mode);
                Write(c, 0x2251, 7);
                Write(c, 0x2253, 9);
                Write(c, 0x2254, 0);
                c.Synchronize(11, HardwareProfile.Ntsc);
                Check(Read(c, 0x2306) == 0, "Early accumulate");
                c.Synchronize(12, HardwareProfile.Ntsc);
                Check(Read(c, 0x2306) == 63, "Mode2/3 accumulated product");
                Write(c, 0x2254, 0);
                c.Synchronize(24, HardwareProfile.Ntsc);
                Check(Read(c, 0x2306) == 126, "Second accumulated product");
            }
        });
        Test("bitmap2/4bpp literal packing and write-protected BW prefix", () =>
        {
            var c = Chip(out _, out var ram);
            Write(c, 0x400000, 0xff);
            Check(ram[0] == 0, "BW reset protection");
            Write(c, 0x2227, 128);
            Write(c, 0x223f, 128);
            Write(c, 0x600000, 1);
            Write(c, 0x600001, 2);
            Write(c, 0x600002, 3);
            Check(ram[0] == 0x39, "Literal 2bpp packing");
            Write(c, 0x223f, 0);
            Write(c, 0x600001, 0xa);
            Check(ram[0] == 0xa9, "Literal4bpp read/modify/write");
        });
        Test("MMC literal bank projection and host FastROM override", () =>
        {
            var rom = new byte[0x400000];
            var ram = new byte[0x20000];
            for (int i = 0; i < 4; i++)
                Array.Fill(rom, (byte)(0x10 + i), i * 0x100000, 0x100000);
            var c = new Sa1CartridgeHardware(new(), rom, ram);
            Check(Read(c, 0xc00000) == 0x10 && Read(c, 0xf00000) == 0x13, "Initial high mapping");
            c.TryWrite(0x2220, 0x82, 0, HardwareProfile.Ntsc);
            Check(Read(c, 0xc00000) == 0x12 && Read(c, 0x008000) == 0x12, "Explicit CX projection");
            Check(c.HostAccessClocks(new(0xc08000, null, "-p-r----"), 6) == 8, "FastROM bypass");
        });
        Test("SCPU and SA1 retain separate nonzero open-bus histories", () =>
        {
            var c = Chip(out _, out _);
            Write(c, 0x222a, 0xa7);
            c.TryWrite(0x2229, 0x5b, 0, HardwareProfile.Ntsc);
            c.TryRead(0x230e, 0xd3, 0, HardwareProfile.Ntsc, out byte host);
            Check(host == 0xd3 && Read(c, 0x2000) == 0xa7, "Independent processor MDR histories");
        });
        Test("SNV SIV legal SA1 writer and illegal SCPU writer", () =>
        {
            var c = Chip(out _, out _);
            Write(c, 0x220c, 0x34);
            Write(c, 0x220d, 0x92);
            Write(c, 0x220e, 0x78);
            Write(c, 0x220f, 0x96);
            Write(c, 0x2209, 0x50);
            c.TryWrite(0x220c, 0xaa, 0, HardwareProfile.Ntsc);
            c.TryRead(0xfffa, 0, 0, HardwareProfile.Ntsc, out byte n);
            c.TryRead(0xfffe, 0, 0, HardwareProfile.Ntsc, out byte q);
            Check(n == 0x34 && q == 0x78, "Actual SCPU vector overrides and access polarity");
        });
        Test("variable bit reader1..16 cross-byte fixed and auto mode", () =>
        {
            var c = Chip(out var rom, out _);
            rom[0] = 0xd6;
            rom[1] = 0x39;
            rom[2] = 0xa7;
            rom[3] = 0x4c;
            for (int n = 1; n <= 16; n++)
            {
                Write(c, 0x2258, 0x80 | (n & 15));
                Write(c, 0x2259, 0);
                Write(c, 0x225a, 0x80);
                Write(c, 0x225b, 0);
                int first = Read(c, 0x230c) | Read(c, 0x230d) << 8;
                int second = Read(c, 0x230c) | Read(c, 0x230d) << 8;
                Check(first == 0x39d6 && second == ((0x4ca739d6u >> n) & 65535), "Literal serial bit stream at length" + n);
            }
        });
        Test("timer free-running latch H/V enable compare reset linear wrap", () =>
        {
            var c = Chip(out _, out _);
            c.Synchronize(20, HardwareProfile.Ntsc);
            Check(Read(c, 0x2302) == 5, "Counter runs while IRQ disabled");
            Write(c, 0x2211, 0);
            Write(c, 0x2212, 3);
            Write(c, 0x2210, 1);
            c.Synchronize(31, HardwareProfile.Ntsc);
            Check((Read(c, 0x2301) & 0x40) == 0, "Early timer edge");
            c.Synchronize(32, HardwareProfile.Ntsc);
            Check((Read(c, 0x2301) & 0x40) != 0, "Exact4MC compare edge");
            Write(c, 0x220b, 0x40);
            Write(c, 0x2211, 0);
            Write(c, 0x2210, 0x80);
            c.Synchronize(32 + 4 * 512, HardwareProfile.Ntsc);
            Check(Read(c, 0x2302) == 0 && Read(c, 0x2304) == 1, "Linear512 dot wrap");
        });
        Test("normal DMA four legal routes byte endpoints and completion IRQ", () =>
        {
            foreach (var route in new[]
            {
                (0, 0),
                (0, 4),
                (1, 0),
                (2, 4)
            }

            )
            {
                var c = Chip(out var rom, out var ram);
                rom[0] = 0x21;
                rom[1] = 0x87;
                ram[0] = 0x21;
                ram[1] = 0x87;
                c.Iram[0] = 0x21;
                c.Iram[1] = 0x87;
                Write(c, 0x2230, 0xc0 | route.Item1 | route.Item2);
                Write(c, 0x2232, 0);
                Write(c, 0x2233, route.Item1 == 0 ? 0x80 : 0);
                Write(c, 0x2234, 0);
                Write(c, 0x2238, 2);
                Write(c, 0x2239, 0);
                Write(c, 0x2235, 0x40);
                Write(c, 0x2236, 0);
                if (route.Item2 != 0)
                    Write(c, 0x2237, 0);
                c.Synchronize(100, HardwareProfile.Ntsc);
                byte[] dest = route.Item2 == 0 ? c.Iram : ram;
                Check(dest[0x40] == 0x21 && dest[0x41] == 0x87 && (Read(c, 0x2301) & 0x20) != 0 && c.DmaBytes == 2, "DMA route" + route);
            }
        });
        Test("CC1 timed two buffers literal plane bytes and snapshot phases", () =>
        {
            var c = Chip(out _, out var ram);
            ram[0] = 0x0c;
            ram[16] = 0x03;
            Write(c, 0x2230, 0xb1);
            c.TryWrite(0x2231, 2, 0, HardwareProfile.Ntsc);
            c.TryWrite(0x2232, 0, 0, HardwareProfile.Ntsc);
            c.TryWrite(0x2233, 0, 0, HardwareProfile.Ntsc);
            c.TryWrite(0x2234, 0x40, 0, HardwareProfile.Ntsc);
            c.TryWrite(0x2235, 0, 0, HardwareProfile.Ntsc);
            c.TryWrite(0x2236, 1, 0, HardwareProfile.Ntsc);
            c.Synchronize(2, HardwareProfile.Ntsc);
            c.TryRead(0x2300, 0, 2, HardwareProfile.Ntsc, out byte early);
            Check((early & 0x20) == 0, "CC1 readiness before timed fill");
            var phases = new HashSet<int>();
            var ready = new HashSet<int>();
            for (int t = 4; t <= 220; t += 2)
            {
                c.Synchronize(t, HardwareProfile.Ntsc);
                var envelope = c.Capture(t, HardwareProfile.Ntsc);
                var state = envelope.Payload.Deserialize<Sa1HardwareState>()!;
                phases.Add(state.Conversion1.Phase);
                ready.Add((state.Conversion1.Ready0 ? 1 : 0) | (state.Conversion1.Ready1 ? 2 : 0));
                var clone = Chip(out _, out var cloneRam);
                ram.CopyTo(cloneRam, 0);
                clone.Restore(envelope, t, HardwareProfile.Ntsc);
                clone.Synchronize(t + 1, HardwareProfile.Ntsc);
                c.Synchronize(t + 1, HardwareProfile.Ntsc);
                Check(State(clone) == State(c), "CC1 phase continuation");
            }

            c.TryRead(0x400000, 0, 221, HardwareProfile.Ntsc, out byte a);
            c.TryRead(0x400001, 0, 221, HardwareProfile.Ntsc, out byte b);
            Check(a == 0x40 && b == 0x40 && phases.SetEquals(new[] { 0, 1 }) && ready.Contains(3), "Two timed CC1 buffers and literal planes");
            c.TryRead(0x40000f, 0, 221, HardwareProfile.Ntsc, out _);
            c.Synchronize(400, HardwareProfile.Ntsc);
            c.TryRead(0x400010, 0, 400, HardwareProfile.Ntsc, out byte second);
            Check(second == 0x80, "SecondCC1 tile survives buffer0 reuse");
        });
        Test("late bad SA1 state rejection keeps distinct live filled BW RAM", () =>
        {
            var m = Machine(HardwareProfile.Ntsc);
            m.RunUntil(2000);
            Array.Fill(m.Bus.Cartridge!.Sram, (byte)0x5a);
            byte[] raw = StateSerializer.Capture(m);
            byte[] needle = System.Text.Encoding.UTF8.GetBytes("{\"Version\":1,\"Kind\":\"SA1\"");
            int at = raw.AsSpan().LastIndexOf(needle);
            Check(at >= 0, "Envelope marker");
            raw[at + 11] = (byte)'2';
            Array.Fill(m.Bus.Cartridge.Sram, (byte)0xa5);
            bool rejected = false;
            try
            {
                Load(m.Bus.Cartridge, raw);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Check(rejected && m.Bus.Cartridge.Sram.All(v => v == 0xa5), "Late failure mutated live RAM");
        });
        Test("invalid SA1 configuration and forbidden normal mapper override", () =>
        {
            foreach (int size in new[]
            {
                0,
                1024,
                524288
            }

            )
            {
                bool reject = false;
                try
                {
                    new Sa1Configuration(size).Validate();
                }
                catch (InvalidDataException)
                {
                    reject = true;
                }

                Check(reject, "Invalid board size");
            }

            bool wrong = false;
            try
            {
                _ = new Cartridge(Sa1DiagnosticRom.Create(), hardware: new SuperFxConfiguration(SuperFxRevision.Gsu2));
            }
            catch (InvalidDataException)
            {
                wrong = true;
            }

            Check(wrong, "Wrong enhanced board type");
        });
        Test("invalid pending CPU DMA conversion timer and clock states reject atomically", () =>
        {
            var c = Chip(out _, out var ram);
            Array.Fill(ram, (byte)0xa5);
            c.Iram[123] = 0x5a;
            var envelope = c.Capture(0, HardwareProfile.Ntsc);
            var state = envelope.Payload.Deserialize<Sa1HardwareState>()!;
            Func<Sa1HardwareState, Sa1HardwareState>[] mutations = [s => s with
            {
                Version = 2
            }, s => s with
            {
                Clock = 1
            }, s => s with
            {
                Iram = null!
            }, s => s with
            {
                RegistersIo = new byte[1]
            }, s => s with
            {
                CycleRemaining = 5
            }, s => s with
            {
                CycleRemaining = 1,
                PendingCycle = null
            }, s => s with
            {
                ResetAsserted = false
            }, s => s with
            {
                Hold = true
            }, s => s with
            {
                BitOffset = 8
            }, s => s with
            {
                BitAddress = -1
            }, s => s with
            {
                TimerSubclock = 4
            }, s => s with
            {
                MathDeadline = 100
            }, s => s with
            {
                MathResult = -1
            }, s => s with
            {
                HostBusyUntil = 13
            }, s => s with
            {
                CpuFlags = 1
            }, s => s with
            {
                Sa1Flags = 1
            }, s => s with
            {
                Dma = null!
            }, s => s with
            {
                Dma = s.Dma with
                {
                    Phase = 2
                }
            }, s => s with
            {
                Dma = s.Dma with
                {
                    Mode = 3
                }
            }, s => s with
            {
                Dma = s.Dma with
                {
                    Active = true,
                    Remaining = 1,
                    Wait = 0
                }
            }, s => s with
            {
                Dma = s.Dma with
                {
                    Conversion = null!
                }
            }, s => s with
            {
                Dma = s.Dma with
                {
                    Source = -1
                }
            }, s => s with
            {
                Dma = s.Dma with
                {
                    Active = true,
                    Mode = 2,
                    Remaining = 2,
                    Wait = 1,
                    ConversionByte = 8
                }
            }, s => s with
            {
                Conversion1 = null!
            }, s => s with
            {
                Conversion1 = s.Conversion1 with
                {
                    Ready0 = true,
                    Tile0 = -1
                }
            }, s => s with
            {
                Conversion1 = s.Conversion1 with
                {
                    Active = true,
                    FillTile = 0,
                    Wait = 0
                }
            }, s => s with
            {
                Conversion1 = s.Conversion1 with
                {
                    Active = true,
                    FillTile = -1,
                    Wait = 0
                }
            }, s => s with
            {
                Conversion1 = s.Conversion1 with
                {
                    Bpp = 3
                }
            }, s => s with
            {
                Conversion1 = s.Conversion1 with
                {
                    FillTile = int.MaxValue
                }
            }

            ];
            foreach (var mutate in mutations)
            {
                bool rejected = false;
                try
                {
                    c.Restore(envelope with { Payload = JsonSerializer.SerializeToElement(mutate(state)) }, 0, HardwareProfile.Ntsc);
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }

                Check(rejected && c.Iram[123] == 0x5a && ram.All(x => x == 0xa5), "Malformed state accepted or mutated live memory");
            }

            c.Restore(envelope, 0, HardwareProfile.Ntsc);
            report.Metadata["invalidStateMutations"] = mutations.Length;
        });
        Test("real SA1 WAI IRQ and NMI vector entry and acknowledgement", () =>
        {
            foreach (bool nmi in new[]
            {
                false,
                true
            }

            )
            {
                var c = Chip(out var rom, out _);
                new byte[]
                {
                    0x58,
                    0xcb,
                    0xa9,
                    0x5a,
                    0x85,
                    0,
                    0xdb
                }.CopyTo(rom, 0);
                new byte[]
                {
                    0xa9,
                    0xa7,
                    0x85,
                    1,
                    0xa9,
                    (byte)(nmi ? 0x10 : 0x80),
                    0x8d,
                    0x0b,
                    0x22,
                    0x40
                }.CopyTo(rom, 0x1000);
                Write(c, 0x222a, 255);
                Write(c, 0x220a, nmi ? 0x10 : 0x80);
                c.TryWrite(0x2203, 0, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2204, 0x80, 0, HardwareProfile.Ntsc);
                c.TryWrite(nmi ? 0x2205 : 0x2207, 0, 0, HardwareProfile.Ntsc);
                c.TryWrite(nmi ? 0x2206 : 0x2208, 0x90, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2200, 0, 0, HardwareProfile.Ntsc);
                c.Synchronize(100, HardwareProfile.Ntsc);
                Check(c.Processor.Waiting, "Real WAI entry");
                c.TryWrite(0x2200, (byte)(nmi ? 0x10 : 0x80), 100, HardwareProfile.Ntsc);
                c.Synchronize(400, HardwareProfile.Ntsc);
                Check(c.Processor.Stopped && c.Iram[0] == 0x5a && c.Iram[1] == 0xa7 && (Read(c, 0x2301) & (nmi ? 0x10 : 0x80)) == 0, "Real vector handler/RTI/wake/ack");
            }
        });
        Test("hold and reset preserve pending phase without hidden instruction overshoot", () =>
        {
            var c = Chip(out var rom, out _);
            rom[0] = 0xea;
            rom[1] = 0xdb;
            c.TryWrite(0x2203, 0, 0, HardwareProfile.Ntsc);
            c.TryWrite(0x2204, 0x80, 0, HardwareProfile.Ntsc);
            c.TryWrite(0x2200, 0, 0, HardwareProfile.Ntsc);
            c.Synchronize(1, HardwareProfile.Ntsc);
            var before = c.Capture(1, HardwareProfile.Ntsc).Payload.Deserialize<Sa1HardwareState>()!;
            Check(before.PendingCycle != null && before.CycleRemaining == 1, "Odd MC reset bus phase");
            c.TryWrite(0x2200, 0x40, 1, HardwareProfile.Ntsc);
            c.Synchronize(101, HardwareProfile.Ntsc);
            var held = c.Capture(101, HardwareProfile.Ntsc).Payload.Deserialize<Sa1HardwareState>()!;
            Check(held.Registers == before.Registers && held.CycleRemaining == 1, "Hold advanced processor");
            c.TryWrite(0x2200, 0, 101, HardwareProfile.Ntsc);
            c.Synchronize(200, HardwareProfile.Ntsc);
            Check(c.Processor.Stopped, "Release did not resume pending bus");
        });
        Test("normal DMA every1MC pending read write and arbitration continuation", () =>
        {
            var seen = new HashSet<int>();
            bool latch = false, blocked = false;
            foreach (int route in new[]
            {
                0,
                4,
                1,
                6
            }

            )
                for (int cut = 0; cut <= 25; cut++)
                {
                    var c = Chip(out var rom, out var ram);
                    rom[0] = 0xa7;
                    rom[1] = 0x5a;
                    ram[0] = 0xa7;
                    ram[1] = 0x5a;
                    c.Iram[0] = 0xa7;
                    c.Iram[1] = 0x5a;
                    Write(c, 0x2230, 0xc0 | route);
                    Write(c, 0x2232, 0);
                    Write(c, 0x2233, (route & 3) == 0 ? 0x80 : 0);
                    Write(c, 0x2234, 0);
                    Write(c, 0x2238, 2);
                    Write(c, 0x2239, 0);
                    Write(c, 0x2235, 0x40);
                    Write(c, 0x2236, 0);
                    if ((route & 4) != 0)
                        Write(c, 0x2237, 0);
                    c.BeginHostDma((route & 3) == 0 ? 0x008000 : (route & 3) == 1 ? 0x400000 : 0x3000, 0, 8);
                    c.Synchronize(cut, HardwareProfile.Ntsc);
                    var envelope = c.Capture(cut, HardwareProfile.Ntsc);
                    var state = envelope.Payload.Deserialize<Sa1HardwareState>()!;
                    if (state.Dma.Active)
                        seen.Add(state.Dma.Phase);
                    latch |= state.Dma.Latched != 0;
                    blocked |= state.Clock < state.HostBusyUntil && state.Dma.Remaining == 2;
                    var clone = Chip(out var cloneRom, out var cloneRam);
                    rom.CopyTo(cloneRom, 0);
                    ram.CopyTo(cloneRam, 0);
                    clone.Restore(envelope, cut, HardwareProfile.Ntsc);
                    clone.Synchronize(cut + 60, HardwareProfile.Ntsc);
                    c.Synchronize(cut + 60, HardwareProfile.Ntsc);
                    Check(State(c) == State(clone) && ram.SequenceEqual(cloneRam), "DMA arbitrary pending phase replay");
                }

            Check(seen.SetEquals(new[] { 0, 1 }) && latch && blocked, "Nonvacuous DMA read/write/latch/host grant coverage");
            report.Metadata["normalDmaContinuations"] = new
            {
                count = 104,
                phases = seen.Order().ToArray(),
                latch,
                blocked
            };
        });
        Test("CC1 small BW RAM source aliases and low6000 host window", () =>
        {
            foreach (int bytes in new[]
            {
                32768,
                65536
            }

            )
            {
                byte[] rom = new byte[32768], ram = new byte[bytes];
                ram[0] = 0x0c;
                var c = new Sa1CartridgeHardware(new(bytes), rom, ram);
                Write(c, 0x2230, 0xb1);
                c.TryWrite(0x2231, 2, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2232, 0, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2233, 0, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2234, 0x43, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2235, 0, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2236, 1, 0, HardwareProfile.Ntsc);
                c.Synchronize(200, HardwareProfile.Ntsc);
                c.TryRead(0x430000, 0xd3, 200, HardwareProfile.Ntsc, out byte high);
                c.TryRead(0x6000, 0xd3, 200, HardwareProfile.Ntsc, out byte low);
                Check(high == 0x40 && low == 0x40, "Physical source/window alias on" + bytes);
            }
        });
        Test("SA1 ROM control-flow refill and branch parity literal cycles", () =>
        {
            foreach (var entry in new[]
            {
                (new byte[] { 0x4c, 3, 0x80, 0xdb }, 14),
                (new byte[] { 0x80, 1, 0xea, 0xdb }, 14),
                (new byte[] { 0x80, 2, 0xea, 0xea, 0xdb }, 13)
            }

            )
            {
                var c = Chip(out var rom, out _);
                var trace = new List<CpuCycle>();
                c.Processor.CycleObserved = trace.Add;
                entry.Item1.CopyTo(rom, 0);
                c.TryWrite(0x2203, 0, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2204, 0x80, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2200, 0, 0, HardwareProfile.Ntsc);
                c.Synchronize(100, HardwareProfile.Ntsc);
                Check(c.Processor.Stopped && c.CpuCycles == entry.Item2, "Refill cycle count " + Convert.ToHexString(entry.Item1) + " actual=" + c.CpuCycles + " trace=" + JsonSerializer.Serialize(trace));
            }
        });
        Test("literal ROM4of8 IRAM4of6 BW4of8 grant endpoints and midgrant replay", () =>
        {
            foreach (int resource in new[]
            {
                1,
                2,
                3
            }

            )
            {
                var c = Chip(out var rom, out var ram);
                int pc = resource == 1 ? 0x3000 : resource == 2 ? 0x6000 : 0x8000;
                if (resource == 1)
                {
                    c.Iram[0] = 0xea;
                    c.Iram[1] = 0xdb;
                }
                else if (resource == 2)
                {
                    ram[0] = 0xea;
                    ram[1] = 0xdb;
                }
                else
                {
                    rom[0] = 0xea;
                    rom[1] = 0xdb;
                }

                c.TryWrite(0x2203, (byte)pc, 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2204, (byte)(pc >> 8), 0, HardwareProfile.Ntsc);
                c.TryWrite(0x2200, 0, 0, HardwareProfile.Ntsc);
                c.Synchronize(14, HardwareProfile.Ntsc);
                c.BeginHostCycle(new(pc, null, "-p-r----"), 14, resource == 1 ? 6 : 8);
                c.Synchronize(15, HardwareProfile.Ntsc);
                var envelope = c.Capture(15, HardwareProfile.Ntsc);
                var s = envelope.Payload.Deserialize<Sa1HardwareState>()!;
                Check(s.HostBusyUntil == 18 && s.PendingCycle != null, "Literal4MC grant horizon");
                var clone = Chip(out var cloneRom, out var cloneRam);
                rom.CopyTo(cloneRom, 0);
                ram.CopyTo(cloneRam, 0);
                clone.Restore(envelope, 15, HardwareProfile.Ntsc);
                var reads = new List<long>();
                c.BusObserved = (kind, t, a, v) =>
                {
                    if (kind == "sa1-read" && a == pc)
                        reads.Add(t);
                };
                c.Synchronize(100, HardwareProfile.Ntsc);
                clone.Synchronize(100, HardwareProfile.Ntsc);
                Check(State(c) == State(clone) && reads.Single() == (resource == 2 ? 22 : 20), "Bus endpoint after grant and full continuation");
            }
        });
        Test("actual main DMA Read to Hold releases A bus after4MC", () =>
        {
            var m = Machine(HardwareProfile.Ntsc);
            int bound = 0;
            while (!(m.Bus.Dma.State.Phase == DmaPhase.Read && m.Bus.Dma.State.Remaining == 4) && bound++ < 10000)
                m.RunUntil(m.Clock.Now + 2);
            Check(bound < 10000, "No actual CPU DMA read phase");
            var c = (Sa1CartridgeHardware)m.Bus.Cartridge!.Hardware!;
            var state = c.Capture(m.Clock.Now, m.Profile).Payload.Deserialize<Sa1HardwareState>()!;
            Check(state.HostResource == 1 && state.HostBusyUntil == m.Clock.Now + 4, "DMA A grant start");
            var raw = StateSerializer.Capture(m);
            var clone = Load(m.Bus.Cartridge, raw);
            long until = m.Clock.Now + 4;
            m.RunUntil(until);
            Check(m.Bus.Dma.State.Phase == DmaPhase.Hold, "DMA Read/Hold transition");
            var hold = c.Capture(m.Clock.Now, m.Profile).Payload.Deserialize<Sa1HardwareState>()!;
            Check(hold.HostBusyUntil <= m.Clock.Now, "A bus held across B transfer");
            m.RunUntil(until + 500);
            clone.RunUntil(until + 500);
            Check(StateSerializer.Capture(m).SequenceEqual(StateSerializer.Capture(clone)), "Actual midDMA grant replay");
        });
        Test("held CPU CC1 and normal DMA grant-release partition invariance", () =>
        {
            foreach (bool conversion in new[]
            {
                false,
                true
            }

            )
            {
                var c = Chip(out var rom, out var ram);
                ram[0] = 0x0c;
                rom[0] = 0xa7;
                if (conversion)
                {
                    Write(c, 0x2230, 0xb1);
                    Write(c, 0x2231, 2);
                    Write(c, 0x2234, 0x40);
                    Write(c, 0x2236, 1);
                }
                else
                {
                    Write(c, 0x2230, 0x80);
                    Write(c, 0x2233, 0x80);
                    Write(c, 0x2238, 2);
                    Write(c, 0x2235, 0x40);
                    Write(c, 0x2236, 0);
                }

                c.BeginHostDma(conversion ? 0x400000 : 0x8000, 0, 8);
                c.Synchronize(1, HardwareProfile.Ntsc);
                var envelope = c.Capture(1, HardwareProfile.Ntsc);
                var sliced = Chip(out var slicedRom, out var slicedRam);
                rom.CopyTo(slicedRom, 0);
                ram.CopyTo(slicedRam, 0);
                sliced.Restore(envelope, 1, HardwareProfile.Ntsc);
                c.Synchronize(100, HardwareProfile.Ntsc);
                for (int t = 2; t <= 100; t++)
                    sliced.Synchronize(t, HardwareProfile.Ntsc);
                Check(State(c) == State(sliced) && ram.SequenceEqual(slicedRam), "Host release skipped inside long horizon");
            }
        });
        Test("actual SystemBus SA1 unmapped windows cannot fall back to normal LoROM", () =>
        {
            var m = Machine(HardwareProfile.Ntsc);
            var cart = m.Bus.Cartridge!;
            Array.Fill(cart.Sram, (byte)0x5a);
            cart.Rom[0] = 0xaa;
            foreach (int address in new[]
            {
                0x508000,
                0x608000,
                0x700000,
                0x708000,
                0xB02000
            }

            )
            {
                m.Bus.OpenBus = 0xd3;
                Check(m.Bus.Read(address) == 0xd3, "Unmapped cartridge decode leaked normal mapper at" + address.ToString("X6"));
                m.Bus.Write(address, 0xa7);
            }

            Check(cart.Sram.All(x => x == 0x5a), "Unmapped LoROM SRAM bypassed SA1 BW write protection");
            m.Bus.OpenBus = 0xd3;
            Check(m.Bus.Read(0x400000) == 0x5a, "Legal host BW window");
        });
        Test("signed40-bit cumulative positive negative overflow and explicit clear", () =>
        {
            foreach (bool negative in new[]
            {
                false,
                true
            }

            )
            {
                var c = Chip(out _, out _);
                Write(c, 0x2250, 2);
                var envelope = c.Capture(0, HardwareProfile.Ntsc);
                var state = envelope.Payload.Deserialize<Sa1HardwareState>()!;
                c.Restore(envelope with { Payload = JsonSerializer.SerializeToElement(state with { MathResult = negative ? 0x8000000000L : 0x7fffffffffL }) }, 0, HardwareProfile.Ntsc);
                Write(c, 0x2251, negative ? -1 : 1);
                Write(c, 0x2252, negative ? -1 : 0);
                Write(c, 0x2253, 1);
                Write(c, 0x2254, 0);
                c.Synchronize(12, HardwareProfile.Ntsc);
                long value = 0;
                for (int i = 0; i < 5; i++)
                    value |= (long)Read(c, 0x2306 + i) << (8 * i);
                Check(value == (negative ? 0x7fffffffffL : 0x8000000000L) && Read(c, 0x230b) == 128, "Signed40-bit overflow boundary");
                Write(c, 0x2250, 2);
                Check(Read(c, 0x2306) == 0 && Read(c, 0x230b) == 0, "Explicit accumulate clear");
            }
        });
        Test("CC2 all2/4/8 planes both eight-row tile buffers and896oneMC continuations", () =>
        {
            int count = 0;
            foreach (int mode in new[]
            {
                2,
                1,
                0
            }

            )
            {
                var c = Chip(out var rom, out var ram);
                int bpp = mode == 2 ? 2 : mode == 1 ? 4 : 8;
                Write(c, 0x2230, 0xa0);
                Write(c, 0x2231, mode);
                Write(c, 0x2235, 0);
                Write(c, 0x2236, 1);
                for (int row = 0; row < 16; row++)
                {
                    for (int x = 0; x < 8; x++)
                        Write(c, 0x2240 + (row & 1) * 8 + x, x == row % 8 ? (1 << bpp) - 1 : 0);
                    long end = c.Clock + bpp * 4;
                    while (c.Clock < end)
                    {
                        var envelope = c.Capture(c.Clock, HardwareProfile.Ntsc);
                        var clone = Chip(out var cloneRom, out var cloneRam);
                        rom.CopyTo(cloneRom, 0);
                        ram.CopyTo(cloneRam, 0);
                        clone.Restore(envelope, c.Clock, HardwareProfile.Ntsc);
                        clone.Synchronize(c.Clock + 1, HardwareProfile.Ntsc);
                        c.Synchronize(c.Clock + 1, HardwareProfile.Ntsc);
                        Check(State(c) == State(clone), "CC2 oneMC continuation");
                        count++;
                    }

                    for (int plane = 0; plane < bpp; plane++)
                    {
                        int address = 0x100 + (row / 8) * bpp * 8 + (row % 8) * 2 + (plane / 2) * 16 + (plane & 1);
                        Check(c.Iram[address] == (1 << (7 - row % 8)), "Literal plane and alternating tile buffer");
                    }
                }
            }

            Check(count == 896, "CC2 continuation inventory");
            report.Metadata["cc2OneMcContinuations"] = count;
        });
        foreach (var p in new[]
        {
            HardwareProfile.Ntsc,
            HardwareProfile.PalProfile
        }

        )
        {
            foreach (bool cc1 in new[]
            {
                false,
                true
            }

            )
                Test(p.Id + (cc1 ? " CC1" : " CC2") + " dualCPU math DMA nativepixel", () =>
                {
                    var m = Machine(p, cc1);
                    var trace = new List<SNESEmulator.Core.BusAccess>();
                    m.Bus.AccessOriginObserved = trace.Add;
                    for (int i = 0; i < 4; i++)
                        m.RunFrame();
                    var c = (Sa1CartridgeHardware)m.Bus.Cartridge!.Hardware!;
                    Check(m.Cpu.Stopped && c.Processor.Stopped && m.Bus.Wram[0] == 63 && m.Bus.Wram[0x10] == 0xc7 && m.Bus.Wram[0x12] == 0xd8 && c.Iram[0] == 0xa5, "Both real processors must finish math handshake");
                    Check(c.Iram[0x100] == 0x40 && c.Iram[0x101] == 0x40, "PlanarCC2 literal row " + Convert.ToHexString(c.Iram.AsSpan(0x100, 32)));
                    Check(m.Bus.Ppu.Vram[0x100] == 0x40 && m.Bus.Ppu.Vram[0x101] == 0x40, "CPU DMA->PPU actual row");
                    Check(trace.Any(x => x.Origin == AccessOrigin.DmaA && x.Address == (cc1 ? 0x400000 : 0x3100) && x.Value == 0x40) && trace.Any(x => x.Origin == AccessOrigin.DmaB && x.Address == 0x2119 && x.Value == 0x40), "Actual DMA bus origins");
                    Check(m.Bus.Ppu.NativePixels.Count(x => x == 0x7fff) == 1, "Exactly one white native pixel");
                });
            Test(p.Id + " every2MC machine snapshot continuation", () =>
            {
                var m = Machine(p);
                var cuts = new List<long>();
                for (long t = 2; t <= 2400; t += 2)
                    cuts.Add(t);
                var phases = new HashSet<string>();
                bool math = false;
                bool contention = false;
                foreach (long t in cuts)
                {
                    m = Machine(p);
                    m.RunUntil(t);
                    var c = (Sa1CartridgeHardware)m.Bus.Cartridge!.Hardware!;
                    var chip = c.Capture(t, p).Payload.Deserialize<Sa1HardwareState>()!;
                    if (chip.PendingCycle is { } pending)
                        phases.Add(pending.Valid ? (pending.Read ? "cpu-read" : "cpu-write") : "cpu-idle");
                    if (chip.Dma.Active && chip.Dma.Mode == 2)
                        phases.Add("CC2-" + chip.Dma.Phase);
                    math |= chip.MathDeadline >= 0;
                    contention |= chip.HostBusyUntil > t && chip.HostResource != 0;
                    byte[] bytes = StateSerializer.Capture(m);
                    var copy = Load(m.Bus.Cartridge, bytes);
                    copy.RunUntil(t + 100);
                    m.RunUntil(t + 100);
                    Check(StateSerializer.Capture(copy).SequenceEqual(StateSerializer.Capture(m)), "Continuation at" + t);
                }

                Check(phases.Contains("cpu-read") && phases.Contains("cpu-write") && phases.Contains("cpu-idle") && phases.Contains("CC2-0") && phases.Contains("CC2-1") && math && contention, "Vacuous pending phase coverage: " + string.Join(',', phases));
                report.Metadata[p.Pal ? "palContinuations" : "ntscContinuations"] = new
                {
                    count = cuts.Count,
                    phases = phases.Order().ToArray(),
                    math,
                    contention
                };
            });
        }

        report.Metadata["scope"] = "Own two-65816 digital model and authored ROM, not physical arbitration or commercial compatibility certification.";
        report.Metadata["coreAssemblySha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(SnesSystem).Assembly.Location)));
        report.Save(args.FirstOrDefault() ?? "artifacts/m10c-sa1/functional");
        return report.ExitCode;
    }
}
