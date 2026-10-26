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
// Models the SA-1 CPU, mapping, DMA/conversion, timers and math registers.
// Board timing limitations remain documented separately from functional coverage.
//

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SNESEmulator.Core;
/// <summary>SA-1 board memory capacity. Revision 0 is a selected digital model, not a hardware certificate.</summary>
public sealed record Sa1Configuration(int RamBytes = 131072, int Revision = 0) : ICartridgeConfiguration
{
    public void Validate()
    {
        if (Revision != 0 || RamBytes is not (32768 or 65536 or 131072 or 262144))
            throw new InvalidDataException("Unsupported SA-1 board revision or BW-RAM capacity.");
    }

    internal static Sa1Configuration Resolve(string source)
    {
        string path = source + ".hardware.json";
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(path))
            throw new NotSupportedException("SA-1 requires an explicit <ROM path>.hardware.json with RamBytes and Revision:0; board timing remains under validation.");
        try
        {
            byte[] bytes = AtomicFile.ReadBytes(path, 16384);
            using var doc = JsonDocument.Parse(bytes);
            foreach (string key in new[]
            {
                "RamBytes",
                "Revision"
            }

            )
                if (!doc.RootElement.TryGetProperty(key, out _))
                    throw new InvalidDataException("Missing SA-1 board field: " + key);
            var c = JsonSerializer.Deserialize<Sa1Configuration>(bytes, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Missing SA-1 configuration.");
            c.Validate();
            return c;
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Invalid SA-1 configuration.", e);
        }
    }
}

public sealed record Sa1DmaState(bool Active, int Phase, int Source, int Destination, int Remaining, byte Latched, int Wait, int Mode, int ConversionRow, int ConversionByte, byte[] Conversion, int Control = 0, int Bpp = 2);
public sealed record Sa1ConversionState(bool Active, int Bpp, int Width, int Source, int Destination, int FillTile, int FillBuffer, int Row, int Byte, int Phase, int Wait, ulong Packed, byte[] Buffers, int Tile0, int Tile1, bool Ready0, bool Ready1);
public sealed record Sa1HardwareState(int Version, Sa1Configuration Configuration, long Clock, CpuRegisters Registers, CpuContinuation? Continuation, CpuInterruptState Interrupts, CpuCycle? PendingCycle, int CycleRemaining, byte[] Iram, byte[] RegistersIo, byte OpenBus, bool ResetAsserted, bool Hold, int Sa1Flags, int CpuFlags, int TimerH, int TimerV, int TimerSubclock, int LatchedH, int LatchedV, long MathDeadline, long MathResult, long PendingMath, bool Overflow, bool PendingOverflow, int BitAddress, int BitOffset, Sa1DmaState Dma, int HostResource, long HostBusyUntil, long CpuCycles, long DmaBytes, int ConversionLine, Sa1ConversionState Conversion1);
/// <summary>
/// Two independently restartable 65816 processors share the board's ROM, BW-RAM and I-RAM.
/// All externally visible effects occur at a saved bus endpoint; a host budget never runs a whole
/// SA-1 instruction beyond its horizon. The selected contention approximation is documented separately.
/// </summary>
public sealed class Sa1CartridgeHardware : ICartridgeHardware, ICpuBus
{
    public string Kind => "SA1";
    public Cpu65816 Processor { get; }
    public byte[] Iram { get; } = new byte[2048];

    private readonly byte[] rom, ram;
    private readonly Sa1Configuration config;
    private readonly byte[] io = new byte[0x100];
    private byte openBus;
    private long clock, target, mathDeadline = -1, mathResult, pendingMath;
    private bool overflow, pendingOverflow, resetAsserted = true, hold;
    private int sa1Flags, cpuFlags, timerH, timerV, timerSubclock, latchedH, latchedV, bitAddress, bitOffset;
    private CpuCycle? pendingCycle;
    private int cycleRemaining, hostResource;
    private long hostBusyUntil;
    private bool synchronizing;
    private HardwareProfile profile = HardwareProfile.Ntsc;
    private long cpuCycles, dmaBytes;
    private int conversionLine;
    private Sa1ConversionState cc = new(false, 2, 1, 0, 0, -1, 0, 0, 0, 0, 0, 0, new byte[128], -1, -1, false, false);
    private Sa1DmaState dma = new(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, new byte[64]);
    private sealed class Budget : Exception;
    private readonly Budget budget = new();
    public Action<bool>? IrqChanged { get; set; }
    public Action<string, long, int, byte>? BusObserved { get; set; }
    public bool IrqOutput => (cpuFlags & io[1] & 0xa0) != 0;
    public long Clock => clock;
    public long CpuCycles => cpuCycles;
    public long DmaBytes => dmaBytes;
    public bool Running => !resetAsserted && !hold && !Processor.Stopped;

    public Sa1CartridgeHardware(Sa1Configuration configuration, byte[] rom, byte[] ram)
    {
        configuration.Validate();
        if (ram.Length != configuration.RamBytes || rom.Length is < 32768 or > 8388608)
            throw new InvalidDataException("SA-1 memory does not match its board configuration.");
        config = configuration;
        this.rom = rom;
        this.ram = ram;
        Processor = new(this)
        {
            Sa1RomPrefetchTiming = true
        };
        Processor.BeforeCycle = BeforeCycle;
        Processor.AfterCycle = AfterCycle;
        Reset(true);
    }

    public bool Decodes(int address, out bool status)
    {
        status = false;
        return false;
    }

