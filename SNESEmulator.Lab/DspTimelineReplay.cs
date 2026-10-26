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
// Replays identical external DSP input timelines and compares timed PCM/echo results.
//

using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Testing;

/// <summary>Replays external RAM/register inputs into the own DSP. Echo writes are
/// compared as outputs and never overwrite own RAM. Exact tick epochs need no alignment.</summary>
static class DspTimelineReplay
{
    public static int Run(string[] args)
    {
        string prefix = args.ElementAtOrDefault(0) ?? "artifacts/dsp-timeline-replay";
        var report = new TestReport("isolated-dsp-external-timeline");
        const string directory = "testdata/r3-dsp-timeline";
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        var root = manifest.RootElement;
        byte[] Load(string suffix)
        {
            var file = root.GetProperty("files").GetProperty(suffix);
            byte[] encoded = File.ReadAllBytes(Path.Combine(directory, file.GetProperty("file").GetString()!));
            if (Hash(encoded) != file.GetProperty("sha256").GetString()) throw new InvalidDataException("Captured fixture identity changed.");
            using var source = new MemoryStream(encoded);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var target = new MemoryStream();
            gzip.CopyTo(target);
            byte[] raw = target.ToArray();
            if (raw.Length != file.GetProperty("rawBytes").GetInt32() || Hash(raw) != file.GetProperty("rawSha256").GetString())
                throw new InvalidDataException("Uncompressed fixture identity changed.");
            return raw;
        }
        byte[] initial = Load("initial.bin"), events = Load("events.bin"), expected = Load("pcm.bin");
        long endTicks = root.GetProperty("executedDspTicks").GetInt64();
        void Test(string name, Action body)
        {
            try { body(); report.Add(name, "pass"); }
            catch (Exception exception) { report.Add(name, "fail", exception.ToString()); }
        }
        Test("All native stereo pairs and timed echo writes match the independent input timeline", () =>
        {
            var result = Replay(initial, events, expected, endTicks);
            report.Metadata["result"] = result;
            if (result.Pairs != 89524 || result.EchoWrites != 191408 || result.Events != 339026)
                throw new InvalidDataException("Captured timeline coverage changed.");
        });
        void MustReject(string label, byte[] alteredEvents, byte[] alteredPcm)
        {
            bool rejected = false;
            try { Replay(initial, alteredEvents, alteredPcm, endTicks); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Negative control was accepted: " + label);
        }
        Test("Changed native PCM remains an accuracy failure", () =>
        {
            byte[] altered = expected.ToArray(); altered[42895 * 4] ^= 1;
            MustReject("PCM sample", events, altered);
        });
        Test("Changed echo output is compared rather than injected", () =>
        {
            byte[] altered = events.ToArray(); int at = FindEvent(events, 2); altered[at + 10] ^= 1;
            MustReject("echo output", altered, expected);
        });
        Test("Changed DSP register input cannot retain an old green result", () =>
        {
            byte[] altered = events.ToArray();
            int at = Enumerable.Range(0, events.Length / 12).Select(i => i * 12)
                .First(i => events[i + 11] == 1 && BitConverter.ToUInt16(events, i + 8) == 0x0c && events[i + 10] != 0);
            altered[at + 10] = 0;
            MustReject("DSP MMIO input", altered, expected);
        });
        Test("Chronological two-sample KON delay changes the independent output", () =>
        {
            var rows = Enumerable.Range(0, events.Length / 12).Select(i => events.AsSpan(i * 12, 12).ToArray()).ToArray();
            byte[] keyOn = rows.First(row => row[11] == 1 && BitConverter.ToUInt16(row, 8) == 0x4c && row[10] != 0);
            long originalTick = BitConverter.ToInt64(keyOn);
            BitConverter.GetBytes(originalTick + 64).CopyTo(keyOn, 0);
            byte[] delayed = rows.OrderBy(row => BitConverter.ToInt64(row)).SelectMany(row => row).ToArray();
            bool rejected = false;
            try { Replay(initial, delayed, expected, endTicks); }
            catch (InvalidDataException exception) when (exception.Message.StartsWith("DSP echo mismatch") || exception.Message.StartsWith("Native PCM differs")) { rejected = true; }
            if (!rejected) throw new Exception("Chronologically valid KON delay escaped the PCM/echo comparator.");
        });
        Test("Missing reference echo output fails complete sequence coverage", () =>
        {
            int at = FindEvent(events, 2);
            byte[] altered = events[..at].Concat(events[(at + 12)..]).ToArray();
            MustReject("missing echo output", altered, expected);
        });
        report.Metadata["evidence"] = root.GetProperty("evidence").GetString();
        report.Metadata["coreAssemblySha256"] = Hash(File.ReadAllBytes(typeof(SnesSystem).Assembly.Location));
        report.Metadata["referenceCommit"] = root.GetProperty("referenceCommit").GetString();
        report.Metadata["firmwareSha256"] = root.GetProperty("firmwareSha256").GetString();
        report.Metadata["epoch"] = "tick 1 executes DSP phase0; phase27 emits first pair at tick28; inputs at tick N occur after N executed DSP phases; no phase rotation or sample shift";
        report.Save(prefix);
        return report.ExitCode;
    }

    private static int FindEvent(byte[] events, byte kind)
    {
        for (int at = 0; at < events.Length; at += 12) if (events[at + 11] == kind) return at;
        throw new InvalidDataException("Missing negative-control event kind.");
    }

    private sealed record Result(long Events, long EchoWrites, long InputRamWrites, long RegisterWrites, int Pairs, string PcmSha256);
    private static Result Replay(byte[] initial, byte[] events, byte[] expected, long endTicks)
    {
        if (initial.Length != 65536 + 128 || events.Length % 12 != 0) throw new InvalidDataException("Invalid timeline layout.");
        byte[] ram = initial[..65536], registers = new byte[128];
        var dsp = new SnesDsp(ram, registers);
        initial.AsSpan(65536).CopyTo(registers);
        // Reflection stays in this lab adapter; the product has no external replay API.
        var advance = typeof(SnesDsp).GetMethod("AdvanceTo", BindingFlags.NonPublic | BindingFlags.Instance)!.CreateDelegate<Action<long>>(dsp);
        var ownWrites = new Queue<(long Crystal, int Address, int Value)>();
        typeof(SnesDsp).GetField("Committed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dsp, (Action<DspPhase>)(phase =>
        {
            foreach (var access in phase.Memory) if (access.Write) ownWrites.Enqueue((phase.Crystal, access.Address, access.Value));
        }));
        using var source = new BinaryReader(new MemoryStream(events, false));
        using var output = new MemoryStream();
        using var pcm = new BinaryWriter(output);
        void Advance(long tick)
        {
            advance(checked(tick * 24));
            foreach (var sample in dsp.DrainSamples()) { pcm.Write(sample.Left); pcm.Write(sample.Right); }
        }
        long previous = 0, count = 0, echoes = 0, inputs = 0, regWrites = 0;
        while (source.BaseStream.Position < source.BaseStream.Length)
        {
            long tick = source.ReadInt64(); int address = source.ReadUInt16(), value = source.ReadByte(), kind = source.ReadByte();
            if (tick < previous || tick > endTicks) throw new InvalidDataException("Unordered/out-of-range DSP event.");
            previous = tick;
            Advance(tick);
            switch (kind)
            {
                case 0: ram[address] = (byte)value; inputs++; break;
                case 1:
                    if (address >= 128) throw new InvalidDataException("DSP register out of range.");
                    dsp.WriteRegister(address, (byte)value); regWrites++; break;
                case 2:
                    if (!ownWrites.TryDequeue(out var actual) || actual != (tick * 24, address, value))
                        throw new InvalidDataException($"DSP echo mismatch at tick {tick}: expected {address:X4}={value:X2}; actual {actual}.");
                    echoes++; break;
                default: throw new InvalidDataException("Unknown event kind.");
            }
            count++;
        }
        Advance(endTicks);
        if (ownWrites.Count != 0) throw new InvalidDataException("Uncompared own DSP echo writes remain.");
        byte[] actualPcm = output.ToArray();
        if (!actualPcm.AsSpan().SequenceEqual(expected))
        {
            int first = Enumerable.Range(0, Math.Min(actualPcm.Length, expected.Length)).FirstOrDefault(i => actualPcm[i] != expected[i], -1);
            throw new InvalidDataException($"Native PCM differs at pair {(first < 0 ? -1 : first / 4)}; bytes {actualPcm.Length}/{expected.Length}.");
        }
        return new(count, echoes, inputs, regWrites, actualPcm.Length / 4, Hash(actualPcm));
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
