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
// Checks external snapshot continuation, title audio, gameplay inputs and SRAM identity.
// Assertions establish their stated test scope, not general physical SNES certification.
//

using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;

static class CompatibilityTests
{
    public static List<object> Evidence {get;}=[];
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static byte[] State(SnesSystem m,string path){StateSerializer.Save(m,path);return File.ReadAllBytes(path);}
    public static void Run(Action<string,Action> test,string directory)
    {
        Directory.CreateDirectory("artifacts/m8-internal");
        File.WriteAllBytes("artifacts/m8-internal/audio.sfc",AudioDiagnosticRom.Create());
        using var manifest=JsonDocument.Parse(File.ReadAllText("testdata/m8-roms/manifest.json"));
        foreach(var item in manifest.RootElement.GetProperty("titles").EnumerateArray())
        {
            string id=item.GetProperty("id").GetString()!,path=item.GetProperty("path").GetString()!,sha=item.GetProperty("sha256").GetString()!;
            test("M8 external state continuation "+id,()=>
            {
                byte[] rom=File.ReadAllBytes(path);Check(Convert.ToHexStringLower(SHA256.HashData(rom))==sha,"External ROM hash");
                SnesSystem Machine(){var m=new SnesSystem();m.Insert(new Cartridge(rom));return m;}
                // The reference is never saved or loaded before it reaches its endpoint.
                var reference=Machine();for(int f=0;f<6;f++){reference.Bus.Controllers.Buttons[0]=(ushort)(f==3?SnesButtons.A:0);reference.RunFrame();}
                byte[] expected=State(reference,Path.Combine(directory,"m8-reference"));
                var observed=Machine();for(int f=0;f<3;f++)observed.RunFrame();
                string save=Path.Combine(directory,"m8-snapshot");State(observed,save);
                var restored=StateSerializer.Load(observed.Bus.Cartridge!,save);
                for(int f=3;f<6;f++){restored.Bus.Controllers.Buttons[0]=(ushort)(f==3?SnesButtons.A:0);restored.RunFrame();}
                Check(State(restored,Path.Combine(directory,"m8-restored")).SequenceEqual(expected),"Complete running state differs from untouched reference");
                Evidence.Add(new{id,sha256=sha,frames=6,state="pass",input="frame3 A down, frame4 released; application consumption not asserted",sram=reference.Bus.Cartridge!.Sram.Length==0?"not applicable":"not independently validated"});
            });
        }
        test("M8 internal sounding console state and stereo tail",()=>
        {
            SnesSystem Machine(){var m=new SnesSystem();m.Insert(new Cartridge(AudioDiagnosticRom.Create()));return m;}
            var reference=Machine();for(int i=0;i<8;i++)reference.RunFrame();
            var samples=reference.Bus.Apu.Dsp.DrainSamples();Check(samples.Any(s=>s.Left!=0||s.Right!=0),"Actual DSP output must sound");
            byte[] expected=State(reference,Path.Combine(directory,"m8-audio-reference"));
            var m=Machine();for(int i=0;i<3;i++)m.RunFrame();long start=m.Bus.Apu.Dsp.SampleCount;
            string file=Path.Combine(directory,"m8-audio-state");State(m,file);
            var copy=StateSerializer.Load(m.Bus.Cartridge!,file);for(int i=3;i<8;i++)copy.RunFrame();
            Check(copy.Bus.Apu.Dsp.DrainSamples().SequenceEqual(samples.Where(s=>s.Index>=start)),"Native stereo tail differs");
            Check(State(copy,Path.Combine(directory,"m8-audio-restored")).SequenceEqual(expected),"Sounding full state differs");
            Evidence.Add(new{id="internal-audio",internalTest=true,romSha256=Convert.ToHexStringLower(SHA256.HashData(AudioDiagnosticRom.Create())),frames=8,samples=samples.Length,nonzero=samples.Count(s=>s.Left!=0||s.Right!=0),state="pass",sram="not applicable"});
        });
        test("M8 external HiROM game input SRAM and sounding state",()=>
        {
            byte[] rom=File.ReadAllBytes("testdata/m8-game/unnamed-snes-game.sfc");
            using var source=JsonDocument.Parse(File.ReadAllText("testdata/m8-game/manifest.json"));
            Check(Convert.ToHexStringLower(SHA256.HashData(rom))==source.RootElement.GetProperty("sha256").GetString(),"Game identity");
            SnesSystem Machine(){var m=new SnesSystem();m.Insert(new Cartridge(rom));Check(m.Bus.Cartridge!.Mapping==CartridgeMapping.HiRom,"External HiROM");return m;}
            ushort Input(int f)=>(ushort)(f is >=150 and <152?SnesButtons.Start:f is >=210 and <212?SnesButtons.B:f is >=270 and <290?SnesButtons.Right:0);
            var reference=Machine();var audio=new List<StereoSample>();
            for(int f=0;f<305;f++){reference.Bus.Controllers.Buttons[0]=Input(f);reference.RunFrame();var tail=reference.Bus.Apu.Dsp.DrainSamples();if(f>=300)audio.AddRange(tail);}
            // The unchanged dungeons.json explicitly selects song "blank" in
            // this room. Sounding title playback is tested separately below.
            Check(audio.All(s=>s.Left==0&&s.Right==0),"Source-declared blank-room audio");
            byte[] expected=State(reference,Path.Combine(directory,"m8-game-reference"));
            Check(reference.Bus.Cartridge!.Sram.Length==4096&&reference.Bus.Cartridge.Sram.Any(v=>v!=0),"Actual CPU-created battery save");
            byte[] battery=reference.Bus.Cartridge.Sram.ToArray();string batteryPath=Path.Combine(directory,"external-game.srm");File.WriteAllBytes(batteryPath,battery);Check(File.ReadAllBytes(batteryPath).SequenceEqual(battery),"Raw battery file roundtrip");
            var m=Machine();for(int f=0;f<300;f++){m.Bus.Controllers.Buttons[0]=Input(f);m.RunFrame();m.Bus.Apu.Dsp.DrainSamples();}
            string save=Path.Combine(directory,"m8-game-state");State(m,save);var restored=StateSerializer.Load(m.Bus.Cartridge!,save);
            for(int f=300;f<305;f++){restored.Bus.Controllers.Buttons[0]=Input(f);restored.RunFrame();}
            Check(restored.Bus.Apu.Dsp.DrainSamples().SequenceEqual(audio),"External game stereo continuation");
            Check(State(restored,Path.Combine(directory,"m8-game-restored")).SequenceEqual(expected),"External game complete state continuation");
            Evidence.Add(new{id="unnamed-snes-game",internalTest=false,frames=305,state="pass",audioPairs=audio.Count,sramBytes=battery.Length,sramSha256=Convert.ToHexStringLower(SHA256.HashData(battery)),sram="CPU-created bytes and raw file roundtrip; frontend battery UI is M9"});
        });
        test("M8 external game sounding title state continuation",()=>
        {
            byte[] rom=File.ReadAllBytes("testdata/m8-game/unnamed-snes-game.sfc");
            SnesSystem Machine(){var m=new SnesSystem();m.Insert(new Cartridge(rom));return m;}
            var reference=Machine();var audio=new List<StereoSample>();for(int f=0;f<135;f++){reference.RunFrame();var samples=reference.Bus.Apu.Dsp.DrainSamples();if(f>=130)audio.AddRange(samples);}
            Check(audio.Any(s=>s.Left!=0||s.Right!=0),"Unmodified external title must sound");byte[] expected=State(reference,Path.Combine(directory,"m8-game-title-reference"));
            var m=Machine();for(int f=0;f<130;f++){m.RunFrame();m.Bus.Apu.Dsp.DrainSamples();}string save=Path.Combine(directory,"m8-game-title-state");State(m,save);var restored=StateSerializer.Load(m.Bus.Cartridge!,save);
            for(int f=130;f<135;f++)restored.RunFrame();Check(restored.Bus.Apu.Dsp.DrainSamples().SequenceEqual(audio),"External sounding stereo tail");Check(State(restored,Path.Combine(directory,"m8-game-title-restored")).SequenceEqual(expected),"External sounding complete state");
            Evidence.Add(new{id="unnamed-snes-game-title",frames=135,audioPairs=audio.Count,nonzero=audio.Count(s=>s.Left!=0||s.Right!=0),state="pass"});
        });
        test("M8 external WAI IRQ MMIO deadlines",()=>
        {
            var m=new SnesSystem();m.Insert(new Cartridge(File.ReadAllBytes("testdata/m8-roms/inidisp_brightness_delay.sfc")));
            var actual=new List<(int V,int H,byte Value)>();m.Bus.AccessObserved=(kind,address,value)=>{if(kind=="write"&&(address&0x40ffff)==0x2100&&m.Frames==23)actual.Add((m.Clock.V,m.Clock.H,value));};
            for(int i=0;i<24;i++)m.RunFrame();
            // Independently executed bsnes AND ares traces at the pinned ROM
            // have these exact bus endpoints. Old WAI event-jumping wrote H388.
            foreach(var expected in new[]{(8,366,(byte)15),(88,392,(byte)0),(104,366,(byte)15),(184,392,(byte)0),(200,366,(byte)15)})Check(actual.Contains(expected),"Missing independent MMIO endpoint "+expected);
        });
        test("M8 WAI IRQ every-clock saved idle phase",()=>
        {
            byte[] rom=File.ReadAllBytes("testdata/m8-roms/inidisp_brightness_delay.sfc");
            SnesSystem Machine(){var m=new SnesSystem();m.Insert(new Cartridge(rom));return m;}
            long begin=1364L*88+1228,end=1364L*89+500;
            var reference=Machine();reference.RunUntil(end);byte[] expected=State(reference,Path.Combine(directory,"m8-wai-reference"));
            for(int offset=0;offset<16;offset++)
            {
                var m=Machine();m.RunUntil(begin+offset);string file=Path.Combine(directory,"m8-wai-state");State(m,file);var restored=StateSerializer.Load(m.Bus.Cartridge!,file);restored.RunUntil(end);Check(State(restored,Path.Combine(directory,"m8-wai-restored")).SequenceEqual(expected),"WAI residual masterclock "+offset);
            }
        });
        test("M8 endpoint and pending field H0 observer snapshots",()=>
        {
            foreach(bool tie in new[]{false,true})
            {
                HardwareProfile profile=tie?HardwareProfile.Ntsc with{Id="m8-24-crystal-master-tie-test",ApuHzNumerator=HardwareProfile.Ntsc.MasterHzNumerator*24,ApuHzDenominator=HardwareProfile.Ntsc.MasterHzDenominator}:HardwareProfile.Ntsc;
                SnesSystem Machine(){var m=new SnesSystem(profile);m.Insert(new Cartridge(DiagnosticRom.Create()));return m;}
                long end=profile.FieldMasterClocks(false,false)+100;
                var reference=Machine();reference.RunUntil(end);var referenceAudio=reference.Bus.Apu.Dsp.DrainSamples();byte[] expected=State(reference,Path.Combine(directory,"m8-endpoint-reference"));
                var m=Machine();var paths=new List<(string Path,long Sample)>();var counts=new Dictionary<string,int>();
                void Capture(string kind,int maximum=1){int count=counts.GetValueOrDefault(kind);if(count>=maximum)return;counts[kind]=count+1;string file=Path.Combine(directory,"m8-endpoint-"+paths.Count);State(m,file);paths.Add((file,m.Bus.Apu.Dsp.SampleCount));}
                m.Clock.EventObserved=_=>{if(m.Frames==1&&m.Clock.V==0&&m.Clock.H==0)Capture("typed-pending-H0");};
                m.Bus.Apu.DspPhaseObserved=_=>{if(m.Frames==1&&m.Clock.V==0&&m.Clock.H==0)Capture("apu-pending-H0");};
                m.CyclePhaseObserved=p=>{if(p.Phase=="write-latch")Capture("cpu-before-ppu",4);};
                m.Bus.Dma.Observed=_=>Capture("dma-before-ppu",4);
                m.Bus.Ppu.PhaseObserved=p=>{if(m.Frames==1&&p.V==0&&p.H==0)Capture("ppu-committed-H0");};
                m.RunUntil(end);
                foreach(string kind in new[]{"typed-pending-H0","cpu-before-ppu","dma-before-ppu","ppu-committed-H0"})Check(counts.GetValueOrDefault(kind)>0,"Observer not exercised: "+kind);
                if(tie)Check(counts.GetValueOrDefault("apu-pending-H0")>0,"Exact APU field-boundary tie not exercised");
                foreach(var (file,sample) in paths){var restored=StateSerializer.Load(m.Bus.Cartridge!,file);restored.RunUntil(end);Check(State(restored,file+"-tail").SequenceEqual(expected),"Endpoint continuation "+file);Check(restored.Bus.Apu.Dsp.DrainSamples().SequenceEqual(referenceAudio.Where(s=>s.Index>=sample)),"Endpoint native stereo "+file);}
                Evidence.Add(new{test="pending-H0-observers",synthetic=tie,counts,snapshots=paths.Count});
            }
        });
        test("M8 published measured oscillator parameters execute real IPL port flow",()=>
        {
            using var rates=JsonDocument.Parse(File.ReadAllText("testdata/m8-measured-rates.json"));
            foreach(var point in rates.RootElement.GetProperty("points").EnumerateArray())
            {
                string id=point.GetProperty("id").GetString()!;long numerator=point.GetProperty("rateNumerator").GetInt64()*768,denominator=point.GetProperty("rateDenominator").GetInt64();
                var profile=(point.GetProperty("pal").GetBoolean()?HardwareProfile.PalProfile:HardwareProfile.Ntsc) with{Id="m8-parameter-only-"+id,ApuHzNumerator=numerator,ApuHzDenominator=denominator};
                var m=new SnesSystem(profile);m.Insert(new Cartridge(SpcUploadRom.Create()));m.RunUntil(200000);
                Check(m.Bus.Wram[0x1ff0]==0xa5&&m.Bus.Apu.Ram[0x300]==0x5a&&m.Bus.Apu.ApuToCpu[1]==0xc3,"Actual CPU/IPL/SPC response "+id);
                long expected=(long)((Int128)200000*numerator*profile.MasterHzDenominator/((Int128)denominator*profile.MasterHzNumerator));
                Check(m.Bus.Apu.Crystal==expected&&m.Clock.ApuTicks==expected,"Measured-rate rational clock "+id);
                Check(m.Bus.Apu.Dsp.SampleCount==(expected/24+3)/32,"Measured-rate DAC count "+id);
                Evidence.Add(new{test="published-rate-parameter",id,profile,crystals=expected,samples=m.Bus.Apu.Dsp.SampleCount,ports="actual executed IPL upload and reply",hardwareReplay=false});
            }
        });
    }
}
