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
