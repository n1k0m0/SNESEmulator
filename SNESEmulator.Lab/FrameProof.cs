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
// Checks completed-field horizons and captures exact deterministic output checkpoints.
//

using System.Security.Cryptography;
using System.Numerics;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;

static class FrameProof
{
    static string Hash(string file)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
    public static int Run(string[] args){
        string directory=args[0];Directory.CreateDirectory(directory);int cases=0;var results=new List<object>();
        foreach(bool pal in new[]{false,true})foreach(int initial in new[]{0,1})foreach(int line in new[]{0,1,64,127,128,129,192,250})foreach(bool observers in new[]{false,true}){
            var r=new PpuDiagnosticRom();r.Write(0x2133,initial);r.Palette(0,31);byte[] rom=r.Finish();var a=new SnesSystem(pal?HardwareProfile.PalProfile:HardwareProfile.Ntsc);a.Insert(new Cartridge(rom));
            a.RunUntil(line*1364L+(line==0?0:3));a.Bus.Write(0x2133,(byte)(initial^1));a.Bus.Apu.Dsp.DrainSamples();string path=Path.Combine(directory,"scratch.state");StateSerializer.Save(a,path);var b=StateSerializer.Load(a.Bus.Cartridge!,path);
            using var traceA=new MemoryStream();using var traceB=new MemoryStream();
            void Observe(SnesSystem m,MemoryStream bytes){var w=new BinaryWriter(bytes);void Row(string kind,object e){w.Write(kind);w.Write(m.Clock.Now);w.Write(JsonSerializer.Serialize(e));}
                m.CyclePhaseObserved=e=>Row("cpu",e);m.Bus.Apu.PhaseObserved=e=>Row("smp",e);m.Bus.Apu.DspPhaseObserved=e=>Row("dsp",e);m.Bus.Ppu.PhaseObserved=e=>Row("ppu",e);m.Clock.EventObserved=e=>Row("event",e);m.Bus.Dma.Observed=e=>Row("dma",e);
            }
            if(observers){Observe(a,traceA);Observe(b,traceB);}
            // Two fields cross the next V0 latch and both parities. Toggle again
            // only after the first field, so before/after V128 cases differ.
            for(int field=0;field<2;field++){
                a.RunFrame();long end=b.Frames+1;while(b.Frames<end)b.RunUntil(b.Clock.NextEventClock);
                StateSerializer.Save(a,path);string actual=Hash(path);StateSerializer.Save(b,path);string expected=Hash(path);
                if(actual!=expected||!traceA.GetBuffer().AsSpan(0,(int)traceA.Length).SequenceEqual(traceB.GetBuffer().AsSpan(0,(int)traceB.Length)))throw new Exception($"RunFrame horizon differs pal{pal} interlace{initial} line{line} observers{observers} field{field}");
                var sa=a.Bus.Apu.Dsp.DrainSamples();var sb=b.Bus.Apu.Dsp.DrainSamples();if(!sa.SequenceEqual(sb))throw new Exception("Native stereo differs");
                results.Add(new{pal,initial,line,observers,field,clock=a.Clock.Now,stateSha256=actual,traceSha256=Convert.ToHexStringLower(SHA256.HashData(traceA.GetBuffer().AsSpan(0,(int)traceA.Length)))});
                traceA.SetLength(0);traceB.SetLength(0);a.Bus.Write(0x2133,(byte)initial);b.Bus.Write(0x2133,(byte)initial);cases++;
            }
        }
        int cacheCases=0;string cachePath=Path.Combine(directory,"cache.state");
        void Equal(SnesSystem a,SnesSystem b,string label){a.RunFrame();b.RunFrame();StateSerializer.Save(a,cachePath);string actual=Hash(cachePath);StateSerializer.Save(b,cachePath);if(actual!=Hash(cachePath))throw new Exception("Derived cache affected "+label);cacheCases++;}
        var switching=new SnesSystem();
        foreach(bool pal in new[]{false,true,false,true}){
            byte[] bytes=DiagnosticRom.Create();switching.Insert(new Cartridge(bytes,options:new(Pal:pal)));switching.RunFrame();switching.RunUntil(switching.Clock.Now+12345);
            StateSerializer.Save(switching,cachePath);var cold=StateSerializer.Load(switching.Bus.Cartridge!,cachePath);switching.Reset();cold.Reset();Equal(switching,cold,"Reset/backward APU tick");
            switching.PowerOn();var fresh=new SnesSystem();fresh.Insert(new Cartridge(bytes,options:new(Pal:pal)));Equal(switching,fresh,"PowerOn/profile switch");
        }
        File.WriteAllText(Path.Combine(directory,"cache-result.json"),JsonSerializer.Serialize(new{cacheCases,profiles="automatic NTSC to PAL to NTSC to PAL, Reset/PowerOn/backward ticks"}));
        int clockCases=0;
        foreach(var profile in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile,HardwareProfile.Ntsc with{Id="synthetic-unreduced",MasterHzNumerator=945000000000,MasterHzDenominator=44000},HardwareProfile.Ntsc with{Id="synthetic-wide",MasterHzNumerator=3000000000000000000,MasterHzDenominator=100000,ApuHzNumerator=1000000000000000,ApuHzDenominator=2}}){
            var clock=new MasterClock(profile);clock.Reset();
            foreach(int delta in new[]{0,1,2,6,100,1364,12345,1,0,7777}){
                clock.AdvanceTo(clock.Now+delta);BigInteger sum=(BigInteger)clock.Now*profile.ApuHzNumerator*profile.MasterHzDenominator;BigInteger denominator=(BigInteger)profile.MasterHzNumerator*profile.ApuHzDenominator;
                if(clock.ApuTicks!=(long)(sum/denominator)||clock.ApuRemainder!=(long)(sum%denominator))throw new Exception("Clock differs from independent BigInteger quotient/remainder");
                var copy=new MasterClock(profile);copy.Reset();copy.Restore(clock.Capture());clock.AdvanceTo(clock.Now+17);copy.AdvanceTo(copy.Now+17);
                if(JsonSerializer.Serialize(clock.Capture())!=JsonSerializer.Serialize(copy.Capture()))throw new Exception("Clock remainder restore diverges");clockCases++;
            }
        }
        File.WriteAllText(Path.Combine(directory,"clock-result.json"),JsonSerializer.Serialize(new{clockCases,oracle="Exact BigInteger, original unreduced remainder domain; includes wide Int128 fallback"}));
        File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{cases,results},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"RunFrame horizons: {cases} exact state/trace/stereo cases");return 0;
    }
}
