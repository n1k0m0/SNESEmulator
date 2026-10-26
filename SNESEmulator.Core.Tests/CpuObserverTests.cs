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
// Checks that optional CPU observation preserves cycle effects and exact execution state.
// Assertions establish their stated test scope, not general physical SNES certification.
//

using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;

static class CpuObserverTests
{
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    static string Json<T>(T value)=>JsonSerializer.Serialize(value);
    static SnesSystem Machine(params byte[] program)
    {
        var m=new SnesSystem();m.Insert(new Cartridge(DiagnosticRom.Create()));m.Bus.Apu.Isolated=true;
        m.Cpu.PC=0;m.Cpu.PBR=m.Cpu.DBR=0;program.CopyTo(m.Bus.Wram,0);return m;
    }
    public static void Run(Action<string,Action> test,string directory)
    {
        test("R6 repeated public read admissions and latched results retain independent identities",()=>{
            var m=Machine(0xad,0x10,0,0x80,0xfb);m.Bus.Wram[0x10]=0x53;var original=m.Cpu.BeforeCycle!;
            m.RunUntil(100);var pending=new List<CpuCycle>();var actual=new List<CpuCycle>();
            m.Cpu.BeforeCycle=c=>{original(c);if(c.Valid&&c.Read&&c.Address==0x10)pending.Add(c);};
            m.Cpu.CycleObserved=c=>{if(c.Valid&&c.Read&&c.Address==0x10)actual.Add(c);};m.RunUntil(500);
            Check(pending.Count>=3&&actual.Count==pending.Count,"Repeated literal reads were omitted");
            Check(pending.Distinct(ReferenceEqualityComparer.Instance).Count()==pending.Count,"Public pending records were reused");
            Check(actual.Distinct(ReferenceEqualityComparer.Instance).Count()==actual.Count,"Committed read records were reused");
            for(int i=0;i<pending.Count;i++)Check(!ReferenceEquals(pending[i],actual[i])&&actual[i].Value==0x53,"Read latch reused pending identity or stale bus data");
            m.Cpu.BeforeCycle=original;m.Cpu.CycleObserved=null;m.RunUntil(600);
            var equivalent=(Action<CpuCycle>)Delegate.CreateDelegate(typeof(Action<CpuCycle>),original.Target,original.Method);
            Check(!ReferenceEquals(equivalent,original)&&equivalent.Equals(original),"Expected equal but independently created delegate");
            m.Cpu.BeforeCycle=equivalent;m.RunUntil(700);Check(ReferenceEquals(equivalent,m.Cpu.BeforeCycle),"Equivalent delegate setter/getter was intercepted");
            m.Bus.Wram[0x10]=0x64;m.RunUntil(800);Check(m.Cpu.A==0x64,"Repeated admission returned stale bus data");
        });
        test("R6 bus-installed hooks share the actual read record with journal and trailing hold",()=>{
            var m=Machine(0xad,0x10,0,0xcb);m.Bus.Wram[0x10]=0x53;
            var before=m.Cpu.BeforeCycle;var effect=m.Cpu.EffectCompleted!;var after=m.Cpu.AfterCycle!;
            CpuCycle? actual=null;int effects=0,observations=0,afters=0;
            m.Bus.AccessObserved=(direction,address,value)=>{
                if(direction!="read"||address!=0x10)return;
                m.Cpu.EffectCompleted=c=>{
                    effect(c);actual=c;effects++;
                    Check(ReferenceEquals(m.CapturePhase().HeldRead,c),"Read hold copied the actual cycle");
                    Check(ReferenceEquals(m.Cpu.CaptureContinuation()!.Completed[^1],c),"Journal copied the actual cycle");
                    // Replacements installed inside the bus effect and the earlier
                    // callback must apply to this same access, not the next one.
                    m.Cpu.CycleObserved=row=>{
                        observations++;Check(ReferenceEquals(row,actual),"Late cycle observer received another object");
                        m.Cpu.AfterCycle=last=>{
                            afters++;Check(ReferenceEquals(last,actual),"Late after hook received another object");
                            m.Cpu.EffectCompleted=effect;m.Cpu.CycleObserved=null;m.Cpu.AfterCycle=after;after(last);
                        };
                    };
                };
            };
            m.RunUntil(100);
            Check(effects==1&&observations==1&&afters==1&&m.Cpu.A==0x53,"Late subscribers missed or repeated the read");
            Check(ReferenceEquals(before,m.Cpu.BeforeCycle),"Public built-in hook getter changed");
        });
        test("R6 composed hooks retain pending identity for writes and cached idle cycles",()=>{
            var m=Machine(0xa9,0x53,0x8d,0x10,0,0xe6,0x10,0xcb);
            var before=m.Cpu.BeforeCycle!;var effect=m.Cpu.EffectCompleted!;var after=m.Cpu.AfterCycle!;
            CpuCycle? pending=null,actual=null;int reads=0,writes=0,idles=0,invalidWrites=0;
            Action<CpuCycle> extra=c=>pending=c;m.Cpu.BeforeCycle+=extra;
            Check(m.Cpu.BeforeCycle!.GetInvocationList().Length==2,"Public += did not compose delegates");
            m.Cpu.EffectCompleted=c=>{
                effect(c);actual=c;Check(pending!=null,"Pending subscriber was omitted");
                bool changed=pending!.Value!=c.Value;
                Check(ReferenceEquals(pending,c)==!changed,"Pending/actual reference contract changed");
                Check(ReferenceEquals(m.Cpu.CaptureContinuation()!.Completed[^1],c),"Completed journal identity changed");
                if(!c.Valid){idles++;if(!c.Read&&c.Value.HasValue)invalidWrites++;}else if(c.Read)reads++;else writes++;
            };
            m.Cpu.CycleObserved=c=>Check(ReferenceEquals(c,actual),"Effect/observation objects differ");
            m.Cpu.AfterCycle=c=>{Check(ReferenceEquals(c,actual),"Effect/after objects differ");after(c);};
            m.RunUntil(160);Check(reads>0&&writes==2&&idles>0&&invalidWrites==1&&m.Bus.Wram[0x10]==0x54,$"Missing literal coverage: reads={reads}, writes={writes}, idles={idles}, invalidWrites={invalidWrites}, memory={m.Bus.Wram[0x10]:X2}, E={m.Cpu.E}");
            m.Cpu.BeforeCycle-=extra;Check(m.Cpu.BeforeCycle!.GetInvocationList().Length==1,"Public -= did not remove subscriber");
            m.Cpu.BeforeCycle=null;Check(m.Cpu.BeforeCycle==null,"Public null assignment was hidden");
            m.Cpu.BeforeCycle=before;m.Cpu.EffectCompleted=effect;m.Cpu.CycleObserved=null;m.Cpu.AfterCycle=after;
            Check(ReferenceEquals(before,m.Cpu.BeforeCycle),"Original delegate was not restored");m.RunUntil(200);
            Check(m.Clock.Now==200,"Restored built-in delegate stopped scheduling");
        });
        test("R6 interrupt polling may replace or remove the current admission hook",()=>{
            var m=Machine(0xea,0xcb);var original=m.Cpu.BeforeCycle!;int stale=0,current=0;
            m.Cpu.BeforeCycle=c=>{stale++;original(c);};
            m.Cpu.InterruptPollingAllowed=()=>{m.Cpu.BeforeCycle=c=>{current++;original(c);};return false;};
            m.Cpu.PulseNmi();m.RunUntil(80);Check(stale==0&&current>0,"Admission used a hook captured before interrupt polling");
            var removed=Machine(0xea,0xcb);removed.Cpu.EffectCompleted=null;removed.Cpu.AfterCycle=null;removed.Cpu.ResumeCycle=null;
            removed.Cpu.InterruptPollingAllowed=()=>{removed.Cpu.BeforeCycle=null;return false;};removed.Cpu.PulseNmi();
            long clock=removed.Clock.Now;Check(removed.Cpu.AdvanceCycle(),"Expected exactly one externally observable cycle");
            Check(removed.Cpu.BeforeCycle==null&&removed.Clock.Now==clock,"Removed scheduler still received admission");
            var row=removed.Cpu.CaptureContinuation()!.Completed.Single();Check(row.Address==0&&row.Value==0xea,"Removed hook changed the bus effect");
        });
        test("R6 built-in and wrapped admission retain every pre-read and trailing budget state",()=>{
            var fast=Machine(0xad,0x10,0,0x8d,0x11,0,0xcb);var wrapped=Machine(0xad,0x10,0,0x8d,0x11,0,0xcb);
            fast.Bus.Wram[0x10]=wrapped.Bus.Wram[0x10]=0x53;
            var original=wrapped.Cpu.BeforeCycle!;int attempts=0,fastReads=0,wrappedReads=0;
            wrapped.Cpu.BeforeCycle=c=>{if(c.Valid&&c.Read&&c.Address==0x10)attempts++;original(c);};
            fast.Cpu.CycleObserved=c=>{if(c.Valid&&c.Read&&c.Address==0x10)fastReads++;};
            wrapped.Cpu.CycleObserved=c=>{if(c.Valid&&c.Read&&c.Address==0x10)wrappedReads++;};
            for(int horizon=1;horizon<=120;horizon++){
                fast.RunUntil(horizon);wrapped.RunUntil(horizon);
                Check(Json(fast.CapturePhase())==Json(wrapped.CapturePhase()),$"Scheduler phase differs at {horizon}");
                Check(Json(fast.Cpu.CaptureContinuation())==Json(wrapped.Cpu.CaptureContinuation()),$"Journal differs at {horizon}");
                Check(fast.Cpu.Registers==wrapped.Cpu.Registers,$"Registers differ at {horizon}");
                Check(StateSerializer.Capture(fast).SequenceEqual(StateSerializer.Capture(wrapped)),$"Full State9 bytes differ at {horizon}");
                if(horizon is 1 or 7 or 15 or 21 or 31){
                    string path=Path.Combine(directory,"r6-admission-"+horizon);StateSerializer.Save(fast,path);
                    var restored=StateSerializer.Load(fast.Bus.Cartridge!,path);restored.RunUntil(120);
                    var oracle=Machine(0xad,0x10,0,0x8d,0x11,0,0xcb);oracle.Bus.Wram[0x10]=0x53;oracle.RunUntil(120);
                    Check(StateSerializer.Capture(restored).SequenceEqual(StateSerializer.Capture(oracle)),"Restored budget duplicated a committed effect");
                }
            }
            Check(attempts>1&&fastReads==1&&wrappedReads==1&&fast.Bus.Wram[0x11]==0x53,"Retry or latched-read cardinality changed");
        });
    }
}
