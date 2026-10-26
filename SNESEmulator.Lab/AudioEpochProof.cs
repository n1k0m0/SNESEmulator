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
// Investigates whole-console reset epochs without changing the shipping hardware clock model.
//

using System.IO.Compression;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Testing;

/// <summary>Source-model experiment, not a physical reset specification. The product's
/// default epoch stays unchanged; only this lab host delays CPU release explicitly.</summary>
static class AudioEpochProof
{
    public static int Run(string[] args)
    {
        string prefix = args.ElementAtOrDefault(0) ?? "artifacts/audio-epoch-proof";
        var report = new TestReport("audio-reset-epoch-isolation");
        const string directory = "testdata/r3-dsp-timeline";
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        var pcmFile = manifest.RootElement.GetProperty("files").GetProperty("pcm.bin");
        using var gzip = new GZipStream(File.OpenRead(Path.Combine(directory, pcmFile.GetProperty("file").GetString()!)), CompressionMode.Decompress);
        using var expectedStream = new MemoryStream(); gzip.CopyTo(expectedStream);
        byte[] expected = expectedStream.ToArray();
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(expected)) != pcmFile.GetProperty("rawSha256").GetString())
            throw new InvalidDataException("Independent PCM fixture changed.");
        string? previousFirmware = Environment.GetEnvironmentVariable("SNESEMULATOR_IPL");
        try
        {
            // Each case deliberately uses the freely redistributable Own IPL.
            Environment.SetEnvironmentVariable("SNESEMULATOR_IPL", null);
            foreach (int delay in new[] { 0, 186 })
            {
                try
                {
                    var profile = HardwareProfile.Ntsc with { Id = "r3-reference-crystal-epoch-experiment-unverified", ApuHzNumerator = 32040 * 768 };
                    var machine = new SnesSystem(profile);
                    byte[] rom = File.ReadAllBytes("testdata/m8-game/unnamed-snes-game.sfc");
                    if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rom)) != manifest.RootElement.GetProperty("program").GetProperty("romSha256").GetString())
                        throw new InvalidDataException("Independent program changed.");
                    machine.Insert(new Cartridge(rom));
                    long firstPortWrite = -1, commandWriteTick = -1;
                    int pollValue = -1, commandPc = -1;
                    machine.Bus.AccessObserved = (direction, address, value) =>
                    {
                        if (firstPortWrite < 0 && direction == "write" && (address & 0x40fffc) == 0x2140) firstPortWrite = machine.Clock.Now;
                    };
                    machine.Bus.Apu.PhaseObserved = phase =>
                    {
                        if (phase.Crystal < 51900 * 24 || phase.Crystal > 52020 * 24) return;
                        var entry = machine.Bus.Apu.Cpu.CaptureContinuation()?.Entry;
                        if (phase.Phase == "read-latch" && phase.Crystal / 24 == 51981 && phase.Cycle.Address == 0xf4 && entry?.PC == 0xffd0)
                            pollValue = phase.Cycle.Value!.Value;
                        if (phase.Phase == "write-latch" && phase.Cycle.Address == 0 && entry?.PC == 0xffd7)
                        { commandWriteTick = phase.Crystal / 24; commandPc = entry.PC; }
                    };
                    // The pinned reference executes 132 clocks before its 54-clock
                    // interrupt-shaped reset sequence. Hold only the lab CPU; let
                    // APU/PPU/master clocks execute. No sample or DSP phase is shifted.
                    if (delay != 0) { machine.Cpu.Stopped = true; machine.RunUntil(delay); machine.Cpu.Stopped = false; }
                    using var actualStream = new MemoryStream(); using var pcm = new BinaryWriter(actualStream);
                    var replay = manifest.RootElement.GetProperty("replay").GetProperty("rawFieldInputPlan").EnumerateArray()
                        .Select(row => (Field: row[0].GetInt32(), Mask: row[1].GetInt32())).ToArray();
                    while (machine.Frames < 168)
                    {
                        foreach (var input in replay) if (input.Field == machine.Frames) machine.Bus.Controllers.Buttons[0] = (ushort)input.Mask;
                        machine.RunFrame();
                        foreach (var sample in machine.Bus.Apu.Dsp.DrainSamples()) { pcm.Write(sample.Left); pcm.Write(sample.Right); }
                    }
                    byte[] actual = actualStream.ToArray();
                    int expectedPairs = manifest.RootElement.GetProperty("pairs").GetInt32();
                    if (expectedPairs != 89524 || expected.Length != expectedPairs * 4 || actual.Length % 4 != 0 || actual.Length < expected.Length)
                        throw new InvalidDataException("Truncated, unaligned or incomplete native stereo overlap.");
                    int overlap = expected.Length;
                    int first = Enumerable.Range(0, overlap).FirstOrDefault(i => actual[i] != expected[i], -1);
                    if (delay == 0 && (first / 4 != 88915 || firstPortWrite != 1088552 || commandWriteTick != 51994 || pollValue != 0xcc))
                        throw new InvalidDataException($"Default-epoch known-red control or causal bus witnesses changed: pair={first/4}, firstPort={firstPortWrite}, command={commandWriteTick}, poll={pollValue}.");
                    if (delay == 186 && (first != -1 || firstPortWrite != 1088746 || commandWriteTick != 52003 || pollValue != 0))
                        throw new InvalidDataException($"Source-equivalent epoch did not match reference PCM and causal bus witnesses: pair={first/4}, firstPort={firstPortWrite}, command={commandWriteTick}, poll={pollValue}.");
                    report.Add(delay == 0 ? "Default epoch reproduces known red pair88915" : "Explicit reference epoch matches every overlapping pair", "pass",
                        evidence: new { cpuReleaseDelayMasterClocks = delay, firstMismatchPair = first < 0 ? -1 : first / 4, firstPortWrite, pollTick = 51981, pollPc = 0xffd0, pollOpcode = 0x78, pollValue, commandWriteTick, commandPc, commandOpcode = 0xda, actualPairs = actual.Length / 4, referencePairs = expected.Length / 4, comparedPairs = overlap / 4, terminalWindow = "Distinct field-end/native capture boundaries retained", profile });
                }
                catch (Exception exception) { report.Add("epoch " + delay, "fail", exception.ToString()); }
            }
        }
        finally { Environment.SetEnvironmentVariable("SNESEMULATOR_IPL", previousFirmware); }
        report.Metadata["scope"] = "Explains the bsnes pair88915 case by CPU release -> port polling -> command timing. Original default accuracy rows remain red. Ares and physical reset behavior are not accepted.";
        report.Metadata["referenceSource"] = "bsnes 78474e5d39f58872dc21aca18cf9eb583b147fe7 sfc/cpu/cpu.cpp main():22*6 reset clocks, then processor/wdc65816/instruction.cpp interrupt():54 access clocks; ares has the same H=186 comment";
        report.Save(prefix);
        return report.ExitCode;
    }
}