    public byte Read(bool status, long masterClock, HardwareProfile p) => throw new NotSupportedException("SA-1 uses address-aware access.");
    public void Write(bool status, byte value, long masterClock, HardwareProfile p) => throw new NotSupportedException("SA-1 uses address-aware access.");
    private static int Resource(int address, bool sa1)
    {
        int bank = (address >> 16) & 255, a = address & 65535;
        if ((bank & 0x40) == 0)
        {
            if (sa1 && a < 0x800 || a is >= 0x3000 and < 0x3800)
                return 1;
            if (a is >= 0x2200 and <= 0x23ff)
                return 4;
            if (a is >= 0x6000 and < 0x8000)
                return 2;
            if (a >= 0x8000)
                return 3;
        }

        if ((bank is >= 0x40 and <= 0x4f || sa1 && bank is >= 0x50 and <= 0x5f) || sa1 && bank is >= 0x60 and <= 0x6f)
            return 2;
        return bank >= 0xc0 ? 3 : 0;
    }

    private int RomOffset(int address)
    {
        int bank = address >> 16, a = address & 65535, slot;
        if (bank >= 0xc0)
        {
            slot = (bank - 0xc0) >> 4;
            return Cartridge.Mirror((io[0x20 + slot] & 7) * 0x100000 + (address & 0xfffff), rom.Length);
        }

        slot = ((bank & 0x80) >> 6) | ((bank & 0x20) >> 5);
        int block = (io[0x20 + slot] & 0x80) != 0 ? io[0x20 + slot] & 7 : slot;
        return Cartridge.Mirror(block * 0x100000 + ((bank & 31) << 15) + (a & 32767), rom.Length);
    }

    private int BwAddress(int address, bool sa1, out bool bitmap)
    {
        int bank = address >> 16, a = address & 65535;
        bitmap = sa1 && (bank is >= 0x60 and <= 0x6f || (bank & 0x40) == 0 && (io[0x25] & 0x80) != 0);
        return (bank & 0x40) == 0 ? ((io[sa1 ? 0x25 : 0x24] & (bitmap ? 127 : 31)) << 13) | (a & 8191) : address & (bitmap ? 0xfffff : 0xfffff);
    }

    private byte ReadMemory(int address, bool sa1, byte hostBus = 0)
    {
        int resource = Resource(address, sa1), a = address & 65535;
        if (resource == 4)
            return ReadIo(a, sa1, hostBus);
        if (resource == 1)
            return Iram[a & 2047];
        if (resource == 2)
        {
            int offset = BwAddress(address, sa1, out bool bitmap);
            if (bitmap)
            {
                int bpp = (io[0x3f] & 0x80) != 0 ? 2 : 4;
                return (byte)((ram[Cartridge.Mirror(offset / (8 / bpp), ram.Length)] >> ((offset % (8 / bpp)) * bpp)) & ((1 << bpp) - 1));
            }

            return ram[Cartridge.Mirror(offset, ram.Length)];
        }

        if (resource == 3)
        {
            if ((address & 0xffffe0) == 0xffe0)
            {
                if (sa1)
                {
                    int vector = a switch
                    {
                        0xfffc or 0xfffd => 3,
                        0xffea or 0xffeb or 0xfffa or 0xfffb => 5,
                        0xffee or 0xffef or 0xfffe or 0xffff => 7,
                        _ => -1
                    };
                    if (vector >= 0)
                        return io[vector + (a & 1)];
                }
                else
                {
                    if (a is 0xffea or 0xffeb or 0xfffa or 0xfffb && (io[9] & 0x10) != 0)
                        return io[0x0c + (a & 1)];
                    if (a is 0xffee or 0xffef or 0xfffe or 0xffff && (io[9] & 0x40) != 0)
                        return io[0x0e + (a & 1)];
                }
            }

            return rom[RomOffset(address)];
        }

        return sa1 ? openBus : hostBus;
    }

    private void WriteMemory(int address, byte value, bool sa1)
    {
        int resource = Resource(address, sa1), a = address & 65535;
        if (resource == 4)
        {
            WriteIo(a, value, sa1);
            return;
        }

        if (resource == 1)
        {
            if ((io[sa1 ? 0x2a : 0x29] & (1 << ((a & 2047) >> 8))) != 0)
                Iram[a & 2047] = value;
            return;
        }

        if (resource != 2)
            return;
        int offset = BwAddress(address, sa1, out bool bitmap), bits = (io[0x3f] & 128) != 0 ? 2 : 4;
        int byteOffset = bitmap ? offset / (8 / bits) : offset;
        // BWPA is a prefix, not a mask of pages. The explicit write-enable
        // overrides that protected prefix; addresses beyond it remain writable.
        if ((io[sa1 ? 0x27 : 0x26] & 128) == 0 && byteOffset < (0x100 << Math.Min(io[0x28] & 15, 10)))
            return;
        int physical = Cartridge.Mirror(byteOffset, ram.Length);
        if (!bitmap)
            ram[physical] = value;
        else
        {
            int shift = (offset % (8 / bits)) * bits, mask = ((1 << bits) - 1) << shift;
            ram[physical] = (byte)((ram[physical] & ~mask) | ((value << shift) & mask));
        }
    }

    byte ICpuBus.Read(int address)
    {
        byte value = ReadMemory(address, true);
        openBus = value;
        BusObserved?.Invoke("sa1-read", clock, address, value);
        return value;
    }

    void ICpuBus.Write(int address, byte value)
    {
        openBus = value;
        WriteMemory(address, value, true);
        BusObserved?.Invoke("sa1-write", clock, address, value);
    }

