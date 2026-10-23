using SNESEmulator.Network;

internal static class NetworkTimelineTests
{
    private static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    public static void Run(Action<string,Action> test)
    {
        test("M9d version4 envelope bounds epoch and opaque media identity",()=>{
            var t=new NetworkTimeline(123,42,945000000,44,24576000,1,false,false,false);
            Check(NetworkTimeline.Decode(t.Encode())==t,"roundtrip");byte[] raw=[0x33,0x44,0x49,0x56,1,2,3];
            Check(t.Unwrap(t.Wrap(raw),out var decoded)&&decoded.SequenceEqual(raw),"VID3 bytes unchanged");
            Check(!(t with{StreamEpoch=43}).Unwrap(t.Wrap(raw),out _),"stale stream");Check(!(t with{ConsoleEpoch=124}).Unwrap(t.Wrap(raw),out _),"stale console");
            foreach(int length in new[]{0,16,50,51,52,67,69}){try{NetworkTimeline.Decode(new byte[length]);throw new Exception("accepted malformed");}catch(InvalidDataException){}}
            foreach(int index in new[]{48,49,50,51}){var bad=t.Encode();bad[index]=2;try{NetworkTimeline.Decode(bad);throw new Exception("accepted flags");}catch(InvalidDataException){}}
        });
        test("M9d shared rational master and exact DAC crystal timestamps",()=>{
            foreach(var profile in new[]{SNESEmulator.Core.HardwareProfile.Ntsc,SNESEmulator.Core.HardwareProfile.PalProfile})
            {
                var t=new NetworkTimeline(5,9,profile.MasterHzNumerator,profile.MasterHzDenominator,profile.ApuHzNumerator,profile.ApuHzDenominator,false,false,false);
                foreach(long sample in new[]{0L,1,533,32000,9876543})
                {
                    long crystal=672+sample*768;
                    long master=(long)(((Int128)crystal*t.MasterNumerator*t.ApuDenominator+t.ApuNumerator*t.MasterDenominator-1)/(t.ApuNumerator*t.MasterDenominator));
                    Check(Math.Abs(t.AudioSeconds(crystal)-t.VideoSeconds(master))<=t.MasterDenominator/(double)t.MasterNumerator+1e-12,"ceil master APUDAC mapping");
                }
                Check(t.AudioSeconds(672)>0,"DAC phase not rounded to sample0");
            }
        });
        test("M9d deterministic A/V scheduling includes 190ms software device backlog",()=>{
            double now=0,playhead=10;int video=0,audio=0;using var p=new NetworkPlayout(()=>{},()=>playhead,()=>now,false);
            p.Enqueue(10,true,()=>audio++);p.Enqueue(10.016,false,()=>video++);now=.04;Check(p.PumpOnce()&&audio==1,"audio submitted at target");
            now=.056;Check(!p.PumpOnce()&&video==0,"video cannot precede software audio playhead");
            now=.23;playhead=10.016;Check(p.PumpOnce()&&video==1,"190ms buffer included");
            Check(p.MaximumSoftwareAvErrorSeconds<1e-10&&p.MaximumQueueLatencySeconds>=.23,"presentation boundary metrics");
        });
        test("M9d deterministic scheduling drift jitter and callback isolation",()=>{
            double now=0,playhead=0;using var p=new NetworkPlayout(()=>{},()=>playhead,()=>now,false);int video=0,audio=0;
            for(int i=0;i<1800;i++)
            {
                double source=i/60.09881389744051;now=source;p.Enqueue(source,true,()=>audio++);p.Enqueue(source,false,()=>video++);
                now+=.04+(i%7)*.0002;playhead=source;while(p.PumpOnce()){}
            }
            Check(audio==1800&&video==1800&&p.MaximumSoftwareAvErrorSeconds<.000001&&p.MaximumSchedulingErrorSeconds<.0013,"30s injected variable field deadlines");
            p.Enqueue(now,true,()=>throw new IOException("injected callback"));now+=.1;p.PumpOnce();Check(p.Fault?.Contains("injected callback")==true,"callback failure visible");
        });
        test("M9g A/V observation distinguishes sampled selection commit and callback playheads",()=>{
            double now=0,head=10;using var p=new NetworkPlayout(()=>{},()=>head,()=>now,false){CaptureDiagnostics=true};
            p.Enqueue(10,false,()=>{head=10.06;now=.10;});now=.04;p.BeforeCommit=()=>{head=10.05;now=.09;};
            Check(p.PumpOnce(),"selected video");var row=p.DiagnosticSnapshot().Single();
            Check(row.Source==10&&row.SamplePlayhead==10&&row.CommitPlayhead==10.05&&row.CompletedPlayhead==10.06&&row.Delivered,"three source-time observations");
            Check(row.SampleAt==.04&&row.CommitAt==.09&&row.CompletedAt==.10,"three host-time observations");
            Check(p.MaximumSoftwareAvErrorSeconds==0,"instrumentation preserves legacy cumulative metric rather than silently fixing it");
            p.BeforeCommit=null;head=10.05;now=.11;p.Enqueue(10,false,()=>{});now=.15;p.PumpOnce();
            Check(Math.Abs(p.MaximumSoftwareAvErrorSeconds-.05)<1e-10,"cold and later samples remain cumulative");
        });
        test("M9d final-provider initial padding underflow refill and exact stereo mapping",()=>{
            var b=new NetworkAudioBuffer();float[] output=new float[9600];b.Read(output,0,output.Length);
            Check(b.PaddingFrames==4800&&b.CrystalAtDriverFrame(0)==null,"startup silence has no source timestamp");
            byte[] pcm=new byte[2120];for(int i=0;i<530;i++){System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4),12000);System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4+2),-9000);}
            b.Write(pcm,672);long first=b.RenderedFrames;b.Read(output,0,output.Length);
            Check(output[0]>0&&output[1]<0&&b.CrystalAtDriverFrame(first)==672,"real stereo source after startup padding");
            Check(b.CrystalAtDriverFrame(first+4800-1)==null,"jitter underflow explicitly invalid");
            long refill=b.RenderedFrames;b.Write(pcm,672+530*768L);b.Read(output,0,output.Length);
            Check(b.CrystalAtDriverFrame(refill-1)==null&&b.CrystalAtDriverFrame(refill)==672+530*768L-512,"refill not falsely assigned to previous driver silence");
            Check(b.CrystalAtDriverFrame(refill+1)==672+530*768L,"final48k resampler source increment");
            b.Clear();Check(b.RenderedFrames==0&&b.CrystalAtDriverFrame(0)==null,"timeline clears all provider maps");
        });
        test("M9d bounded source mappings and multi-hour WinMM 32bit wrap",()=>{
            var cursor=new DriverByteCursor();Check(cursor.Observe(0xfffffff0)==0xfffffff0,"initial cursor");Check(cursor.Observe(0x20)==0x100000020,"32bit wrap");
            ulong prior=cursor.Observe(0x20);for(int i=0;i<100;i++){prior+=100000000;Check(cursor.Observe(unchecked((uint)prior))==prior,"multi-hour monotone cursor");}
            cursor.Reset();Check(cursor.Observe(0)==0,"explicit epochreset");
            var b=new NetworkAudioBuffer();try{b.Write(new byte[(NetworkAudioBuffer.NativeCapacity+1)*4],672);throw new Exception("accepted unbounded source");}catch(IOException){}
            var output=new float[2];for(int i=0;i<20000;i++){if(i%2==0)b.Write([1,0,2,0],672+i*768L);b.Read(output,0,2);}
            Check(b.MappingSegments<=NetworkAudioBuffer.SegmentCapacity&&b.BufferedNativeFrames<=NetworkAudioBuffer.NativeCapacity,"bounded marker and sample resources");
        });
        test("M9d UI commit race hardware invalidation and explicit mode retention",()=>{
            var old=new NetworkTimeline(10,10,945000000,44,24576000,1,true,false,false);
            var box=new EpochPublication<string>();box.Update(old);Check(box.Publish(old,"old"),"initial owned image");
            using var before=new ManualResetEventSlim();using var go=new ManualResetEventSlim();
            Task callback=Task.Run(()=>{before.Set();go.Wait();Check(!box.Publish(old,"stale"),"old in-flight callback rejected at atomic UI commit");});
            Check(before.Wait(1000),"commit barrier");var reset=old with{ConsoleEpoch=11,StreamEpoch=11,InvalidatePicture=true};box.Update(reset);go.Set();callback.GetAwaiter().GetResult();Check(box.Read(reset)==null,"paused hardware reset clears old image");
            Check(box.Publish(reset,"new"),"new epoch field");var mode=reset with{ConsoleEpoch=12,StreamEpoch=12,InvalidatePicture=false};box.Update(mode);Check(box.Read(mode)=="new"&&box.Read(reset)==null,"mode retains owned image with explicit current identity");
        });
        test("M9d audio chunk partition invariance and honest missing lookahead",()=>{
            byte[] pcm=new byte[2131*4];for(int i=0;i<2131;i++){System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4),(short)(i%2==0?16000:-8000));System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4+2),(short)(i%3==0?-12000:3000));}
            var whole=new NetworkAudioBuffer();var parts=new NetworkAudioBuffer();whole.Write(pcm,672);int offset=0;
            foreach(int length in new[]{532,533,1,533,532}){parts.Write(pcm.AsSpan(offset*4,length*4).ToArray(),672+offset*768L);offset+=length;}
            float[] a=new float[6300],b=new float[6300];whole.Read(a,0,a.Length);parts.Read(b,0,b.Length);Check(a.SequenceEqual(b),"identical final output across odd packet partitions with same prefilling");
            var edge=new NetworkAudioBuffer();edge.Write([0,64,0,192],672);float[] one=new float[6];edge.Read(one,0,6);Check(edge.CrystalAtDriverFrame(0)==672&&edge.CrystalAtDriverFrame(1)==null&&one[2]==0,"fractional lookahead absent is padding, not invented interpolation");
            edge.Write([0,192,0,64],1440);edge.Read(one,0,2);Check(edge.CrystalAtDriverFrame(3)==1184&&one[0]<0&&one[1]>0,"late neighbor restores exact interpolation phase");
        });
        test("M9d worker hardware and input generations survive coalesced mode transitions",()=>{
            using var worker=new SNESEmulator.Runtime.EmulationWorker();byte[] rom=SNESEmulator.Core.DiagnosticRom.Create();worker.InsertAsync(rom).GetAwaiter().GetResult();
            var initial=worker.Status;worker.SetModeAsync(true,true).GetAwaiter().GetResult();var turbo=worker.Status;
            Check(turbo.Epoch>initial.Epoch&&turbo.HardwareGeneration==initial.HardwareGeneration&&turbo.InputEpoch==initial.InputEpoch,"pure turbo keeps hardware/input identity");
            worker.ResetAsync().GetAwaiter().GetResult();worker.SetModeAsync(true,false).GetAwaiter().GetResult();var reset=worker.Status;
            Check(reset.HardwareGeneration>turbo.HardwareGeneration&&reset.InputEpoch>turbo.InputEpoch,"reset plus unseen turbo cannot hide reset");
            using var compressed=new MemoryStream();byte[] raw=worker.CaptureAsync().GetAwaiter().GetResult();using(var zip=new System.IO.Compression.GZipStream(compressed,System.IO.Compression.CompressionLevel.Optimal,true))zip.Write(raw);
            worker.LoadAsync(compressed.ToArray()).GetAwaiter().GetResult();worker.SetModeAsync(true,true).GetAwaiter().GetResult();var loaded=worker.Status;
            Check(loaded.HardwareGeneration>reset.HardwareGeneration&&loaded.InputEpoch>reset.InputEpoch,"load plus unseen mode cannot hide hardware replacement");
            worker.InsertAsync(rom).GetAwaiter().GetResult();worker.SetModeAsync(true,false).GetAwaiter().GetResult();Check(worker.Status.HardwareGeneration>loaded.HardwareGeneration,"ROM plus coalesced mode retains hardware generation");
        });
        test("M9d held live worker input survives pure turbo without neutral fields",()=>{
            var clock=new HeldClock();using var worker=new SNESEmulator.Runtime.EmulationWorker(clock:clock);worker.InsertAsync(SNESEmulator.Core.DiagnosticRom.Create()).GetAwaiter().GetResult();worker.SetModeAsync(false,false).GetAwaiter().GetResult();
            Check(SpinWait.SpinUntil(()=>worker.Status.Field>=1&&clock.Waiting,3000),"initial owner boundary");worker.SubmitInput(new(0x8000,0x4000));long before=worker.Status.Field;worker.SetModeAsync(false,true).GetAwaiter().GetResult();
            Check(SpinWait.SpinUntil(()=>worker.Status.Field>before,3000),"turbo field emitted");var output=worker.Latest;Check(output!.Input.Port1==0x8000&&output.Input.Port2==0x4000,"no implicit neutral mailbox on turbo rebase");
        });
        test("M9d delayed audio commit cannot resurrect samples after stream reset",()=>{
            double now=0;int samples=0;using var p=new NetworkPlayout(()=>samples=0,clock:()=>now,automatic:false);using var before=new ManualResetEventSlim();using var resume=new ManualResetEventSlim();
            p.Enqueue(0,true,()=>samples++);now=.04;p.BeforeCommit=()=>{before.Set();resume.Wait();};Task commit=Task.Run(()=>p.PumpOnce());Check(before.Wait(1000),"final audio commit barrier");p.Reset();resume.Set();commit.GetAwaiter().GetResult();Check(samples==0&&p.PresentedAudio==0,"generation check and callback are atomic against reset");
        });
        test("M9d network status and encode faults cannot stop local presentation",()=>{
            int local=0,errors=0;var network=new SNESEmulator.Runtime.IsolatedNetworkPresentation(_=>throw new IOException("status fault"),(_,_)=>throw new IOException("encode fault"),_=>errors++);
            var status=new SNESEmulator.Runtime.WorkerStatus(1,0,0,"","",true,false,null,0,0,0,0,24576000,1);var g=new SNESEmulator.Core.PpuGeometry(256,224,256,false);
            var output=new SNESEmulator.Runtime.FieldOutput(1,new(g,new ushort[256*224],new byte[256*224],new uint[256*224]),ReadOnlyMemory<SNESEmulator.Core.StereoSample>.Empty,0,0,default,false);
            var handoff=new SNESEmulator.Runtime.PresentationHandoff(_=>{},_=>local++,()=>{},_=>{});
            for(int i=0;i<4;i++){network.Update(status);network.Present(output,()=>status);handoff.Present(output,1);}
            Check(local==4&&network.Failures==8&&errors==8&&network.LastError!.Contains("encode fault"),"both network sink boundaries isolate faults and expose complete error");
        });
        test("M9d visible network outcome survives older coalesced disconnect status",()=>{
            using var mailbox=new SNESEmulator.Runtime.UiMailbox();string shown="CONNECTING";
            foreach(string outcome in new[]{"PASSWORD REJECTED","TLS CERTIFICATE CHANGED - REVIEW CERTIFICATE ROW"})
            {
                mailbox.SetStatus("DISCONNECTED");mailbox.PostOutcomeAsync(()=>shown=outcome,()=>shown).GetAwaiter().GetResult();mailbox.Drain();shown=mailbox.TakeStatus()??shown;
                Check(shown==outcome,"exact Drain/TakeStatus order preserves authoritative connection result");
            }
        });
        test("M9d queue pressure reset and in-flight generation cancellation",()=>{
            double now=0;int stale=0,clears=0;using var p=new NetworkPlayout(()=>clears++,clock:()=>now,automatic:false);
            for(int i=0;i<1000;i++)p.Enqueue(0,false,()=>stale++);
            Check(p.Pending<=NetworkPlayout.Capacity&&p.Dropped>0&&clears>0,"bounded scheduler");p.Reset();now=1;while(p.PumpOnce()){}Check(stale==0,"reset removes prior timeline");
        });
    }
    private sealed class HeldClock:SNESEmulator.Runtime.IMonotonicClock
    {
        public long Timestamp=>0;public long Frequency=>1000000;public volatile bool Waiting;
        public void WaitUntil(long deadline,WaitHandle changed,CancellationToken token){Waiting=true;WaitHandle.WaitAny([changed,token.WaitHandle]);Waiting=false;}
    }

}
