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
// Checks that arbitrary suspension/resume preserves hardware state and output order.
//

using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;

// Regression against the frozen pre-M9 exception implementation, not a hardware oracle.
static class SuspendProof
{
    sealed class Stop : Exception;
    sealed class Bus : ISpcBus
    {
        public byte[] Memory = new byte[65536];
        public List<string> Trace = [];
        public byte Read(ushort a)
        {
            byte v = Memory[a];
            Trace.Add($"R{a:X4}:{v:X2}");
            return v;
        }

        public void Write(ushort a, byte v)
        {
            Memory[a] = v;
            Trace.Add($"W{a:X4}:{v:X2}");
        }
    }

    static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    public static int Run(string[] args)
    {
        string prefix = args[0];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefix))!);
        using var reference = args.Length > 1 ? new StreamReader(new GZipStream(File.OpenRead(args[1]), CompressionMode.Decompress)) : null;
        using var output = new StreamWriter(new GZipStream(File.Create(prefix + ".jsonl.gz"), CompressionLevel.SmallestSize));
        var run = typeof(Spc700).GetMethod("RunInstruction")!;
        var hotCounter = typeof(Spc700).GetProperty("HotInstructionsCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var legacyCounter = typeof(Spc700).GetProperty("ReplayRunsCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var valueCounter = typeof(Spc700).GetProperty("HotValueEntriesStarted", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var materializedCounter = typeof(Spc700).GetProperty("HotEntryRecordsMaterialized", BindingFlags.Instance | BindingFlags.NonPublic)!;
        long hotCompletions = 0, legacyCompletions = 0, valueEntries = 0, materializedEntries = 0;
        object? Invoke(Spc700 cpu)
        {
            long hot = (long)hotCounter.GetValue(cpu)!, legacy = (long)legacyCounter.GetValue(cpu)!, values = (long)valueCounter.GetValue(cpu)!, records = (long)materializedCounter.GetValue(cpu)!;
            var result = run.Invoke(cpu, null);
            hotCompletions += (long)hotCounter.GetValue(cpu)! - hot;
            legacyCompletions += (long)legacyCounter.GetValue(cpu)! - legacy;
            valueEntries += (long)valueCounter.GetValue(cpu)! - values;
            materializedEntries += (long)materializedCounter.GetValue(cpu)! - records;
            return result;
        }

        var prepare = typeof(Spc700).GetProperty("PrepareCycle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var complete = typeof(Spc700).GetProperty("EffectCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool cooperative = prepare.PropertyType == typeof(Func<SpcCycle, bool>);
        long cases = 0;
        for (int seed = 0; seed < 8; seed++)
            for (int op = 0; op < 256; op++)
            {
                var random = new Random(seed * 256 + op + 913);
                byte[] memory = new byte[65536];
                random.NextBytes(memory);
                memory[0x200] = (byte)op;
                var registers = new SpcRegisters(0x200, (byte)random.Next(256), seed == 0 ? (byte)0 : (byte)random.Next(256), (byte)random.Next(256), seed == 0 ? (byte)0 : (byte)random.Next(256), (byte)(seed * 37), false, false, 17);
                (Spc700 Cpu, Bus Bus) Machine()
                {
                    var b = new Bus
                    {
                        Memory = memory.ToArray()
                    };
                    var c = new Spc700(b);
                    c.Restore(registers);
                    return (c, b);
                }

                var whole = Machine();
                var cycles = new List<SpcCycle>();
                whole.Cpu.CycleObserved = cycles.Add;
                whole.Cpu.Step();
                foreach (bool after in new[]
                {
                    false,
                    true
                }

                )
                    for (int stop = 0; stop < cycles.Count; stop++)
                    {
                        var m = Machine();
                        int index = 0;
                        var observations = new List<SpcCycle>();
                        m.Cpu.CycleObserved = observations.Add;
                        bool Before(SpcCycle c)
                        {
                            if (!after && index++ == stop)
                            {
                                if (!cooperative)
                                    throw new Stop();
                                return false;
                            }

                            return true;
                        }

                        bool After(SpcCycle c)
                        {
                            if (after && index++ == stop)
                            {
                                if (!cooperative)
                                    throw new Stop();
                                return false;
                            }

                            return true;
                        }

                        prepare.SetValue(m.Cpu, cooperative ? (Delegate)(Func<SpcCycle, bool>)Before : (Action<SpcCycle>)(c => Before(c)));
                        complete.SetValue(m.Cpu, cooperative ? (Delegate)(Func<SpcCycle, bool>)After : (Action<SpcCycle>)(c => After(c)));
                        try
                        {
                            Invoke(m.Cpu);
                        }
                        catch (TargetInvocationException e)when (e.InnerException is Stop)
                        {
                        }

                        string row = JsonSerializer.Serialize(new { seed, op, stop, after, registers = m.Cpu.Capture(), continuation = m.Cpu.CaptureContinuation(), memorySha256 = Hash(m.Bus.Memory), bus = m.Bus.Trace, observations });
                        output.WriteLine(row);
                        if (reference != null && reference.ReadLine() != row)
                            throw new Exception($"Frozen suspension mismatch op{op:X2} seed{seed} {(after ? "after" : "before")} cycle{stop}");
                        var restored = new Spc700(new Bus { Memory = m.Bus.Memory.ToArray() });
                        restored.Restore(m.Cpu.Capture());
                        restored.RestoreContinuation(m.Cpu.CaptureContinuation());
                        Invoke(restored);
                        if (JsonSerializer.Serialize(restored.Capture()) != JsonSerializer.Serialize(whole.Cpu.Capture()))
                            throw new Exception("SPC restored continuation mismatch");
                        prepare.SetValue(m.Cpu, null);
                        complete.SetValue(m.Cpu, null);
                        Invoke(m.Cpu);
                        if (JsonSerializer.Serialize(m.Cpu.Capture()) != JsonSerializer.Serialize(whole.Cpu.Capture()) || !m.Bus.Memory.SequenceEqual(whole.Bus.Memory) || !m.Bus.Trace.SequenceEqual(whole.Bus.Trace))
                            throw new Exception($"SPC resumed state/effects mismatch {op:X2}/{seed}/{stop}/{after}");
                        cases++;
                    }
            }

        if (reference?.ReadLine() != null)
            throw new Exception("Extra frozen suspension rows");
        File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(new { cases, cooperative, hotCompletions, legacyCompletions, valueEntries, materializedEntries, entryCounterScope = "RunInstruction deltas; public CaptureContinuation calls between invocations are excluded from materializedEntries", reference = args.ElementAtOrDefault(1), accuracy = "Old10164c7 exception semantics regression; external SPC oracle remains separate" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SPC suspension: {cases} exact cases, cooperative={cooperative}");
        return 0;
    }
}