    public bool TryRead(int address, byte bus, long masterClock, HardwareProfile p, out byte value)
    {
        value = bus;
        // This MMC board owns the cartridge decode, including its unselected
        // windows. Returning false would accidentally invoke normal LoROM RAM.
        if (Resource(address, false) == 0)
            return true;
        Synchronize(masterClock, p);
        if (cc.Active && Resource(address, false) == 2)
            value = ReadConversion1(address, bus);
        else
            value = ReadMemory(address, false, bus);
        return true;
    }

    public bool TryWrite(int address, byte value, long masterClock, HardwareProfile p)
    {
        if (Resource(address, false) == 0)
            return true;
        Synchronize(masterClock, p);
        WriteMemory(address, value, false);
        return true;
    }

    public int HostAccessClocks(CpuCycle cycle, int normal)
    {
        if (!cycle.Valid || cycle.Address is not { } address)
            return normal;
        // Book II1-2-3: MEMSEL cannot accelerate SA-1 cartridge ROM/BW-RAM.
        return Resource(address, false)is 2 or 3 ? 8 : normal;
    }

    public void BeginHostCycle(CpuCycle cycle, long start, int duration)
    {
        if (!cycle.Valid || cycle.Address is not { } address)
            return;
        int resource = Resource(address, false);
        if (resource is >= 1 and <= 3)
        {
            hostResource = resource;
            hostBusyUntil = start + Math.Min(4, duration);
        }
    }

    public void BeginHostDma(int address, long start, int duration)
    {
        int resource = Resource(address, false);
        if (resource is >= 1 and <= 3)
        {
            hostResource = resource;
            hostBusyUntil = start + Math.Min(4, duration);
        }
    }

    private void BeforeCycle(CpuCycle c)
    {
        // The common core emits an all-low terminal observation after WAI/STP.
        // It is not an oscillator edge or a selected external bus cycle.
        if (c.Pins == "--------")
            return;
        if (pendingCycle == null)
        {
            pendingCycle = c;
            cycleRemaining = !c.Valid ? 2 : Resource(c.Address ?? 0, true) == 2 ? 4 : 2;
        }
        else if (pendingCycle != c)
            throw new InvalidOperationException("SA-1 continuation changed its pending bus pins.");
        int resource = Processor.Sa1PrefetchCycle ? 3 : c.Valid ? Resource(c.Address ?? 0, true) : 0;
        if (resource != 0 && resource == hostResource && clock < hostBusyUntil)
            AdvanceConcurrent(Math.Min(target, hostBusyUntil), 0);
        while (cycleRemaining > 0)
        {
            if (clock >= target)
                throw budget;
            int delta = (int)Math.Min(cycleRemaining, target - clock);
            AdvanceConcurrent(clock + delta, resource);
            cycleRemaining -= delta;
        }
    }

    private void AfterCycle(CpuCycle c)
    {
        if (c.Pins == "--------")
            return;
        pendingCycle = null;
        cycleRemaining = 0;
        cpuCycles++;
    }

    private void UpdateInterrupts()
    {
        bool nmi = (sa1Flags & io[0x0a] & 0x10) != 0, irq = (sa1Flags & io[0x0a] & 0xe0) != 0;
        Processor.SetInterruptLines(nmi, irq);
        IrqChanged?.Invoke(IrqOutput);
    }

    public long NextBoundary(long current, HardwareProfile p)
    {
        long next = long.MaxValue;
        if (Running || dma.Active && dma.Mode != 1 || cc.Active && cc.FillTile >= 0 || (io[0x10] & 3) != 0)
            next = current + 2;
        if (mathDeadline >= 0)
            next = Math.Min(next, mathDeadline);
        return next;
    }

    public void Synchronize(long masterClock, HardwareProfile p)
    {
        if (masterClock < clock)
            throw new InvalidOperationException("SA-1 timeline moved backwards.");
        if (synchronizing)
            return;
        profile = p;
        target = masterClock;
        synchronizing = true;
        try
        {
            while (clock < target)
            {
                UpdateInterrupts();
                if (dma.Active && dma.Mode != 1 && ((dma.Control & 0x40) != 0 || !Running))
                {
                    AdvanceDma();
                    continue;
                }

                if (!Running)
                {
                    AdvanceConcurrent(target, 0);
                    continue;
                }

                try
                {
                    Processor.RunInstruction();
                }
                catch (Budget)
                {
                    break;
                }
            }
        }
        finally
        {
            synchronizing = false;
        }
    }

    private int DmaResource => dma.Phase == 1 ? ((dma.Control & 4) == 0 ? 1 : 2) : (dma.Control & 3) switch
    {
        0 => 3,
        1 => 2,
        2 => 1,
        _ => 0
    };

    private void AdvanceConcurrent(long until, int cpuResource)
    {
        // CPU-priority DMA uses cycles on an independent selected memory bus.
        // A pending byte retains its own phase/latch when either processor wins
        // arbitration; an internal CPU cycle has no selected memory resource.
        while (clock < until)
        {
            bool dmaCandidate = dma.Active && dma.Mode != 1 && (dma.Control & 0x40) == 0 && DmaResource != cpuResource;
            bool dmaBlocked = DmaResource == hostResource && clock < hostBusyUntil;
            bool progress = dmaCandidate && !dmaBlocked;
            int ccResource = cc.Phase == 0 ? 2 : 1;
            bool ccCandidate = cc.Active && cc.FillTile >= 0 && ccResource != cpuResource;
            bool ccBlocked = ccResource == hostResource && clock < hostBusyUntil;
            bool convert = ccCandidate && !ccBlocked;
            long next = until;
            // A grant release is a real eligibility edge even while the CPU is
            // held. Skipping it would make one long run differ from1MC slices.
            if (dmaCandidate && dmaBlocked || ccCandidate && ccBlocked)
                next = Math.Min(next, hostBusyUntil);
            if (progress)
                next = Math.Min(next, clock + dma.Wait);
            if (convert)
                next = Math.Min(next, clock + cc.Wait);
            int n = (int)Math.Min(int.MaxValue, next - clock);
            AdvanceTime(clock + n);
            if (progress)
            {
                dma = dma with
                {
                    Wait = dma.Wait - n
                };
                if (dma.Wait == 0)
                    CommitDma();
            }

            if (convert)
            {
                cc = cc with
                {
                    Wait = cc.Wait - n
                };
                if (cc.Wait == 0)
                    ConversionEndpoint();
            }
        }
    }

