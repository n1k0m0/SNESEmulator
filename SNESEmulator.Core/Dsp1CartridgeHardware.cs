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
// Connects the uPD7725 to an explicitly configured DSP1 cartridge board.
// Host ports, rational chip time and continuation state stay inside the hardware core.
//

using System.Security.Cryptography;
using System.Text.Json;
namespace SNESEmulator.Core;

public enum Dsp1Variant { Dsp1, Dsp1A, Dsp1B, Synthetic }
public enum Dsp1Board { LoRom1MiB, LoRom2MiB, HiRom1MiB, HiRom2MiB }

// Firmware is immutable and shared by isolated cartridge clones; mutable chip
// RAM is never shared. Local firmware paths are not embedded in a savestate.
public interface ICartridgeConfiguration { }

public sealed class Dsp1Configuration : ICartridgeConfiguration
{
    private readonly byte[] firmware;
    public Dsp1Variant Variant { get; }
    public Dsp1Board Board { get; }
    public string FirmwareHash { get; }
    public long InstructionHz { get; } = 7600000;
    internal ReadOnlySpan<byte> Firmware => firmware;
    private Dsp1Configuration(byte[] bytes, Dsp1Variant variant, Dsp1Board board)
    {
        if (bytes.Length != 8192 || !Enum.IsDefined(variant) || !Enum.IsDefined(board)) throw new InvalidDataException("Invalid DSP1 firmware/variant/board.");
        firmware = bytes.ToArray(); Variant = variant; Board = board; FirmwareHash = Convert.ToHexString(SHA256.HashData(firmware));
        string expected = variant switch { Dsp1Variant.Dsp1 or Dsp1Variant.Dsp1A => "91E87D11E1C30D172556BED2211CCE2EFA94BA595F58C5D264809EF4D363A97B", Dsp1Variant.Dsp1B => "D789CB3C36B05C0B23B6C6F23BE7AA37C6E78B6EE9CEAC8D2D2AA9D8C4D35FA9", _ => FirmwareHash };
        if (expected != FirmwareHash) throw new InvalidDataException("Firmware bytes do not match the explicitly selected DSP1 variant (little-endian .rom required).");
    }
    public static Dsp1Configuration FromFirmware(byte[] bytes, Dsp1Variant variant, Dsp1Board board)
    {
        if (variant == Dsp1Variant.Synthetic) throw new ArgumentException("Use SyntheticProgram explicitly for authored diagnostic microcode.");
        return new(bytes, variant, board);
    }
    public static Dsp1Configuration SyntheticProgram(byte[] bytes, Dsp1Board board) => new(bytes, Dsp1Variant.Synthetic, board);
    private sealed record FileOptions(string Variant, string Board, string Firmware, string FirmwareSha256);
    public static Dsp1Configuration FromFile(string path)
    {
        try
        {
            byte[] json = AtomicFile.ReadBytes(path, 16384);
            var options = JsonSerializer.Deserialize<FileOptions>(json) ?? throw new InvalidDataException("Missing hardware configuration.");
            if (!Enum.TryParse<Dsp1Variant>(options.Variant, false, out var variant) || variant == Dsp1Variant.Synthetic || !Enum.TryParse<Dsp1Board>(options.Board, false, out var board))
                throw new InvalidDataException("Select an explicit Dsp1/Dsp1A/Dsp1B variant and supported board.");
            string firmwarePath = Path.GetFullPath(options.Firmware, Path.GetDirectoryName(Path.GetFullPath(path))!);
            var config = FromFirmware(AtomicFile.ReadBytes(firmwarePath, 8192), variant, board);
            if (!config.FirmwareHash.Equals(options.FirmwareSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Configured firmware SHA256 does not match.");
            return config;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException)
        { throw new InvalidDataException("Invalid DSP1 hardware configuration: " + e.Message, e); }
    }

    internal static Dsp1Configuration Resolve(string source)
    {
        string path = source + ".hardware.json";
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(path)) throw new NotSupportedException("Cartridge chipset03/05 is ambiguous. Provide a DSP1 board/variant/firmware configuration at <ROM path>.hardware.json; DSP2/3/4 remain unsupported.");
        return FromFile(path);
    }
}

public interface ICartridgeHardware
{
    string Kind { get; }
    bool TryRead(int address, byte openBus, long clock, HardwareProfile profile, out byte value)
    { value=openBus; if(!Decodes(address,out bool status))return false;value=Read(status,clock,profile);return true; }
    bool TryWrite(int address, byte value, long clock, HardwareProfile profile)
    { if(!Decodes(address,out bool status))return false;Write(status,value,clock,profile);return true; }
    bool Decodes(int address, out bool status);
    byte Read(bool status, long masterClock, HardwareProfile profile);
    void Write(bool status, byte value, long masterClock, HardwareProfile profile);
    void Reset(bool powerOn);
    void Synchronize(long masterClock, HardwareProfile profile);
    CartridgeHardwareState Capture(long masterClock, HardwareProfile profile);
    void Restore(CartridgeHardwareState state, long masterClock, HardwareProfile profile);
}
public sealed record CartridgeHardwareState(int Version, string Kind, JsonElement Payload);
public sealed record Dsp1HardwareState(int Version, string Kind, string Variant, string Board, string FirmwareHash, long InstructionHz, long LastMasterClock, long Remainder, Upd7725State Processor);

public sealed class Dsp1CartridgeHardware(Dsp1Configuration config) : ICartridgeHardware
{
    public string Kind => "NEC-DSP1";
    public Upd7725 Processor { get; } = new(config.Firmware);
    private long lastMaster, remainder;
    public bool Decodes(int address, out bool status)
    {
        int bank = (address >> 16) & 127, offset = address & 65535; status = false;
        switch (config.Board)
        {
            case Dsp1Board.LoRom1MiB: status = offset >= 0xc000; return bank is >= 0x30 and <= 0x3f && offset >= 0x8000;
            case Dsp1Board.LoRom2MiB: status = offset >= 0x4000; return bank is >= 0x60 and <= 0x6f && offset < 0x8000;
            case Dsp1Board.HiRom1MiB: status = offset >= 0x7000; return bank < 0x20 && offset is >= 0x6000 and < 0x8000;
            default: status = offset >= 0x7000; return (bank < 0x10 || bank is >= 0x20 and < 0x30) && offset is >= 0x6000 and < 0x8000;
        }
    }
    public void Synchronize(long clock, HardwareProfile profile)
    {
        if (clock < lastMaster) throw new InvalidOperationException("DSP1 timeline moved backwards.");
        // The chip has no shared CPU memory or interrupt output. Executing all
        // elapsed instructions immediately before a port/capture is therefore
        // equivalent to eager execution. Never stop early when RQM becomes1.
        Int128 sum = (Int128)(clock - lastMaster) * config.InstructionHz * profile.MasterHzDenominator + remainder;
        long count = checked((long)(sum / profile.MasterHzNumerator));
        remainder = (long)(sum % profile.MasterHzNumerator); lastMaster = clock;
        for (long i = 0; i < count; i++) Processor.Step();
    }
    public byte Read(bool status, long masterClock, HardwareProfile profile)
    { Synchronize(masterClock, profile); return status ? Processor.ReadStatus() : Processor.ReadData(); }
    public void Write(bool status, byte value, long masterClock, HardwareProfile profile)
    { Synchronize(masterClock, profile); if (!status) Processor.WriteData(value); }
    public void Reset(bool powerOn) { Processor.Reset(powerOn); lastMaster = remainder = 0; }
    public CartridgeHardwareState Capture(long masterClock, HardwareProfile profile)
    { Synchronize(masterClock, profile); var state = new Dsp1HardwareState(1, Kind, config.Variant.ToString(), config.Board.ToString(), config.FirmwareHash, config.InstructionHz, lastMaster, remainder, Processor.Capture()); return new(1, Kind, JsonSerializer.SerializeToElement(state)); }
    public void Restore(CartridgeHardwareState envelope, long masterClock, HardwareProfile profile)
    {
        if (envelope.Version != 1 || envelope.Kind != Kind) throw new InvalidDataException("Unsupported cartridge hardware state envelope.");
        var s = envelope.Payload.Deserialize<Dsp1HardwareState>() ?? throw new InvalidDataException("Missing DSP1 state payload.");
        if (s.Version != 1 || s.Kind != Kind || s.Variant != config.Variant.ToString() || s.Board != config.Board.ToString() || s.FirmwareHash != config.FirmwareHash || s.InstructionHz != config.InstructionHz || s.LastMasterClock != masterClock || s.Remainder < 0 || s.Remainder >= profile.MasterHzNumerator || s.Processor == null)
            throw new InvalidDataException("DSP1 state board, firmware, clock or version does not match.");
        // Remainder/instruction identity prevents malformed states manufacturing
        // extra instructions at a later host access; all restored fields remain
        // owned by the temporary machine until the enclosing loader succeeds.
        Int128 ticks = (Int128)masterClock * config.InstructionHz * profile.MasterHzDenominator;
        if ((long)(ticks % profile.MasterHzNumerator) != s.Remainder || (long)(ticks / profile.MasterHzNumerator) != s.Processor.Instructions) throw new InvalidDataException("Inconsistent DSP1 oscillator phase.");
        Processor.Restore(s.Processor); lastMaster = s.LastMasterClock; remainder = s.Remainder;
    }
}

