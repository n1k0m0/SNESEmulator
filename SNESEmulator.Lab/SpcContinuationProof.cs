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
// Compares resumable SPC700 execution with frozen and external vector expectations.
//

using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;

// Regression of electrical admission/commit and existing State9 reconstruction.
static class SpcContinuationProof
{
    public static int Run(string[] args)
    {
        var report = new SNESEmulator.Testing.TestReport("SPC-hot-path-guard-and-lifecycle");
        var checkedCpus = new List<Spc700>();
        Apu NewApu()
        {
            var apu = new Apu();
            checkedCpus.Add(apu.Cpu);
            return apu;
        }

        void Check(bool ok, string detail)
        {
            if (!ok)
                throw new Exception(detail);
        }

        void Run(string name, Action body)
        {
            try
            {
                body();
                report.Add(name, "pass");
            }
            catch (Exception e)
            {
                report.Add(name, "fail", e.Message);
            }
        }

        void Set(Spc700 c, string name, object? value) => typeof(Spc700).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(c, value);
        Spc700 Make(Bus b)
        {
            b.Ram[0x200] = 0xe8;
            b.Ram[0x201] = 0x35;
            var c = new Spc700(b);
            checkedCpus.Add(c);
            c.Restore(new(0x200, 0, 0, 0, 0xef, 2, false, false, 0));
            return c;
        }

        void Finish(Spc700 c)
        {
            for (int i = 0; i < 16; i++)
                if (c.RunInstruction())
                    return;
            throw new Exception("No instruction completion");
        }

        string State(Apu a) => JsonSerializer.Serialize(a.Capture());
        Run("Legacy veto precedes fast electrical admission", () =>
        {
            var b = new Bus();
            var c = Make(b);
            int fast = 0;
            Set(c, "PrepareBusCycle", (Func<ushort?, string, bool>)((a, d) =>
            {
                fast++;
                return true;
            }));
            Set(c, "PrepareCycle", (Func<SpcCycle, bool>)(_ => false));
            Check(!c.RunInstruction(), "Legacy veto ignored");
            Check(fast == 0 && b.Reads.Count == 0, "Fast admission or bus ran despite legacy veto");
        });
        Run("Fast veto suppresses bus and observer until admission", () =>
        {
            var b = new Bus();
            var c = Make(b);
            int before = 0;
            bool allowed = false;
            Set(c, "PrepareBusCycle", (Func<ushort?, string, bool>)((a, d) => allowed));
            c.BeforeCycle = _ => before++;
            Check(!c.RunInstruction(), "Fast veto ignored");
            Check(b.Reads.Count == 0 && before == 0, "Premature effect/observer");
            allowed = true;
            Finish(c);
            Check(c.A == 0x35 && c.PC == 0x202 && b.Reads.SequenceEqual(new ushort[] { 0x200, 0x201 }) && before == 2, "Admission duplicated or skipped a bus effect");
        });
        Run("Both postlatch hooks observe a committed cycle when fast hook yields", () =>
        {
            var b = new Bus();
            var c = Make(b);
            int fast = 0, legacy = 0;
            Set(c, "CompleteBusCycle", (Func<ushort?, byte?, string, bool>)((a, v, d) => ++fast != 1));
            Set(c, "EffectCompleted", (Func<SpcCycle, bool>)(_ =>
            {
                legacy++;
                return true;
            }));
            Check(!c.RunInstruction(), "Fast postlatch veto ignored");
            Check(fast == 1 && legacy == 1 && b.Reads.Count == 1 && c.PC == 0x200, "Postlatch callback skipped or PC advanced before yield");
            Finish(c);
            Check(c.A == 0x35 && c.PC == 0x202 && b.Reads.Count == 2 && fast == 2 && legacy == 2, "Postlatch resume repeated an effect/callback");
        });
        Run("Capture Restore after legacy admission veto preserves APU bus time", () =>
        {
            var a = NewApu();
            Set(a.Cpu, "PrepareCycle", (Func<SpcCycle, bool>)(_ => false));
            a.RunUntil(24);
            var snapshot = a.Capture();
            Check(a.Crystal == 0 && a.Cycles == 0, "Legacy veto advanced APU electrical time");
            var copy = NewApu();
            copy.Restore(snapshot);
            copy.RunUntil(24);
            var reference = NewApu();
            reference.RunUntil(24);
            Check(State(copy) == State(reference), "APU admission ran twice or was lost across restore");
        });
        Run("Port-read hold calls both hooks and survives APU Restore", () =>
        {
            Apu MakeApu()
            {
                var a = NewApu();
                a.Ram[0x200] = 0xe4;
                a.Ram[0x201] = 0xf4;
                a.CpuToApu[0] = 0xa5;
                a.Cpu.Restore(new(0x200, 0, 0, 0, 0xef, 2, false, false, 0));
                return a;
            }

            var a = MakeApu();
            int legacy = 0;
            Set(a.Cpu, "EffectCompleted", (Func<SpcCycle, bool>)(_ =>
            {
                legacy++;
                return true;
            }));
            a.RunUntil(60);
            Check(legacy == 3 && a.Cycles == 3 && a.Capture().Holding, "Missing legacy callback at held read latch");
            var copy = NewApu();
            copy.Restore(a.Capture());
            copy.RunUntil(72);
            var reference = MakeApu();
            reference.RunUntil(60);
            reference.RunUntil(72);
            Check(State(copy) == State(reference), "Held read duplicated/lost after restore");
        });
        Run("Reset discards an old suspended microprogram", () =>
        {
            var b = new Bus();
            var c = Make(b);
            Set(c, "PrepareCycle", (Func<SpcCycle, bool>)(_ => false));
            Check(!c.RunInstruction(), "Expected suspension");
            Set(c, "PrepareCycle", null);
            b.Ram[0xffc0] = 0xe8;
            b.Ram[0xffc1] = 0x42;
            c.Reset();
            Finish(c);
            Check(c.A == 0x42 && c.PC == 0xffc2 && c.Instructions == 1 && c.CaptureContinuation() == null, "Reset resumed an old instruction");
        });
        Run("Explicit replacement continuation discards old local operands", () =>
        {
            var b = new Bus();
            var c = Make(b);
            int effects = 0;
            Set(c, "EffectCompleted", (Func<SpcCycle, bool>)(_ => ++effects != 1));
            Check(!c.RunInstruction(), "Expected postlatch suspension");
            Set(c, "EffectCompleted", null);
            c.RestoreContinuation(null);
            c.Restore(new(0x300, 0, 0, 0, 0xef, 2, false, false, 0));
            b.Ram[0x300] = 0xcd;
            b.Ram[0x301] = 0x7f;
            Finish(c);
            Check(c.X == 0x7f && c.A == 0 && c.PC == 0x302 && c.Instructions == 1, "Replacement reused stale opcode/operand");
        });
        Run("Legacy value journal preserves postlatch veto and snapshot without repeating the effect", () =>
        {
            var b=new Bus();var c=Make(b);b.Ram[0x200]=0x88; // ADC A,#imm uses the original decoder.
            c.A=0x11;int effects=0;bool veto=true;
            Set(c,"CompleteBusCycle",(Func<ushort?,byte?,string,bool>)((a,v,d)=>{effects++;return a!=0x201||!veto;}));
            Check(!c.RunInstruction(),"Operand postlatch veto was ignored");
            var journal=c.CaptureContinuation()!;
            Check(journal.Completed.Length==2&&journal.Completed[1].Value==0x35&&c.PC==0x201&&c.A==0x11,"Incorrect exposed latch or register point");
            Check(b.Reads.SequenceEqual(new ushort[]{0x200,0x201})&&effects==2,"Unexpected pre-resume effects");
            var copyBus=new Bus();b.Ram.CopyTo(copyBus.Ram,0);var copy=new Spc700(copyBus);copy.RestoreContinuation(journal);
            veto=false;Finish(c);Finish(copy);
            Check(c.A==0x46&&c.PC==0x202&&copy.Capture()==c.Capture(),"Legacy resume registers differ");
            Check(b.Reads.Count==2&&copyBus.Reads.Count==0&&effects==2,"A latched read or completion hook repeated");
        });
        Run("Legacy observer changes retain before and shared committed record semantics", () =>
        {
            var b=new Bus();var c=Make(b);b.Ram[0x200]=0x88;c.A=0x11;
            SpcCycle? effect=null,observed=null;int before=0;
            c.BeforeCycle=row=>{
                if(row.Address!=0x201)return;before++;
                Check(row.Value==0,"BeforeCycle must retain the decoder's pending read value");
                Set(c,"EffectCompleted",(Func<SpcCycle,bool>)(actual=>{
                    effect=actual;c.CycleObserved=next=>observed=next;return true;
                }));
            };
            Finish(c);
            Check(before==1&&effect?.Value==0x35&&ReferenceEquals(effect,observed),"Late hook changes lost or replaced the immutable committed row");
            Check(c.A==0x46&&c.PC==0x202&&b.Reads.Count==2,"Observer changes altered the instruction");
        });
        long Count(Spc700 cpu, string name) => (long)typeof(Spc700).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cpu)!;
        report.Metadata["executionPaths"] = new
        {
            hotInstructions = checkedCpus.Sum(cpu => Count(cpu, "HotInstructionsCompleted")),
            legacyCompletions = checkedCpus.Sum(cpu => Count(cpu, "ReplayRunsCompleted")),
            hotCycleAttempts = checkedCpus.Sum(cpu => Count(cpu, "HotCycleAttempts")),
            meaning = "Actual per-CPU diagnostics; guards cover both hot and legacy forms. Other opcodes are separately verified by mixed vector coverage."
        };
        report.Metadata["coreAssemblySha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Spc700).Assembly.Location)));
        report.Save(args.ElementAtOrDefault(0) ?? "artifacts/spc-continuation-guards");
        return report.ExitCode;
    }

    sealed class Bus : ISpcBus
    {
        public byte[] Ram = new byte[65536];
        public List<ushort> Reads = [];
        public byte Read(ushort a)
        {
            Reads.Add(a);
            return Ram[a];
        }

        public void Write(ushort a, byte v) => Ram[a] = v;
    }
}