    private void AdvanceTime(long until)
    {
        long old = clock;
        clock = until;
        if (mathDeadline >= 0 && clock >= mathDeadline)
        {
            mathResult = pendingMath;
            overflow = pendingOverflow;
            mathDeadline = -1;
        }

        int timerDelta = checked((int)(until - old));
        {
            timerSubclock += timerDelta;
            while (timerSubclock >= 4)
            {
                timerSubclock -= 4;
                timerH++;
                int horizontal = (io[0x10] & 128) != 0 ? 512 : 341;
                if (timerH >= horizontal)
                {
                    timerH = 0;
                    timerV++;
                    if (timerV >= ((io[0x10] & 128) != 0 ? 512 : profile.Lines))
                    {
                        timerV = 0;
                    }
                }

                int h = io[0x12] | (io[0x13] & 1) << 8, v = io[0x14] | (io[0x15] & 1) << 8;
                bool hit = (io[0x10] & 3) switch
                {
                    1 => timerH == h,
                    2 => timerH == 0 && timerV == v,
                    3 => timerH == h && timerV == v,
                    _ => false
                };
                if (hit)
                {
                    sa1Flags |= 0x40;
                    UpdateInterrupts();
                }
            }
        }
    }

    private byte ReadIo(int address, bool sa1, byte hostBus = 0)
    {
        int a = address & 0x1ff;
        if (a == 0x100 && !sa1)
            return (byte)(cpuFlags | (io[9] & 0x5f));
        if (!sa1)
            return hostBus; // Selected S-CPU model: SpeedTest observes open bus at $230E on SHVC-1L8B-10; other variants are unverified.
        if (a == 0x101)
            return (byte)(sa1Flags | (io[0] & 15));
        if (a == 0x102)
        {
            latchedH = timerH;
            latchedV = timerV;
            return (byte)latchedH;
        }

        if (a is >= 0x103 and <= 0x105)
            return (byte)(a == 0x103 ? latchedH >> 8 : latchedV >> ((a - 0x104) * 8));
        if (a is >= 0x106 and <= 0x10a)
            return (byte)(mathResult >> ((a - 0x106) * 8));
        if (a == 0x10b)
            return (byte)(overflow ? 128 : 0);
        if (a is 0x10c or 0x10d)
        {
            int bits = ReadVbr(bitAddress) | ReadVbr((bitAddress + 1) & 0xffffff) << 8 | ReadVbr((bitAddress + 2) & 0xffffff) << 16;
            byte value = (byte)(bits >> (bitOffset + (a == 0x10d ? 8 : 0)));
            if (a == 0x10d && (io[0x58] & 128) != 0)
                AdvanceBits();
            return value;
        }

        return openBus;
    }

    private void AdvanceBits()
    {
        int n = io[0x58] & 15;
        bitOffset += n == 0 ? 16 : n;
        bitAddress = (bitAddress + (bitOffset >> 3)) & 0xffffff;
        bitOffset &= 7;
    }

    private void WriteIo(int address, byte value, bool sa1)
    {
        int a = address & 0x1ff;
        if (a >= 0x100)
            return;
        // Keep the two writers distinct. In particular S-CPU cannot bypass
        // CIWP by writing the SA-1-only enable register through a host alias.
        bool shared = a is >= 0x31 and <= 0x37;
        bool hostOnly = a is 0 or 1 or 2 or >= 3 and <= 8 or >= 0x20 and <= 0x24 or 0x26 or 0x28 or 0x29;
        if (!shared && (sa1 == hostOnly))
            return;
        io[a] = value;
        switch (a)
        {
            case 0:
                hold = (value & 0x40) != 0;
                bool asserted = (value & 0x20) != 0;
                if (resetAsserted && !asserted)
                {
                    Processor.RequestReset();
                    pendingCycle = null;
                    cycleRemaining = 0;
                }

                resetAsserted = asserted;
                if ((value & 0x80) != 0)
                    sa1Flags |= 0x80;
                if ((value & 0x10) != 0)
                    sa1Flags |= 0x10;
                break;
            case 2:
                cpuFlags &= ~(value & 0xa0);
                break;
            case 9:
                if ((value & 128) != 0)
                    cpuFlags |= 128;
                break;
            case 0x0b:
                sa1Flags &= ~(value & 0xf0);
                break;
            case 0x11:
                timerH = timerV = timerSubclock = 0;
                break;
            case 0x30:
                if ((value & 128) == 0)
                {
                    dma = dma with
                    {
                        Active = false
                    };
                    cc = cc with
                    {
                        Active = false,
                        FillTile = -1
                    };
                    conversionLine = 0;
                }

                break;
            case 0x31:
                if ((value & 128) != 0)
                {
                    cc = cc with
                    {
                        Active = false,
                        FillTile = -1
                    };
                    if (dma.Mode == 1)
                        dma = dma with
                        {
                            Active = false
                        };
                }

                break;
            case 0x36 when (io[0x30] & 4) == 0:
                StartDma();
                break;
            case 0x37 when (io[0x30] & 4) != 0:
                StartDma();
                break;
            case 0x47 or 0x4f:
                if ((io[0x30] & 0xb0) == 0xa0)
                    StartConversion2(a == 0x47 ? 0 : 8);
                break;
            case 0x50:
                if ((value & 2) != 0)
                {
                    mathResult = 0;
                    overflow = false;
                }

                break;
            case 0x54:
                StartMath();
                break;
            case 0x58:
                if ((value & 128) == 0)
                    AdvanceBits();
                break;
            case 0x5b:
                bitAddress = io[0x59] | io[0x5a] << 8 | io[0x5b] << 16;
                bitOffset = 0;
                break;
        }

        UpdateInterrupts();
    }

