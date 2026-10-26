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
// Checks worker ownership, bounded commands, pacing deadlines and deterministic field handoff.
// Assertions establish their stated test scope, not general physical SNES certification.
//

using System.Security.Cryptography;
using SNESEmulator.Core;
using SNESEmulator.Runtime;
using SNESEmulator.SaveStates;

static class RuntimeTests
{
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static T Await<T>(Task<T> t)=>t.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    static void Await(Task t)=>t.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    static void Until(Func<bool> p,string why)=>Check(SpinWait.SpinUntil(p,TimeSpan.FromSeconds(10)),why);
    static string Hash(ReadOnlySpan<byte> b)=>Convert.ToHexString(SHA256.HashData(b));
    static byte[] GeometryRom()
    {
        var rom=DiagnosticRom.Create();
        byte[] entry=[0xA9,5,0x8D,0x33,0x21,0x4C,0,0x80];entry.CopyTo(rom,0x2000);rom[0x7FFC]=0;rom[0x7FFD]=0xA0;
        for(int i=0;i<100;i++)if(rom[i]==0xA9&&rom[i+1]==0&&rom[i+2]==0x8D&&rom[i+3]==5&&rom[i+4]==0x21){rom[i+1]=5;break;}
        rom[0x7FD8]=1;return rom;
    }
    static void Compare(SnesSystem direct,FieldOutput output,string why)
    {
        var f=direct.Bus.Ppu.CompletedFrame!;
        Check(output.Video.Geometry==f.Geometry,why+" geometry");
        Check(output.Video.Native.Span.SequenceEqual(f.Native.Span),why+" RGB555");
        Check(output.Video.Brightness.Span.SequenceEqual(f.Brightness.Span),why+" brightness");
        Check(output.Video.Argb.Span.SequenceEqual(f.Argb.Span),why+" ARGB");
        Check(output.Audio.Span.SequenceEqual(direct.Bus.Apu.Dsp.DrainSamples()),why+" timestamped stereo");
    }
    public static void Run(Action<string,Action> test,string root)
    {
        test("R7 monotonic waits preserve exact deadline arithmetic and interruption reasons",()=>{
            foreach(long now in new[]{long.MinValue,-1L,0L,1L,long.MaxValue}){
                Check(MonotonicClock.WaitTimeoutMilliseconds(long.MinValue,now,10000000)==0,"Past extreme deadline cannot wrap");
                Check(MonotonicClock.WaitTimeoutMilliseconds(now,now,10000000)==0,"Inclusive deadline no wait");
            }
            Check(MonotonicClock.WaitTimeoutMilliseconds(long.MaxValue,long.MinValue,10000000)==50,"Future extreme caps safely");
            Check(MonotonicClock.WaitTimeoutMilliseconds(1,0,10000000)==1&&MonotonicClock.WaitTimeoutMilliseconds(10001,0,10000000)==2,"Original ceil semantics");
            var clock=new MonotonicClock();using var changed=new AutoResetEvent(false);var wait=clock.WaitObserved(long.MinValue,changed,CancellationToken.None);
            Check(wait.Reason==WaitReturnReason.DeadlineReached&&wait.Start==wait.Wake,"Past deadlines return without waiting, no wall timing assertion");
            changed.Set();wait=clock.WaitObserved(long.MaxValue,changed,CancellationToken.None);Check(wait.Reason==WaitReturnReason.Changed&&wait.Wake<long.MaxValue,"Changed returns before deadline");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();wait=clock.WaitObserved(long.MaxValue,changed,cancelled.Token);Check(wait.Reason==WaitReturnReason.Cancelled,"Cancellation returns without throwing worker fault");
            using var signaled=new AutoResetEvent(true);using var idle=new AutoResetEvent(false);
            var a=Task.Run(()=>clock.WaitObserved(long.MaxValue,signaled,CancellationToken.None));var b=Task.Run(()=>clock.WaitObserved(long.MaxValue,idle,cancelled.Token));Task.WaitAll(a,b);
            Check(a.Result.Reason==WaitReturnReason.Changed&&b.Result.Reason==WaitReturnReason.Cancelled,"Shared clocks return independent caller observations");
        });
        test("R7 timing diagnostics preserve hardware replay and real work counters",()=>{
            using var worker=new EmulationWorker();var direct=new SnesSystem();byte[] rom=DiagnosticRom.Create();Await(worker.InsertAsync(rom));direct.Insert(new Cartridge(rom));worker.EnableTimingDiagnostics();
            for(int i=0;i<4;i++){long instructions=direct.Cpu.Instructions;direct.RunFrame();var output=Await(worker.StepAsync(default));Compare(direct,output,"Observed owner");var point=worker.ReadTimingDiagnostics()!.Recent.Last(x=>x.Kind==OwnerTimingKind.Field);
                Check(point.Work.MasterClocks==output.Video.Geometry.EndClock-output.InputMasterClock&&point.Work.CpuInstructions==direct.Cpu.Instructions-instructions,"Work counters are actual immutable field deltas");
                Check(point.Work.Width==output.Video.Geometry.Width&&point.ThreadAfter.User100ns>=point.ThreadBefore.User100ns,"Geometry and monotone thread counters");
            }
            Check(StateSerializer.Capture(direct).SequenceEqual(Await(worker.CaptureAsync())),"Instrumentation changes no hardware bytes");
        });
        test("R7 bounded owner timing retains exact first steady catchup and detached context",()=>{
            var recorder=new OwnerTimingRecorder(1000);recorder.MarkReady(100);
            OwnerTimingPoint Point(long at,bool catchup=false)=>new(0,OwnerTimingKind.Field,at,at,at,at+5,at,at+4,at+4,catchup,default,false,0,null,1,2,3,4);
            recorder.Add(Point(99,true));recorder.Add(Point(101,true));
            for(int i=0;i<3000;i++)recorder.Add(Point(102+i,true));
            var snapshot=recorder.Snapshot();Check(snapshot.Total==3002&&snapshot.Overwritten==3002-OwnerTimingRecorder.Capacity,"bounded detail never conceals total records");
            Check(snapshot.FirstCatchup!.Value.Start==99&&snapshot.FirstSteadyCatchup!.Value.Start==101,"first actual point remains independent of cold start and overwritten ring");
            Check(snapshot.FirstSteadyContext.Last().Start==101&&snapshot.FirstSteadyContext.First().Start==99,"original bounded preceding context");snapshot.Recent[0]=default;
            Check(recorder.Snapshot().Recent[0]!=default,"reader cannot mutate retained owner points");
        });
        test("M10c worker SA1 direct pixel PCM state replay and immutable board load",()=>{
            var rom=SNESEmulator.Testing.Sa1DiagnosticRom.Create();var config=new Sa1Configuration();
            using var w=new EmulationWorker();Await(w.InsertAsync(rom,hardware:config));var direct=new SnesSystem();direct.Insert(new Cartridge(rom,hardware:config));
            for(int i=0;i<3;i++){direct.RunFrame();var output=Await(w.StepAsync(default));Compare(direct,output,"SA1 worker");Check(StateSerializer.Capture(direct).SequenceEqual(Await(w.CaptureAsync())),"SA1 full processor/shared RAM/pending state");}
            var raw=Await(w.CaptureAsync());string file=Path.Combine(root,"sa1-worker.snessav");StateSerializer.SaveCaptured(raw,file,direct.Bus.Cartridge!);Await(w.ResetAsync());Await(w.LoadAsync(File.ReadAllBytes(file)));Check(raw.SequenceEqual(Await(w.CaptureAsync())),"SA1 isolated load");Await(w.PowerCycleAsync());Check(w.Status.MasterClock==0,"SA1 power epoch");
        });
        test("M10b worker SuperFX direct replay and immutable board load",()=>{
            var rom=SNESEmulator.Testing.SuperFxDiagnosticRom.Create();var config=new SuperFxConfiguration(SuperFxRevision.Gsu2);
            using var w=new EmulationWorker();Await(w.InsertAsync(rom,hardware:config));var direct=new SnesSystem();direct.Insert(new Cartridge(rom,hardware:config));
            for(int i=0;i<3;i++){direct.RunFrame();var output=Await(w.StepAsync(default));Compare(direct,output,"SuperFX worker");Check(StateSerializer.Capture(direct).SequenceEqual(Await(w.CaptureAsync())),"SuperFX complete phase/RAM state");}
            var raw=Await(w.CaptureAsync());string file=Path.Combine(root,"superfx-worker.snessav");StateSerializer.SaveCaptured(raw,file,direct.Bus.Cartridge!);Await(w.ResetAsync());Await(w.LoadAsync(File.ReadAllBytes(file)));Check(raw.SequenceEqual(Await(w.CaptureAsync())),"SuperFX isolated load");
        });
        test("M10a worker DSP1 format10 isolated load reset and direct replay",()=>{
            var rom=SNESEmulator.Testing.Dsp1DiagnosticRom.Create(Dsp1Board.LoRom1MiB);
            var config=Dsp1Configuration.SyntheticProgram(SNESEmulator.Testing.Dsp1DiagnosticRom.Firmware(),Dsp1Board.LoRom1MiB);
            using var w=new EmulationWorker();Await(w.InsertAsync(rom,hardware:config));
            var direct=new SnesSystem();direct.Insert(new Cartridge(rom,hardware:config));
            for(int i=0;i<3;i++){direct.RunFrame();var output=Await(w.StepAsync(default));Compare(direct,output,"DSP1 worker");Check(StateSerializer.Capture(direct).SequenceEqual(Await(w.CaptureAsync())),"DSP1 complete state");}
            var raw=Await(w.CaptureAsync());string path=Path.Combine(root,"dsp1-worker.snessav");StateSerializer.SaveCaptured(raw,path,direct.Bus.Cartridge!);
            Await(w.ResetAsync());Await(w.LoadAsync(File.ReadAllBytes(path)));Check(Await(w.CaptureAsync()).SequenceEqual(raw),"DSP1 isolated load retains chip state");
            Await(w.PowerCycleAsync());Check(w.Status.MasterClock==0,"DSP1 cold reset");
        });
        foreach(var (profile,rom,name) in new[]{(HardwareProfile.Ntsc,DiagnosticRom.Create(),"NTSC"),(HardwareProfile.PalProfile,DiagnosticRom.Create(),"PAL"),(HardwareProfile.Ntsc,GeometryRom(),"NTSC hires interlace239"),(HardwareProfile.PalProfile,GeometryRom(),"PAL hires interlace239"),(HardwareProfile.Ntsc,AudioDiagnosticRom.Create(),"native stereo")})
        test("M9b direct versus worker complete hardware replay "+name,()=>{
            using var w=new EmulationWorker(profile);Await(w.InsertAsync(rom));var d=new SnesSystem(profile);d.Insert(new Cartridge(rom));
            var held=new List<(FieldOutput output,string native,string argb,string brightness)>();
            for(int i=0;i<40;i++)
            {
                var input=new FieldInput((ushort)(i%3==0?SnesButtons.Left:SnesButtons.Right),(ushort)(i%4==0?SnesButtons.B:SnesButtons.Start));
                long clock=d.Clock.Now;d.Bus.Controllers.Buttons[0]=input.Port1;d.Bus.Controllers.Buttons[1]=input.Port2;d.RunFrame();
                var output=Await(w.StepAsync(input));Check(output.Input==input&&output.InputMasterClock==clock&&output.InputSequence==i+1,"input application record");Compare(d,output,name+i);
                Check(StateSerializer.Capture(d).SequenceEqual(Await(w.CaptureAsync())),name+i+" exact hardware bytes");
                if(i<3)held.Add((output,Hash(System.Runtime.InteropServices.MemoryMarshal.AsBytes(output.Video.Native.Span)),Hash(System.Runtime.InteropServices.MemoryMarshal.AsBytes(output.Video.Argb.Span)),Hash(output.Video.Brightness.Span)));
                Check(w.Status.PendingOutput<=EmulationWorker.OutputCapacity,"bounded presentation");
            }
            foreach(var old in held){Check(Hash(System.Runtime.InteropServices.MemoryMarshal.AsBytes(old.output.Video.Native.Span))==old.native,"held native");Check(Hash(System.Runtime.InteropServices.MemoryMarshal.AsBytes(old.output.Video.Argb.Span))==old.argb,"held ARGB");Check(Hash(old.output.Video.Brightness.Span)==old.brightness,"held brightness");}
            Check(w.Status.DroppedPresentation>0,"slow consumer drops presentation only");Check(Await(w.BatteryAsync())!.Bytes.SequenceEqual(d.Bus.Cartridge!.Sram),"SRAM exact");
            if(name.Contains("hires"))Check(w.Latest!.Video.Geometry is{Width:512,Height:239,Interlace:true},"actual ROM geometry");
        });
        test("M9b load preserves format9 controller bytes and partial-field application",()=>{
            var rom=AudioDiagnosticRom.Create();var d=new SnesSystem();d.Insert(new Cartridge(rom));d.RunUntil(123456);d.Bus.Controllers.Buttons[0]=(ushort)SnesButtons.B;d.Bus.Controllers.Buttons[1]=(ushort)SnesButtons.R;
            byte[] raw=StateSerializer.Capture(d);string path=Path.Combine(root,"worker-partial.snessav");StateSerializer.SaveCaptured(raw,path,d.Bus.Cartridge!);
            // Off-thread compression can choose different Deflate block boundaries;
            // the complete hardware payload must remain byte-for-byte identical.
            using(var compressed=File.OpenRead(path))using(var zip=new System.IO.Compression.GZipStream(compressed,System.IO.Compression.CompressionMode.Decompress))using(var uncompressed=new MemoryStream()){zip.CopyTo(uncompressed);Check(raw.SequenceEqual(uncompressed.ToArray()),"captured compression preserves every hardware byte");}
            using var w=new EmulationWorker();Await(w.InsertAsync(rom));long epoch=w.Status.Epoch;Await(w.LoadAsync(File.ReadAllBytes(path)));
            Check(Await(w.CaptureAsync()).SequenceEqual(raw),"load/capture exact including held controller bytes");Check(w.Status.Epoch>epoch&&w.Latest==null&&w.Status.PendingOutput==0,"load clears presentation");
            d.Bus.Apu.Dsp.ClearPresentation();var input=new FieldInput((ushort)SnesButtons.A,0);d.Bus.Controllers.Buttons[0]=input.Port1;d.Bus.Controllers.Buttons[1]=0;long when=d.Clock.Now;d.RunFrame();
            var output=Await(w.StepAsync(input));Check(output.InputMasterClock==when&&when>output.Video.Geometry.StartClock,"actual partial field input timestamp");Compare(d,output,"partial state continuation");Check(Await(w.CaptureAsync()).SequenceEqual(StateSerializer.Capture(d)),"continued state exact");
            var stable=Await(w.CaptureAsync());try{Await(w.LoadAsync([1,2,3]));throw new Exception("accepted corrupt state");}catch(InvalidDataException){}
            Check(Await(w.CaptureAsync()).SequenceEqual(stable),"command failure retains valid machine");
        });
        test("M9b reset ROM replacement turbo epochs and canceled commands",()=>{
            using var w=new EmulationWorker();Await(w.InsertAsync(GeometryRom()));Await(w.StepAsync(new(0x8000,0)));long epoch=w.Status.Epoch;
            var held=w.Latest;Await(w.SetModeAsync(true,true));Check(w.Status.Epoch>epoch&&w.Status.Turbo&&ReferenceEquals(held,w.Latest),"pause/turbo retain owned image");
            var raw=Await(w.CaptureAsync());using var cancel=new CancellationTokenSource();cancel.Cancel();try{Await(w.ResetAsync(cancel.Token));throw new Exception("canceled reset executed");}catch(OperationCanceledException){}
            Check(Await(w.CaptureAsync()).SequenceEqual(raw),"canceled command cannot mutate");Await(w.ResetAsync());Check(w.Status.MasterClock==0&&w.Status.PendingOutput==0&&w.Latest==null,"reset origin clears old image");
            Await(w.InsertAsync(AudioDiagnosticRom.Create()));Check(w.Latest==null&&w.Status.Field==0&&w.Status.CartridgeHash==new Cartridge(AudioDiagnosticRom.Create()).Hash,"ROM identity");
            w.Dispose();Check(!w.IsAlive,"shutdown joined");try{Await(w.ResetAsync());throw new Exception("accepted disposed command");}catch(ObjectDisposedException){}
        });
        test("M9b same-ROM reload reads latest SRAM after flush",()=>{
            string pathFor(string hash)=>Path.Combine(root,"battery-reload",hash+".srm");Directory.CreateDirectory(Path.GetDirectoryName(pathFor("same"))!);File.WriteAllBytes(pathFor("same"),[1,2,3]);
            var bytes=Await(BatteryStorage.FlushAndReadAsync(()=>Task.FromResult<BatterySnapshot?>(new("same",[9,8,7])),pathFor,"same"));Check(bytes.SequenceEqual(new byte[]{9,8,7}),"same-ROM reload cannot restore stale disk SRAM");
            bytes=Await(BatteryStorage.FlushAndReadAsync(()=>Task.FromResult<BatterySnapshot?>(new("same",[6,5,4])),pathFor,"other"));Check(bytes.Length==0&&File.ReadAllBytes(pathFor("same")).SequenceEqual(new byte[]{6,5,4}),"different ROM still flushes previous cartridge");
        });
        test("M9b million-field rational pacing NTSC PAL and interlace",()=>{
            foreach(var p in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile})foreach(bool interlace in new[]{false,true})
            {
                var pacer=new FieldPacer();pacer.Rebase(12345);long sum=0,deadline=0;
                for(int i=0;i<1000000;i++){long duration=p.FieldMasterClocks((i&1)!=0,interlace);sum+=duration;deadline=pacer.Add(duration,p,10000000);}
                var exact=(System.Numerics.BigInteger)sum*p.MasterHzDenominator*10000000/p.MasterHzNumerator+12345;Check(deadline==(long)exact,"rational no accumulated rounding");
                pacer.Rebase(19);Check(pacer.Add(123,p,10000000,4)==19+(long)((Int128)123*p.MasterHzDenominator*10000000/(p.MasterHzNumerator*(Int128)4)),"turbo rational epoch");
            }
        });
        foreach(var profile in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile})
        foreach(long endLatenessTicks in new long[]{0,90000,100000,100001})
        test($"R2 actual worker end-deadline catchup {profile.Id} late={endLatenessTicks}us",()=>{
            // The clock stays fixed while hardware runs. The first WaitUntil is
            // a real owner boundary; advancing it then models a delayed second
            // field without sleeping or changing any host scheduling settings.
            var c=new ControlledClock();using var w=new EmulationWorker(profile,c);
            byte[] rom=DiagnosticRom.Create();var direct=new SnesSystem(profile);direct.Insert(new Cartridge(rom));
            direct.Bus.Controllers.Buttons[0]=0;direct.Bus.Controllers.Buttons[1]=0;
            Await(w.InsertAsync(rom));Await(w.SetModeAsync(false,false));
            Until(()=>w.Status.Field==1&&c.Waiting,"first rational boundary");
            Check(w.TryDequeue(out var first),"first owner output");direct.RunFrame();Compare(direct,first!,"initial paced field");
            var calibration=new SnesSystem(profile);calibration.Insert(new Cartridge(rom));calibration.RunFrame();calibration.RunFrame();
            long secondEndClock=calibration.Clock.Now;
            long secondEndDeadline=(long)((System.Numerics.BigInteger)secondEndClock*profile.MasterHzDenominator*c.Frequency/profile.MasterHzNumerator);
            long completion=secondEndDeadline+endLatenessTicks;c.Advance(completion);
            Until(()=>w.Status.Field>=2&&c.Waiting,"delayed field reaches held boundary");
            var outputs=new List<FieldOutput>();while(w.TryDequeue(out var output))outputs.Add(output!);
            Check(outputs.Count>0&&w.Status.DroppedPresentation==0,"controlled output must stay bounded and complete");
            var second=outputs[0];
            Check(second.Video.Geometry.EndClock==secondEndClock,"absolute second-field end differs from direct hardware");
            foreach(var output in outputs){direct.RunFrame();Compare(direct,output,"late field preserves hardware/video/PCM");}
            Check(Await(w.CaptureAsync()).SequenceEqual(StateSerializer.Capture(direct)),"late pacing preserves exact state");
            Check(second.CatchingUp==(endLatenessTicks>100000),$"End lateness {endLatenessTicks}us at completion {completion}, end deadline {secondEndDeadline}, actual CatchingUp={second.CatchingUp}; policy is strictly >100ms AFTER field end");
        });
        test("M9b controlled clock pause resume queue pressure and shutdown cancellation",()=>{
            var c=new ControlledClock();using var w=new EmulationWorker(clock:c);Await(w.InsertAsync(DiagnosticRom.Create()));Await(w.SetModeAsync(false,false));
            Until(()=>w.Status.Field==1&&c.Waiting,"first field independent of UI");Thread.Sleep(30);Check(w.Status.Field==1,"clock holds hardware");
            c.Advance(100000);Until(()=>w.Status.Field>=5&&c.Waiting,"catch up without UI frames");Await(w.SetModeAsync(true,false));long now=w.Status.MasterClock;c.Advance(10000000);Thread.Sleep(20);Check(w.Status.MasterClock==now,"pause stable");
            long before=w.Status.Field;Await(w.SetModeAsync(false,false));Until(()=>w.Status.Field==before+1&&c.Waiting,"resume rebases elapsed pause");
            c.Hold=true;c.Advance(1);Until(()=>c.Held,"owner held in cancelable clock wait");
            var captures=Enumerable.Range(0,EmulationWorker.CommandCapacity+5).Select(_=>w.CaptureAsync()).ToArray();Check(captures.Count(t=>t.IsFaulted)==5,"bounded command rejection");
            w.Dispose();Check(!w.IsAlive&&captures.All(t=>t.IsCompleted),"shutdown completes pending tasks");Check(captures.Take(EmulationWorker.CommandCapacity).All(t=>t.IsCanceled),"pending commands canceled without execution");
        });
        test("M9b paced live mailbox output replays at recorded input clocks",()=>{
            var c=new ControlledClock();using var w=new EmulationWorker(clock:c);var d=new SnesSystem();byte[] rom=DiagnosticRom.Create();d.Insert(new Cartridge(rom));Await(w.InsertAsync(rom));Await(w.SetModeAsync(false,false));
            long previousSequence=0;
            for(int i=0;i<6;i++)
            {
                Until(()=>w.Status.Field==i+1&&c.Waiting,"paced output boundary");Check(w.TryDequeue(out var output),"owned paced output");
                Check(output!.InputMasterClock==d.Clock.Now&&output.InputSequence>=previousSequence,"recorded monotone application");previousSequence=output.InputSequence;
                d.Bus.Controllers.Buttons[0]=output.Input.Port1;d.Bus.Controllers.Buttons[1]=output.Input.Port2;d.RunFrame();Compare(d,output,"paced input replay");
                if(i<5){w.SubmitInput(new((ushort)(i%2==0?SnesButtons.Left:SnesButtons.Right),(ushort)SnesButtons.B));c.Advance(17000);}
            }
            Await(w.SetModeAsync(true,false));Check(Await(w.CaptureAsync()).SequenceEqual(StateSerializer.Capture(d)),"paced replay final state exact");
        });
        test("M9b critical UI completions survive status bursts and bounded pressure",()=>{
            using var mailbox=new UiMailbox(2);int completed=0;
            Await(mailbox.PostAsync(()=>completed++));Await(mailbox.PostAsync(()=>completed++));
            var waiting=mailbox.PostAsync(()=>completed++);Check(!waiting.IsCompleted&&mailbox.Pending==2,"bounded backpressure");
            for(int i=0;i<10000;i++)mailbox.SetStatus("status"+i);
            Check(mailbox.TakeStatus()=="status9999","latest status coalesces");mailbox.Drain();Await(waiting);mailbox.Drain();Check(completed==3,"critical completion never dropped");
            using var cancel=new CancellationTokenSource();Await(mailbox.PostAsync(()=>completed++));Await(mailbox.PostAsync(()=>completed++));var canceled=mailbox.PostAsync(()=>completed++,cancel.Token);cancel.Cancel();try{Await(canceled);throw new Exception("canceled post ran");}catch(OperationCanceledException){}mailbox.Drain();Check(completed==5,"shutdown cancellation no stale completion");
        });
        test("Audit UI mailbox disposal releases blocked producers and discards stale callbacks",()=>{
            using var cancellation=new CancellationTokenSource();
            using var mailbox=new UiMailbox(1);int callbacks=0;
            Await(mailbox.PostAsync(()=>callbacks++));
            var blocked=mailbox.PostAsync(()=>callbacks++,cancellation.Token);
            Check(!blocked.IsCompleted,"producer is waiting for bounded capacity");
            mailbox.Dispose();
            try
            {
                Check(SpinWait.SpinUntil(()=>blocked.IsCompleted,TimeSpan.FromSeconds(1)),"dispose must finish blocked producers");
                try{Await(blocked);throw new Exception("accepted completion after disposal");}catch(ObjectDisposedException){}
                mailbox.Drain();Check(callbacks==0&&mailbox.Pending==0,"queued callbacks discarded on close");
                mailbox.SetStatus("late status");Check(mailbox.TakeStatus()==null,"late status ignored");
                try{Await(mailbox.PostAsync(()=>callbacks++));throw new Exception("accepted post after disposal");}catch(ObjectDisposedException){}
                mailbox.Dispose();
            }
            finally{cancellation.Cancel();}
        });
        test("Audit UI callbacks permit concurrent status reporting and reentrant close",()=>{
            using var mailbox=new UiMailbox(2);int callbacks=0;
            Await(mailbox.PostAsync(()=>{
                // Real close callbacks can wait for I/O whose failure reports status.
                // Holding the mailbox gate while invoking this action would deadlock.
                Await(Task.Run(()=>mailbox.SetStatus("background I/O completed")));
                Check(mailbox.TakeStatus()=="background I/O completed","status producer is not blocked by callback");
                callbacks++;mailbox.Dispose();
            }));
            Await(mailbox.PostAsync(()=>callbacks++));mailbox.Drain();
            Check(callbacks==1&&mailbox.Pending==0,"reentrant close discards the remaining callback");
        });
        test("M9b worker fault visibility and shutdown",()=>{
            var c=new ControlledClock{Throw=true};using var w=new EmulationWorker(clock:c);Await(w.InsertAsync(DiagnosticRom.Create()));Await(w.SetModeAsync(false,false));Until(()=>w.Status.Fault!=null,"clock failure reported");Check(w.Status.Paused,"fault pauses");Until(()=>!w.IsAlive,"faulted owner exits");try{Await(w.CaptureAsync());throw new Exception("dead owner accepted command");}catch(InvalidOperationException){}w.Dispose();Check(!w.IsAlive,"fault shutdown");
        });
        test("M9b concurrent input shutdown never signals disposed wait handles",()=>{
            using var w=new EmulationWorker();using var done=new CancellationTokenSource();
            var producer=Task.Run(()=>{while(!done.IsCancellationRequested)w.SubmitInput(new(0x8000,0));});
            Thread.Sleep(20);w.Dispose();Thread.Sleep(10);done.Cancel();Await(producer);Check(!w.IsAlive,"concurrent shutdown joined");
        });
        test("M9b synthetic mappings focus hotplug chords capture and release suppression",()=>{
            var bindings=GamepadMapping.Defaults();foreach(string name in GamepadMapping.Names){bindings[name]="Button29;Axis7-;Hat2Down";Check(GamepadMapping.Read(bindings,new HashSet<string>{"Axis7-"})!=0,"arbitrary mapping "+name);bindings[name]="";}
            Check(GamepadMapping.Names.Length==12,"all SNES buttons");var s=new GamepadSession();ushort chord=(ushort)(SnesButtons.Start|SnesButtons.Select);
            Check(s.Update(false,"a",chord,true,false,false)==default,"background chord ignored");Check(!s.Update(true,"a",chord,true,false,false).MainMenu,"focus held chord ignored");
            s.Update(true,"a",0,false,false,false);Check(s.Update(true,"a",chord,true,false,false).MainMenu,"new menu edge");Check(!s.Update(true,"a",chord,true,true,false).MainMenu,"held chord one edge");
            Check(s.Update(true,"a",(ushort)SnesButtons.A,true,false,false).GameButtons==0,"menu holds suppressed after close");s.Update(true,"a",0,false,false,false);Check(s.Update(true,"a",(ushort)SnesButtons.A,true,false,false).GameButtons==(ushort)SnesButtons.A,"release then gameplay");
            Check(s.Update(true,"b",chord,true,false,false)==default,"hotplug needs neutral");s.Update(true,"b",0,false,false,false);Check(!s.Update(true,"b",chord,true,false,true).MainMenu,"capture no actions");Check(s.Update(true,null,0,false,false,false)==default,"disconnect neutral");
            s.Update(true,"b",0,false,false,false);ushort saves=(ushort)(SnesButtons.L|SnesButtons.R|SnesButtons.Select);Check(s.Update(true,"b",saves,true,false,false).Saves,"save chord");s.Reset();Check(s.Update(true,"b",saves,true,false,false)==default,"mapping/port transition");
        });
        test("M9b independent presentation failures epochs rational APU guard and turbo",()=>{
            int video=0,audio=0,clears=0,errors=0;bool breakVideo=true,breakAudio=false;
            var h=new PresentationHandoff(_=>{video++;if(breakVideo)throw new IOException("video fault");},_=>{audio++;if(breakAudio)throw new IOException("audio fault");},()=>clears++,_=>errors++);
            var g=new PpuGeometry(256,224,256,false,StartClock:0,EndClock:100);FieldOutput o=new(1,new(g,new ushort[256*224],new byte[256*224],new uint[256*224]),new StereoSample[]{new(0,672,10,-10)},1,0,default,false);
            h.Present(o,1);h.Present(o,1);Check(video==1&&audio==2&&errors==1,"video failure cannot stop audio");breakVideo=false;breakAudio=true;h.Present(o with{Epoch=2},2);h.Present(o with{Epoch=2},2);Check(video==3&&audio==3&&errors==2,"audio failure cannot stop video");
            breakAudio=false;h.Present(o with{Epoch=3,Turbo=true},3);Check(audio==3,"turbo muted");h.Present(o with{Epoch=3,CatchingUp=true},3);Check(audio==3,"catchup audio muted without discarding native source samples");h.Present(o with{Epoch=3,Video=o.Video with{Geometry=g with{ApuHzNumerator=24606720}}},3);Check(audio==3,"32040Hz blocked");
            h.Present(o with{Epoch=3,Video=o.Video with{Geometry=g with{ApuHzNumerator=49152000,ApuHzDenominator=2}}},3);Check(audio==4,"equivalent unreduced rate accepted");h.Present(o,3);Check(audio==4,"old epoch ignored");Check(clears>=5,"presentation clears");
            using var w=new EmulationWorker(HardwareProfile.Ntsc with{ApuHzNumerator=24606720});Await(w.InsertAsync(AudioDiagnosticRom.Create()));var native=Await(w.StepAsync(default));Check(!w.Status.AudioSupported&&native.Audio.Length>0,"lab core retains native audio");
        });
    }
    sealed class ControlledClock : IMonotonicClock
    {
        long timestamp;readonly AutoResetEvent advanced=new(false);public long Timestamp=>Interlocked.Read(ref timestamp);public long Frequency=>1000000;public volatile bool Waiting,Hold,Held,Throw;
        public void Advance(long ticks){Interlocked.Add(ref timestamp,ticks);advanced.Set();}
        public void WaitUntil(long deadline,WaitHandle changed,CancellationToken cancel)
        {
            if(Throw)throw new IOException("injected monotone clock failure");Waiting=true;
            if(Hold){Held=true;while(Hold&&!cancel.IsCancellationRequested)WaitHandle.WaitAny([advanced,cancel.WaitHandle]);}else WaitHandle.WaitAny([advanced,changed,cancel.WaitHandle]);Waiting=false;
        }
    }
}
