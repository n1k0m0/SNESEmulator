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
// Checks rational remote clock conversion, timeline discontinuities and bounded drift.
// Assertions establish their stated test scope, not general physical SNES certification.
//

using SNESEmulator.Network;

internal static class NetworkClockTests
{
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    private sealed record Result(NetworkPlayout.DropCounters Drops,long PaddingAt50,long PaddingAtEnd,double AgeAt50,double AgeAtEnd,int MaximumBuffer,int MaximumPending,double MaximumAv,double MaximumCorrectionPpm);
    private static Result Simulate(double rate,bool adaptive,int gap)
    {
        var buffer=new NetworkAudioBuffer(adaptive);double now=0,activeAt=-1;long rendered=0;int sent=0,pending=0;
        double? head=null;int maxBuffer=0,maxPending=0;double maxCorrection=0,age50=0,ageEnd=0;long padding50=0;
        using var p=new NetworkPlayout(()=>{buffer.Clear();activeAt=-1;rendered=0;head=null;},()=>head,()=>now,false,()=>activeAt>=0){CaptureDiagnostics=true};
        float[] output=new float[4800];byte[] pcm=new byte[640*4];
        for(int ms=0;ms<=400000;ms++){
            now=ms/1000d;if(ms%20==0)pending++;
            if(ms<60000||ms>=60000+gap)while(pending>0){pending--;int packet=sent++;long crystal=672+packet*640*768L;
                p.Enqueue(packet*.02,true,()=>{buffer.Write(pcm,crystal);if(activeAt<0){activeAt=now;buffer.Read(output,0,4800);buffer.Read(output,0,4800);rendered=4800;}});
                p.Enqueue((packet+1)*.02,false,()=>{});
            }
            if(activeAt>=0){long consumed=(long)((now-activeAt)*rate);while(rendered-consumed<=2400){buffer.Read(output,0,4800);rendered+=2400;}head=buffer.CrystalAtDriverFrame(consumed)/24576000d;}
            while(p.PumpOnce()){}
            if(ms%1000==0){var a=buffer.ReadDiagnostics();maxBuffer=Math.Max(maxBuffer,a.BufferedNativeFrames);maxPending=Math.Max(maxPending,p.Pending);maxCorrection=Math.Max(maxCorrection,Math.Abs(a.RateAdjustmentPpm));
                var last=p.DiagnosticSnapshot().LastOrDefault();double age=last==null?0:last.CommitAt-last.Arrival;
                if(ms==50000){padding50=a.PaddingFrames;age50=age;}ageEnd=age;
            }
        }
        return new(p.ReadDropCounters(),padding50,buffer.PaddingFrames,age50,ageEnd,maxBuffer,maxPending,p.MaximumSoftwareAvErrorSeconds,maxCorrection);
    }
    internal static void Run(Action<string,Action> test)
    {
        test("R6 real driver video clock supersedes earlier or later host deadlines",()=>{
            foreach(bool later in new[]{false,true}){
                double now=0;double? head=10;int video=0,audio=0;
                using var p=new NetworkPlayout(()=>{},()=>head,()=>now,false,()=>true){CaptureDiagnostics=true};
                p.Enqueue(10.2,true,()=>audio++);p.Enqueue(10.2,false,()=>video++);now=later?.06:.01;head=10.2;
                while(p.PumpOnce()){}
                Check(video==1&&audio==(later?1:0),"valid driver can admit video on either side of old wall due");
                Check(p.Dropped==0&&p.MaximumSoftwareAvErrorSeconds<1e-10,"unchanged true driver distance");
                if(!later){Check(p.DiagnosticSnapshot().Single().SchedulingError<0,"old host due remains a signed diagnostic, not clamped");now=.04;Check(p.PumpOnce()&&audio==1,"audio still waits for40ms wall due");}
            }
        });
        test("R6 original8ms admission40ms commit and missing padding clock boundaries",()=>{
            foreach(double source in new[]{.008,.008000001}){
                double now=0;using var p=new NetworkPlayout(()=>{},()=>0d,()=>now,false,()=>true);p.Enqueue(source,false,()=>{});
                Check(p.PumpOnce()==(source==.008),"8ms inclusive driver admission");
            }
            foreach(double head in new[]{-.04,.04}){
                double now=0;using var p=new NetworkPlayout(()=>{},()=>head,()=>now,false,()=>true);p.Enqueue(0,false,()=>{});now=.3;p.PumpOnce();Check(p.Dropped==1,"40ms commit is still rejected");
            }
            foreach(bool required in new[]{false,true}){
                double now=0;int shown=0;using var p=new NetworkPlayout(()=>{},clock:()=>now,automatic:false,needsAudio:()=>required);p.Enqueue(0,false,()=>shown++);now=.039;Check(!p.PumpOnce(),"clockless wall due retained");now=.04;
                if(required){Check(!p.PumpOnce(),"unknown padding clock waits original250ms");now=.25;Check(p.PumpOnce()&&p.Dropped==1,"missing audio remains a counted drop");}
                else Check(p.PumpOnce()&&shown==1,"video-only clockless fallback remains40ms");
            }
        });
        foreach(double rate in new[]{47976d,48024d})test($"R6 presentation controls driver {(rate/48000-1)*1000000:+0;-0}ppm without permanent video loss",()=>{
            var old=Simulate(rate,false,0);var adjusted=Simulate(rate,true,0);
            if(rate<48000)Check(old.Drops.DistantVideo>4000&&old.Drops.DeliveryFailure>0,"fixed slower driver must retain the real persistent-distance/overflow defect");
            else Check(old.PaddingAtEnd>old.PaddingAt50+7000,"fixed faster driver continually needs source-less padding");
            Check(adjusted.Drops.Total==0&&adjusted.PaddingAtEnd==adjusted.PaddingAt50,"controller sustains video and stops accumulating padding after warmup");
            Check(adjusted.MaximumBuffer<NetworkAudioBuffer.NativeCapacity&&adjusted.MaximumPending<NetworkPlayout.Capacity,"original budgets");
            Check(adjusted.MaximumAv<.04&&adjusted.MaximumCorrectionPpm<=5000.000001,"original AV bound and explicit correction budget");
        });
        test("R6 one139ms transport underflow recovers at identical nominal clocks",()=>{
            var old=Simulate(48000,false,139);var adjusted=Simulate(48000,true,139);
            Check(old.AgeAtEnd>old.AgeAt50+.03&&old.PaddingAtEnd>old.PaddingAt50,"fixed provider retains actual padding latency");
            Check(Math.Abs(adjusted.AgeAtEnd-adjusted.AgeAt50)<.01&&adjusted.PaddingAtEnd>adjusted.PaddingAt50,"actual underflow is counted while queue latency recovers");
            Check(adjusted.Drops.Total==0&&adjusted.MaximumAv<.04,"recovery preserves original playout policy");
        });
        test("R6 nominal adaptive output is legacy exact and changed rates map actual interpolated stereo",()=>{
            byte[] Make(int count){var pcm=new byte[count*4];for(int i=0;i<count;i++){System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4),(short)i);System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4+2),(short)-i);}return pcm;}
            var old=new NetworkAudioBuffer();var adaptive=new NetworkAudioBuffer(true);byte[] pcm=Make(2240);old.Write(pcm,672);adaptive.Write(pcm,672);float[] a=new float[4800],b=new float[4800];old.Read(a,0,a.Length);adaptive.Read(b,0,b.Length);
            Check(a.SequenceEqual(b)&&adaptive.ReadDiagnostics().RateAdjustmentPpm==0,"zero queue error retains exact3:2 waveform");
            for(int i=0;i<2400;i++)Check(adaptive.CrystalAtDriverFrame(i)==old.CrystalAtDriverFrame(i),"legacy exact source map");
            var changed=new NetworkAudioBuffer(true);changed.Write(Make(4000),672);changed.Read(b,0,b.Length);Check(changed.ReadDiagnostics().RateAdjustmentPpm>0,"actual nonzero adjustment");
            for(int i=0;i<2400;i++){double position=(changed.CrystalAtDriverFrame(i)!.Value-672)/768;Check(Math.Abs(b[i*2]*32768-position)<.001&&Math.Abs(b[i*2+1]*32768+position)<.001,"mapped coordinate equals the real interpolation and channel order");}
            changed.Read(b,0,b.Length);changed.Read(b,0,b.Length);Check(changed.PaddingFrames>0&&changed.CrystalAtDriverFrame(changed.RenderedFrames-1)==null,"padding never acquires a source timestamp");
            changed.Clear();Check(changed.RenderedFrames==0&&changed.MappingSegments==0&&changed.ReadDiagnostics().RateAdjustmentPpm==0,"reset clears actual maps/controller without concealing cumulative padding");
        });
        test("R6 opt-in durable diagnostic gate preserves first rejection signed coordinates and bounded cadence",()=>{
            double now=0;using var p=new NetworkPlayout(()=>{},()=>1d,()=>now,false){CaptureDiagnostics=true};
            var gate=new PlayoutDiagnosticExportGate(2,10);var exported=new List<NetworkPlayout.Observation>();p.DiagnosticObserver=row=>{if(gate.TryExport(row))exported.Add(row);};
            for(int i=0;i<1500;i++){now=i*.02;p.Enqueue(.9,false,()=>{});now+=.3;p.PumpOnce();}
            Check(exported.Count==2&&gate.CapacityOmitted>0&&gate.CadenceOmitted>0,"bounded export even at50Hz rejection");
            Check(exported[0].RejectionReason=="DistantVideo"&&Math.Abs(exported[0].SourceMinusCommitPlayheadSeconds!.Value+.1)<1e-12,"original signed rejected coordinate and reason");
            Check(exported[1].CommitAt-exported[0].CommitAt>=10,"periodic export interval");
            Check(gate.Exported+gate.CapacityOmitted+gate.CadenceOmitted==1500,"every rejection export decision accounted without changing drop counters");
            double clock=0;using var failed=new NetworkPlayout(()=>{},()=>1d,()=>clock,false){CaptureDiagnostics=true};
            failed.Enqueue(1,false,()=>throw new IOException("diagnostic delivery failure"));clock=.05;failed.PumpOnce();
            Check(failed.DiagnosticSnapshot().Single().RejectionReason=="DeliveryFailure","failed callback retains actual rejection reason");
        });
    }
}