    private void StartMath()
    {
        int a = (short)(io[0x51] | io[0x52] << 8), b = io[0x53] | io[0x54] << 8;
        pendingOverflow = false;
        if ((io[0x50] & 2) != 0)
        {
            long signed = (mathResult << 24) >> 24, result = signed + (long)a * (short)b;
            pendingOverflow = result < -(1L << 39) || result >= 1L << 39;
            pendingMath = result & 0xffffffffffL;
        }
        else if ((io[0x50] & 1) != 0)
        {
            // Remainder is unsigned: use floor division for negative dividends.
            if (b == 0)
                pendingMath = 0;
            else
            {
                int q = a / b, r = a % b;
                if (r < 0)
                {
                    q--;
                    r += b;
                }

                pendingMath = (uint)((ushort)q | r << 16);
            }
        }
        else
            pendingMath = (uint)(a * (short)b);
        // Manufacturer1-7-1: multiplication/division5 and cumulative6 SA-1 clocks.
        // Visibility at this nominal deadline is modeled; analog propagation is not measured.
        mathDeadline = clock + ((io[0x50] & 2) != 0 ? 12 : 10);
    }

    private byte ReadVbr(int address)
    {
        return Resource(address, true) switch
        {
            1 => Iram[address & 2047],
            2 => ram[Cartridge.Mirror(BwAddress(address, true, out _), ram.Length)],
            3 => rom[RomOffset(address)],
            _ => 255
        };
    }

    private int SourceAddress => io[0x32] | io[0x33] << 8 | io[0x34] << 16;
    private int DestinationAddress => io[0x35] | io[0x36] << 8 | io[0x37] << 16;
    private int DmaHalfClocks => (dma.Control & 7) == 0 ? 1 : 2;

    private void StartDma()
    {
        if ((io[0x30] & 128) == 0)
            return;
        int mode = (io[0x30] & 32) == 0 ? 0 : (io[0x30] & 16) != 0 ? 1 : 2;
        int count = io[0x38] | io[0x39] << 8;
        if (mode == 2)
            return; // Conversion2 is triggered by a completed BRF row.
        dma = new(true, 0, SourceAddress, DestinationAddress, count == 0 ? 65536 : count, 0, mode == 0 && (io[0x30] & 7) == 0 ? 1 : 2, mode, 0, 0, new byte[64], io[0x30], ConversionBpp);
        if (mode != 1)
            cc = cc with
            {
                Active = false,
                FillTile = -1
            };
        if (mode == 1)
        {
            int bpp = ConversionBpp, width = 1 << Math.Min((io[0x31] >> 2) & 7, 5);
            cc = new(true, bpp, width, SourceAddress & 0x3ffff, DestinationAddress & 2047, 0, 0, 0, 0, 0, 4, 0, new byte[128], -1, -1, false, false);
        }
    }

    private byte DmaRead(int source)
    {
        int device = dma.Control & 3;
        return device switch
        {
            0 => rom[RomOffset(source)],
            1 => ram[Cartridge.Mirror(source & 0xfffff, ram.Length)],
            2 => Iram[source & 2047],
            _ => openBus
        };
    }

    private void AdvanceDma()
    {
        if (clock >= target)
            throw new InvalidOperationException("DMA called without a clock budget.");
        if (DmaResource == hostResource && clock < hostBusyUntil)
        {
            AdvanceTime(Math.Min(target, hostBusyUntil));
            return;
        }

        int delta = (int)Math.Min(dma.Wait, target - clock);
        AdvanceTime(clock + delta);
        dma = dma with
        {
            Wait = dma.Wait - delta
        };
        if (dma.Wait != 0)
            return;
        CommitDma();
    }

    private void CommitDma()
    {
        if (dma.Phase == 0)
        {
            dma = dma with
            {
                Latched = dma.Mode == 2 ? dma.Conversion[dma.ConversionByte] : DmaRead(dma.Source),
                Phase = 1,
                Wait = dma.Mode == 0 ? DmaHalfClocks : 2
            };
            return;
        }

        int destination = dma.Mode == 2 ? dma.Source + dma.ConversionRow * 2 + (dma.ConversionByte / 2) * 16 + (dma.ConversionByte & 1) : dma.Destination;
        if ((dma.Control & 4) == 0)
            Iram[destination & 2047] = dma.Latched;
        else
            ram[Cartridge.Mirror(destination & 0xfffff, ram.Length)] = dma.Latched;
        BusObserved?.Invoke("sa1-dma-write", clock, destination, dma.Latched);
        dmaBytes++;
        int remaining = dma.Remaining - 1;
        dma = dma with
        {
            Active = remaining != 0,
            Remaining = remaining,
            Source = dma.Mode == 2 ? dma.Source : (dma.Source + 1) & 0xffffff,
            Destination = (destination + 1) & 0xffffff,
            Phase = 0,
            Wait = remaining == 0 ? 0 : dma.Mode == 0 ? DmaHalfClocks : 2,
            ConversionByte = dma.Mode == 2 ? dma.ConversionByte + 1 : 0
        };
        if (remaining == 0)
        {
            sa1Flags |= 0x20;
            UpdateInterrupts();
        }
    }

