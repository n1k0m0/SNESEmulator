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
// Produces deterministic performance workloads with state/audio/video checkpoint identities.
//

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;

// Whole-console benchmark and byte-for-byte BEFORE/AFTER evidence, not an independent accuracy oracle.
static class Performance
{
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    static byte[] GeometryRom(){var r=new PpuDiagnosticRom();r.Write(0x2105,5);r.Write(0x2133,5);r.Write(0x212c,1);r.Write(0x212d,1);r.Palette(0,0,31,992,31744);byte[] tile=Enumerable.Repeat((byte)0xff,32).ToArray();r.Vram(0,tile);r.Write(0x2107,16);r.Vram(0x2000,new byte[2048]);return r.Finish();}
    public static int Run(string[] args)
    {
        string directory=args.ElementAtOrDefault(0)??throw new ArgumentException("performance DIRECTORY [FIELDS=120] [FILTER=all] [MODE=untraced]");
        int fields=int.Parse(args.ElementAtOrDefault(1)??"120");string filter=args.ElementAtOrDefault(2)??"all",mode=args.ElementAtOrDefault(3)??"untraced";
        if(fields<1||mode is not ("traced" or "untraced" or "devices"))throw new ArgumentException("Invalid fields/mode");Directory.CreateDirectory(directory);
        foreach(string name in new[]{"diagnostic-ntsc","audio-ntsc","geometry-ntsc","geometry-pal","game-ntsc","game-pal"})
        {
            if(filter!="all"&&filter!=name)continue;
            var profile=name.EndsWith("pal")?HardwareProfile.PalProfile:HardwareProfile.Ntsc;
            byte[] rom=name.StartsWith("game")?File.ReadAllBytes("testdata/m8-game/unnamed-snes-game.sfc"):name.StartsWith("audio")?AudioDiagnosticRom.Create():name.StartsWith("geometry")?GeometryRom():DiagnosticRom.Create();
            var m=new SnesSystem(profile);m.Insert(new Cartridge(rom));string prefix=Path.Combine(directory,name+"-"+mode);File.WriteAllBytes(prefix+".sfc",rom);
            using var traceBytes=new MemoryStream();using var trace=new BinaryWriter(traceBytes);long rows=0;
            void Row(string kind,object row){trace.Write(kind);trace.Write(m.Clock.Now);trace.Write(JsonSerializer.Serialize(row));rows++;}
            if(mode=="traced"){
                m.CyclePhaseObserved=e=>Row("cpu",e);m.Clock.EventObserved=e=>Row("event",e);m.Bus.Dma.Observed=e=>Row("dma",e);
                m.Bus.AccessObserved=(d,a,v)=>Row("bus",new{d,a,v});
                // Hash all observer epochs without retaining unbounded traces.
            }
            if(mode=="devices"){
                // Real observer path, compact records at every committed device epoch.
                m.Bus.Apu.PhaseObserved=e=>{trace.Write((byte)1);trace.Write(m.Clock.Now);trace.Write(e.Crystal);trace.Write(e.Phase);trace.Write(e.Cycle.Address??-1);trace.Write(e.Cycle.Value??0);trace.Write(e.Cycle.Direction);rows++;};
                m.Bus.Apu.DspPhaseObserved=e=>{trace.Write((byte)2);trace.Write(m.Clock.Now);trace.Write(e.Crystal);trace.Write(e.Phase);trace.Write(e.Sample);trace.Write(JsonSerializer.Serialize(e.Memory));trace.Write(JsonSerializer.Serialize(e.VoiceOperations));rows++;};
                m.Bus.Ppu.PhaseObserved=e=>{trace.Write((byte)3);trace.Write(e.MasterClock);trace.Write(e.H);trace.Write(e.V);trace.Write(e.Phase);trace.Write(e.Index);rows++;};
            }
            using var traceHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var videoHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            bool stream=args.ElementAtOrDefault(4)=="stream";
            using Stream pcmStream=stream?new MemoryStream():File.Create(prefix+".pcm.bin");using var pcm=new BinaryWriter(pcmStream);using var pcmHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void CommitPcm(){if(stream){pcm.Flush();var memory=(MemoryStream)pcmStream;pcmHash.AppendData(memory.GetBuffer().AsSpan(0,(int)memory.Length));memory.SetLength(0);}}
            var checkpoints=new List<object>();var perField=new List<object>();long managedStart=0,managedMax=0;int largestDrain=0;var phases=new Dictionary<string,(int Fields,double Seconds,long Clocks)>();double seconds=0;long allocated=0,clocks=0,samples=0;int[] collections=new int[3];
            const int warmup=10;int count=name.StartsWith("game")?Math.Max(fields,320):fields;
            // Same replay as room sequence: actual controller inputs at raw field indices.
            var replay= name.StartsWith("game")? JsonSerializer.Deserialize<Replay[]>(File.ReadAllText("testdata/m9-performance-replay.json"),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??[]:[];
            for(int field=0;field<warmup+count;field++){
                foreach(var e in replay.Where(e=>e.Frame==field))m.Bus.Controllers.Buttons[e.Port]=e.Buttons;
                if(field==warmup)managedStart=GC.GetTotalMemory(false);
                long start=m.Clock.Now,bytes=GC.GetAllocatedBytesForCurrentThread();int[] gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();long timer=Stopwatch.GetTimestamp();
                m.RunFrame();var audio=m.Bus.Apu.Dsp.DrainSamples();
                double elapsed=Stopwatch.GetElapsedTime(timer).TotalSeconds;
                largestDrain=Math.Max(largestDrain,audio.Length);
                if(field>=warmup){perField.Add(new{field,masterClocks=m.Clock.Now-start,seconds=elapsed,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-bytes});managedMax=Math.Max(managedMax,GC.GetTotalMemory(false));string phase=name.StartsWith("game")?(field<150?"fields10-149":field<210?"fields150-209":field<300?"fields210-299":"fields300-plus"):"steady";var prior=phases.GetValueOrDefault(phase);phases[phase]=(prior.Fields+1,prior.Seconds+elapsed,prior.Clocks+m.Clock.Now-start);seconds+=elapsed;allocated+=GC.GetAllocatedBytesForCurrentThread()-bytes;clocks+=m.Clock.Now-start;for(int g=0;g<3;g++)collections[g]+=GC.CollectionCount(g)-gc[g];}
                foreach(var s in audio){pcm.Write(s.Index);pcm.Write(s.Crystal);pcm.Write(s.Left);pcm.Write(s.Right);samples++;}
                CommitPcm();
                var v=m.Bus.Ppu.CompletedFrame!;videoHash.AppendData(JsonSerializer.SerializeToUtf8Bytes(v.Geometry));videoHash.AppendData(MemoryMarshal.AsBytes(v.Native.Span));videoHash.AppendData(v.Brightness.Span);videoHash.AppendData(MemoryMarshal.AsBytes(v.Argb.Span));
                traceHash.AppendData(traceBytes.GetBuffer().AsSpan(0,(int)traceBytes.Length));traceBytes.SetLength(0);
                if(field is 0 or 9||field==warmup+count-1){string state=prefix+".field"+field+".state";StateSerializer.Save(m,state);checkpoints.Add(new{field,clock=m.Clock.Now,stateSha256=Hash(File.ReadAllBytes(state))});}
            }
            // Deliberately end within a field, exercising in-flight serialization/cache reconstruction.
            m.RunUntil(m.Clock.Now+12345);var tail=m.Bus.Apu.Dsp.DrainSamples();foreach(var s in tail){pcm.Write(s.Index);pcm.Write(s.Crystal);pcm.Write(s.Left);pcm.Write(s.Right);samples++;}CommitPcm();pcm.Dispose();
            traceHash.AppendData(traceBytes.GetBuffer().AsSpan(0,(int)traceBytes.Length));
            StateSerializer.Save(m,prefix+".state");File.WriteAllBytes(prefix+".sram.bin",m.Bus.Cartridge!.Sram);
            var result=new{name,mode,profile,coreAssemblySha256=Hash(File.ReadAllBytes(typeof(SnesSystem).Assembly.Location)),firmwareSha256=m.Bus.Apu.FirmwareHash,perField,managedStart,managedMax,managedEnd=GC.GetTotalMemory(false),largestDrain,presentationCapacity=SnesDsp.PresentationCapacity,pcmStorage=stream?"bounded per-field memory stream hashed then drained":"file",pcmFormat="20-byte little-endian records: Index64,Crystal64,Left16,Right16",phaseMetrics=phases.ToDictionary(p=>p.Key,p=>new{fields=p.Value.Fields,seconds=p.Value.Seconds,masterClocks=p.Value.Clocks,realtimeRatio=profile.ClockSeconds(p.Value.Clocks)/p.Value.Seconds}),romSha256=Hash(rom),replaySha256=name.StartsWith("game")?Hash(File.ReadAllBytes("testdata/m9-performance-replay.json")):null,warmupFields=warmup,measuredFields=count,masterClocks=clocks,emulatedSeconds=profile.ClockSeconds(clocks),elapsedSeconds=seconds,realtimeRatio=profile.ClockSeconds(clocks)/seconds,fieldsPerSecond=count/seconds,allocatedBytes=allocated,allocatedBytesPerField=(double)allocated/count,gcCollections=collections,host=new{Environment.MachineName,Environment.ProcessorCount,runtime=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription},activeApu=!m.Bus.Apu.Isolated,dspSamples=samples,presentationDrops=m.Bus.Apu.Dsp.DroppedPresentationSamples,videoSha256=Convert.ToHexStringLower(videoHash.GetHashAndReset()),traceRows=rows,traceSha256=Convert.ToHexStringLower(traceHash.GetHashAndReset()),pcmSha256=stream?Convert.ToHexStringLower(pcmHash.GetHashAndReset()):Hash(File.ReadAllBytes(prefix+".pcm.bin")),stateSha256=Hash(File.ReadAllBytes(prefix+".state")),sramSha256=Hash(m.Bus.Cartridge.Sram),checkpoints};
            File.WriteAllText(prefix+".json",JsonSerializer.Serialize(result,Json));Console.WriteLine($"{name} {mode}: {count/seconds:F2} fields/s, realtime {profile.ClockSeconds(clocks)/seconds:F3}x, {allocated/count} B/field, drops {m.Bus.Apu.Dsp.DroppedPresentationSamples}");
            if(m.Bus.Apu.Dsp.DroppedPresentationSamples!=0)throw new Exception("Presentation overflow");
        }
        return 0;
    }
    sealed record Replay(long Frame,int Port,ushort Buttons);
}