    private int ConversionBpp => (io[0x31] & 3) switch
    {
        0 => 8,
        1 => 4,
        _ => 2
    };

    private void StartConversion2(int buffer)
    {
        cc = cc with
        {
            Active = false,
            FillTile = -1
        };
        int bpp = ConversionBpp, row = conversionLine;
        conversionLine = (conversionLine + 1) & 15;
        byte[] converted = new byte[64];
        // One BRF byte is one pixel. Plane pairs are separated by16 bytes,
        // while the two planes of a pair occupy adjacent bytes of a row.
        for (int plane = 0; plane < bpp; plane++)
        {
            byte bits = 0;
            for (int x = 0; x < 8; x++)
                bits |= (byte)(((io[0x40 + buffer + x] >> plane) & 1) << (7 - x));
            converted[plane] = bits;
        }

        // The conversion write sequencer uses a plane address for each byte,
        // rather than pretending the BRF stream is an ordinary linear DMA.
        int tileBase = (DestinationAddress & 2047) & ~(bpp * 16 - 1);
        tileBase += (row & 8) * bpp;
        dma = new(true, 0, tileBase, tileBase, bpp, 0, 2, 2, row & 7, 0, converted, io[0x30], bpp);
    }

    private int NextTile(int tile) => tile % (ram.Length / (cc.Bpp * 8));
    private void ConversionEndpoint()
    {
        int bpp = cc.Bpp;
        if (cc.Phase == 0)
        {
            int tile = cc.FillTile, pixel = ((tile / cc.Width) * 8 + cc.Row) * cc.Width * 8 + (tile % cc.Width) * 8;
            int source = cc.Source + pixel * bpp / 8 + cc.Byte;
            byte value = ram[Cartridge.Mirror(source, ram.Length)];
            ulong packed = cc.Packed | ((ulong)value << (cc.Byte * 8));
            int n = cc.Byte + 1;
            cc = cc with
            {
                Packed = packed,
                Byte = n == bpp ? 0 : n,
                Phase = n == bpp ? 1 : 0,
                Wait = n == bpp ? 2 : 4
            };
            BusObserved?.Invoke("sa1-cc1-read", clock, source, value);
        }
        else
        {
            int plane = cc.Byte;
            byte value = 0;
            for (int x = 0; x < 8; x++)
                value |= (byte)(((cc.Packed >> (x * bpp + plane)) & 1) << (7 - x));
            int within = cc.Row * 2 + (plane / 2) * 16 + (plane & 1), at = cc.FillBuffer * 64 + within;
            cc.Buffers[at] = value;
            Iram[(cc.Destination + cc.FillBuffer * bpp * 8 + within) & 2047] = value;
            BusObserved?.Invoke("sa1-cc1-write", clock, (cc.Destination + cc.FillBuffer * bpp * 8 + within) & 2047, value);
            if (plane + 1 < bpp)
            {
                cc = cc with
                {
                    Byte = plane + 1,
                    Wait = 2
                };
                return;
            }

            if (cc.Row < 7)
            {
                cc = cc with
                {
                    Row = cc.Row + 1,
                    Byte = 0,
                    Phase = 0,
                    Packed = 0,
                    Wait = 4
                };
                return;
            }

            bool first = cc.FillTile == 0;
            cc = cc.FillBuffer == 0 ? cc with
            {
                Ready0 = true,
                Tile0 = cc.FillTile
            }

            : cc with
            {
                Ready1 = true,
                Tile1 = cc.FillTile
            };
            int nextBuffer = cc.FillBuffer ^ 1;
            bool free = nextBuffer == 0 ? !cc.Ready0 : !cc.Ready1;
            cc = cc with
            {
                FillTile = free ? NextTile(cc.FillTile + 1) : -1,
                FillBuffer = nextBuffer,
                Row = 0,
                Byte = 0,
                Phase = 0,
                Packed = 0,
                Wait = free ? 4 : 0
            };
            if (first)
            {
                cpuFlags |= 0x20;
                UpdateInterrupts();
            }
        }
    }

    private byte ReadConversion1(int address, byte hostBus)
    {
        int physical = Cartridge.Mirror(BwAddress(address, false, out _), ram.Length);
        int relative = (physical - Cartridge.Mirror(cc.Source, ram.Length) + ram.Length) % ram.Length, tile = relative / (cc.Bpp * 8), within = relative % (cc.Bpp * 8);
        int buffer = cc.Ready0 && cc.Tile0 == tile ? 0 : cc.Ready1 && cc.Tile1 == tile ? 1 : -1;
        // Reads before the standby interrupt retain S-CPU open bus. No hidden
        // synchronous fill can advance this processor beyond the host horizon.
        if (buffer < 0)
            return hostBus;
        byte result = cc.Buffers[buffer * 64 + within];
        if (within == cc.Bpp * 8 - 1)
        {
            cc = buffer == 0 ? cc with
            {
                Ready0 = false
            }

            : cc with
            {
                Ready1 = false
            };
            if (cc.FillTile < 0)
                cc = cc with
                {
                    FillTile = NextTile(tile + 2),
                    FillBuffer = buffer,
                    Row = 0,
                    Byte = 0,
                    Phase = 0,
                    Packed = 0,
                    Wait = 4
                };
        }

        return result;
    }

    public void Reset(bool powerOn)
    {
        if (powerOn)
            Array.Clear(Iram);
        Array.Clear(io);
        io[0] = 0x20;
        io[0x20] = 0;
        io[0x21] = 1;
        io[0x22] = 2;
        io[0x23] = 3;
        io[0x28] = 0xff;
        resetAsserted = true;
        hold = false;
        clock = target = cpuCycles = dmaBytes = 0;
        Processor.RequestReset();
        pendingCycle = null;
        cycleRemaining = 0;
        openBus = 0;
        conversionLine = 0;
        sa1Flags = cpuFlags = timerH = timerV = timerSubclock = latchedH = latchedV = bitAddress = bitOffset = hostResource = 0;
        hostBusyUntil = 0;
        mathDeadline = -1;
        mathResult = pendingMath = 0;
        overflow = pendingOverflow = false;
        dma = new(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, new byte[64]);
        cc = new(false, 2, 1, 0, 0, -1, 0, 0, 0, 0, 0, 0, new byte[128], -1, -1, false, false);
        UpdateInterrupts();
    }

    public CartridgeHardwareState Capture(long masterClock, HardwareProfile p)
    {
        Synchronize(masterClock, p);
        return new(1, Kind, JsonSerializer.SerializeToElement(new Sa1HardwareState(1, config, clock, Processor.Registers, Processor.CaptureContinuation(), Processor.CaptureInterrupts(), pendingCycle, cycleRemaining, Iram.ToArray(), io.ToArray(), openBus, resetAsserted, hold, sa1Flags, cpuFlags, timerH, timerV, timerSubclock, latchedH, latchedV, mathDeadline, mathResult, pendingMath, overflow, pendingOverflow, bitAddress, bitOffset, dma with { Conversion = dma.Conversion.ToArray() }, hostResource, hostBusyUntil, cpuCycles, dmaBytes, conversionLine, cc with { Buffers = cc.Buffers.ToArray() })));
    }

    public void Restore(CartridgeHardwareState envelope, long masterClock, HardwareProfile p)
    {
        if (envelope.Version != 1 || envelope.Kind != Kind)
            throw new InvalidDataException("Invalid SA-1 envelope.");
        var s = envelope.Payload.Deserialize<Sa1HardwareState>() ?? throw new InvalidDataException("Missing SA-1 state.");
        if (s.Version != 1 || s.Configuration != config || s.Clock != masterClock || s.Clock < 0 || s.Registers == null || s.Interrupts == null || s.Iram?.Length != 2048 || s.RegistersIo?.Length != 256)
            throw new InvalidDataException("Invalid SA-1 state identity or memory capacity.");
        // Validate each domain before copying even the first I-RAM byte. The
        // outer serializer provides the same isolation for nonempty BW-RAM.
        if (s.CycleRemaining is < 0 or > 4 || s.PendingCycle == null && s.CycleRemaining != 0 || s.PendingCycle != null && s.Continuation == null || s.Registers.Instructions < 0 || !Enum.IsDefined(s.Interrupts.Entry) || s.Continuation != null && s.Registers != s.Continuation.Initial)
            throw new InvalidDataException("Invalid SA-1 CPU continuation phase.");
        if (s.ResetAsserted != ((s.RegistersIo[0] & 0x20) != 0) || s.Hold != ((s.RegistersIo[0] & 0x40) != 0) || (s.Sa1Flags & ~0xf0) != 0 || (s.CpuFlags & ~0xa0) != 0 || s.Interrupts.NmiLine != ((s.Sa1Flags & s.RegistersIo[0x0a] & 0x10) != 0) || s.Interrupts.IrqLine != ((s.Sa1Flags & s.RegistersIo[0x0a] & 0xe0) != 0))
            throw new InvalidDataException("Inconsistent SA-1 reset, hold or interrupt output.");
        if (s.TimerH is < 0 or > 511 || s.TimerV is < 0 or > 511 || s.TimerSubclock is < 0 or > 3 || s.LatchedH is < 0 or > 511 || s.LatchedV is < 0 or > 511 || s.BitAddress is < 0 or > 0xffffff || s.BitOffset is < 0 or > 7 || s.MathResult is < 0 or > 0xffffffffffL || s.PendingMath is < 0 or > 0xffffffffffL || s.MathDeadline < -1 || s.MathDeadline >= 0 && (s.MathDeadline < s.Clock || s.MathDeadline - s.Clock > 12))
            throw new InvalidDataException("Invalid SA-1 timer, bit reader or arithmetic deadline.");
        if (s.HostResource is < 0 or > 3 || s.HostBusyUntil < 0 || s.HostBusyUntil > s.Clock && s.HostBusyUntil - s.Clock > 4 || s.CpuCycles < 0 || s.DmaBytes < 0)
            throw new InvalidDataException("Invalid SA-1 shared bus reservation.");
        var transfer = s.Dma;
        if (transfer == null || transfer.Control is < 0 or > 255 || transfer.Bpp is not (2 or 4 or 8) || transfer.Conversion?.Length != 64 || transfer.Phase is < 0 or > 1 || transfer.Mode is < 0 or > 2 || transfer.Wait is < 0 or > 2 || transfer.Remaining is < 0 or > 65536 || transfer.Source is < 0 or > 0xffffff || transfer.Destination is < 0 or > 0xffffff || transfer.ConversionRow is < 0 or > 7 || transfer.ConversionByte is < 0 or > 8 || s.ConversionLine is < 0 or > 15)
            throw new InvalidDataException("Invalid SA-1 DMA register or buffer domain.");
        if (transfer.Active && (transfer.Remaining == 0 || transfer.Mode != 1 && transfer.Wait == 0) || transfer.Active && transfer.Mode == 2 && (transfer.ConversionByte >= transfer.Bpp || transfer.Remaining + transfer.ConversionByte != transfer.Bpp))
            throw new InvalidDataException("Inconsistent SA-1 active DMA phase.");
        var conversion = s.Conversion1;
        if (conversion == null || conversion.Buffers?.Length != 128 || conversion.Bpp is not (2 or 4 or 8) || conversion.Width is not (1 or 2 or 4 or 8 or 16 or 32) || conversion.Source is < 0 or > 0x3ffff || conversion.Destination is < 0 or > 2047 || conversion.FillBuffer is < 0 or > 1 || conversion.Phase is < 0 or > 1 || conversion.Row is < 0 or > 7 || conversion.Byte < 0 || conversion.Byte >= conversion.Bpp || conversion.Wait is < 0 or > 4)
            throw new InvalidDataException("Invalid SA-1 character conversion buffer domain.");
        int tiles = ram.Length / (conversion.Bpp * 8);
        if (conversion.FillTile < -1 || conversion.FillTile >= tiles || conversion.Tile0 < -1 || conversion.Tile0 >= tiles || conversion.Tile1 < -1 || conversion.Tile1 >= tiles || conversion.Ready0 && conversion.Tile0 < 0 || conversion.Ready1 && conversion.Tile1 < 0 || conversion.Ready0 && conversion.Ready1 && conversion.Tile0 == conversion.Tile1 || conversion.Active && (transfer.Mode != 1 || !transfer.Active) || conversion.Active && conversion.FillTile < 0 && (!conversion.Ready0 || !conversion.Ready1 || conversion.Wait != 0) || conversion.Active && conversion.FillTile >= 0 && (conversion.Wait == 0 || (conversion.FillBuffer == 0 ? conversion.Ready0 : conversion.Ready1)))
            throw new InvalidDataException("Inconsistent SA-1 character conversion ready/fill phase.");
        // Validate the processor journal on an isolated bus before installing any
        // memory. StateSerializer also performs this on an independent cartridge.
        if (s.Continuation is { } cont && (cont.Initial == null || cont.Completed == null || cont.Samples == null || cont.Completed.Length > 65536 || cont.Completed.Length != cont.Samples.Length || cont.Completed.Any(c => c == null || c.Pins?.Length != 8 || c.Address is < 0 or > 0xffffff) || cont.Samples.Any(x => x == null)))
            throw new InvalidDataException("Invalid SA-1 CPU continuation journal.");
        if (s.PendingCycle is { } pending && (pending.Pins?.Length != 8 || pending.Address is < 0 or > 0xffffff || !pending.Read && pending.Valid && pending.Value == null))
            throw new InvalidDataException("Invalid SA-1 pending pins.");
        var probe = new Cpu65816(new ValidationBus());
        SetRegisters(probe, s.Registers);
        probe.RestoreContinuation(s.Continuation);
        probe.RestoreInterrupts(s.Interrupts);
        s.Iram.CopyTo(Iram, 0);
        s.RegistersIo.CopyTo(io, 0);
        SetRegisters(Processor, s.Registers);
        Processor.RestoreContinuation(s.Continuation);
        Processor.RestoreInterrupts(s.Interrupts);
        clock = s.Clock;
        target = clock;
        profile = p;
        pendingCycle = s.PendingCycle;
        cycleRemaining = s.CycleRemaining;
        openBus = s.OpenBus;
        resetAsserted = s.ResetAsserted;
        hold = s.Hold;
        sa1Flags = s.Sa1Flags;
        cpuFlags = s.CpuFlags;
        timerH = s.TimerH;
        timerV = s.TimerV;
        timerSubclock = s.TimerSubclock;
        latchedH = s.LatchedH;
        latchedV = s.LatchedV;
        mathDeadline = s.MathDeadline;
        mathResult = s.MathResult;
        pendingMath = s.PendingMath;
        overflow = s.Overflow;
        pendingOverflow = s.PendingOverflow;
        bitAddress = s.BitAddress;
        bitOffset = s.BitOffset;
        dma = s.Dma with
        {
            Conversion = s.Dma.Conversion.ToArray()
        };
        hostResource = s.HostResource;
        hostBusyUntil = s.HostBusyUntil;
        cpuCycles = s.CpuCycles;
        dmaBytes = s.DmaBytes;
        conversionLine = s.ConversionLine;
        cc = s.Conversion1 with
        {
            Buffers = s.Conversion1.Buffers.ToArray()
        };
        UpdateInterrupts();
    }

    private static void SetRegisters(Cpu65816 c, CpuRegisters r)
    {
        c.A = r.A;
        c.X = r.X;
        c.Y = r.Y;
        c.S = r.S;
        c.D = r.D;
        c.PC = r.PC;
        c.P = r.P;
        c.PBR = r.PBR;
        c.DBR = r.DBR;
        c.E = r.E;
        c.Waiting = r.Waiting;
        c.Stopped = r.Stopped;
        c.Instructions = r.Instructions;
    }

    private sealed class ValidationBus : ICpuBus
    {
        public byte Read(int a) => 0;
        public void Write(int a, byte v)
        {
        }
    }
}
