using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Frontend;
using SNESEmulator.Management;
using SNESEmulator.Network;
using SNESEmulator.SaveStates;

namespace SNESEmulator;

/// <summary>Explicit native-app test entrypoint. Bounded hashes/observations, actual normal pacing and owned WPF Manager.</summary>
internal static class SoakSmoke
{
    internal static int Run(string directory,string rom,double seconds,string managerExe,string region)
    {
        if(!double.IsFinite(seconds)||seconds<10||seconds>14400||!File.Exists(managerExe)||region is not ("ntsc" or "pal"))throw new ArgumentException("Invalid native soak arguments");
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("SNESEMULATOR_DATA_DIR",Path.Combine(directory,"userdata"));
        Environment.SetEnvironmentVariable("SNESEMULATOR_IPC_DIR",Path.Combine(directory,"ipc"));
        var timer=Stopwatch.StartNew();double beganStopwatch=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;var began=DateTimeOffset.UtcNow;
        object reportGate=new(),mediaGate=new();using var log=new StreamWriter(Path.Combine(directory,"progress.jsonl")){AutoFlush=true};
        int errorCount=0;var errors=new List<string>();
        void Error(string text){lock(reportGate){errorCount++;if(errors.Count<16)errors.Add(text);}}
        void Row(object row){lock(reportGate)log.WriteLine(JsonSerializer.Serialize(row));}
        byte[] romBytes=File.ReadAllBytes(rom);var cartridge=new Cartridge(romBytes);
        if(cartridge.Pal!=(region=="pal"))throw new ArgumentException("Soak ROM region does not match requested native hardware profile");
        using var localHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var remoteHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var pcmHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sourceFields=new Dictionary<(long,long),string>();
        var sourceOrder=new Queue<(long,long)>();
        long sourceCount=0,remoteCount=0,matched=0,unmatched=0,mismatches=0,pairs=0,nonzero=0,differentStereo=0;
        long lastSource=-1,lastInput=-1,lastObservation=0,observationGaps=0,maxPending=0,peakManaged=0,managerStatusRequests=0,priorManagerRequests=0;int noPollSamples=0;
        double coldMaximum=0,steadyMaximum=0,readyAt=-1,stableSince=-1;
        long coldDrops=0;double nextRow=0,nextLibrary=0,nextState=15;
        int scans=0,checkpoints=0,steadyCatchupSamples=0,noMediaSamples=0;long priorAudio=0,priorDriver=0;bool firstScreenshot=false,priorReady=false,hosting=false,readinessFailure=false,remoteFailure=false,managerFailure=false;
        Task<bool>? connecting=null;Task libraryTask=Task.CompletedTask,stateTask=Task.CompletedTask;
        using var lifetime=new CancellationTokenSource();using var remote=new SnesNetClient{AudioVolume=0};
        NetworkPlayout? observed=null;Process? manager=null;Task managerOut=Task.CompletedTask,managerErr=Task.CompletedTask;
        NetworkPlayout.Observation? coldMaximumRow=null,steadyMaximumRow=null;
        double closingMaximum=0,measurementEnd=double.PositiveInfinity;long closingDropped=0;
        var quality=new SoakQualityTracker();
        NetworkPlayout.DropCounters? readyDropReasons=null,closingDropReasons=null;
        bool actualCatchupFailure=false,counterRegressionFailure=false,dropAccountingFailure=false;
        void ObservePending()
        {
            if(observed==null)return;
            var rows=observed.DiagnosticSnapshot().Where(x=>x.Sequence>lastObservation).ToArray();
            if(rows.Length==0)return;
            if(rows[0].Sequence>lastObservation+1)observationGaps+=rows[0].Sequence-lastObservation-1;
            lastObservation=rows[^1].Sequence;
            foreach(var row in rows.Where(x=>x.Delivered&&x.CompletedAt<=measurementEnd)){
                double distance=Math.Max(row.CommitPlayhead.HasValue?Math.Abs(row.CommitPlayhead.Value-row.Source):0,row.CompletedPlayhead.HasValue?Math.Abs(row.CompletedPlayhead.Value-row.Source):0);
                if(readyAt<0||row.CompletedAt<beganStopwatch+readyAt){if(distance>coldMaximum){coldMaximum=distance;coldMaximumRow=row;}}
                else if(distance>steadyMaximum){steadyMaximum=distance;steadyMaximumRow=row;}
            }
        }
        string managerProbe=Path.Combine(directory,"manager");Directory.CreateDirectory(managerProbe);
        string libraryRoms=Path.Combine(directory,"library-roms");Directory.CreateDirectory(libraryRoms);File.WriteAllBytes(Path.Combine(libraryRoms,"soak.sfc"),romBytes);
        using var catalog=new LibraryStore(Path.Combine(UserDataPaths.GetBaseDirectory(),"manager","library.db"));
        using var window=new SnesWindow(rom);
        window.Title="SNESEmulator - M9h soak ("+region+")";
        window.ManagerRequestForProbe=command=>{if(command=="Status")Interlocked.Increment(ref managerStatusRequests);};
        window.ProbeSettings.AudioVolume=0; // Gain only: source PCM, providers and real devices remain active.
        remote.FrameReceived+=frame=>{
            string hash=Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(frame.Pixels.AsSpan())));
            lock(mediaGate){remoteCount++;remoteHash.AppendData(MemoryMarshal.AsBytes(frame.Pixels.AsSpan()));
                if(frame.Geometry!=null&&sourceFields.TryGetValue((frame.Timeline.ConsoleEpoch,frame.Geometry.EndClock),out string? expected)){
                    matched++;if(expected!=hash){mismatches++;if(mismatches==1){File.WriteAllBytes(Path.Combine(directory,"first-video-difference.argb32le.bin"),MemoryMarshal.AsBytes(frame.Pixels.AsSpan()).ToArray());Error("Remote native ARGB differs from same-epoch source field");}}
                }else unmatched++;
            }
        };
        remote.AudioReceived+=block=>{
            lock(mediaGate){pcmHash.AppendData(block.Pcm);pairs+=block.Frames;
                for(int i=0;i<block.Pcm.Length;i+=4){short l=BinaryPrimitives.ReadInt16LittleEndian(block.Pcm.AsSpan(i)),r=BinaryPrimitives.ReadInt16LittleEndian(block.Pcm.AsSpan(i+2));if(l!=0||r!=0)nonzero++;if(l!=r)differentStereo++;}
            }
        };
        object? endMetrics=null;long endMaster=0,endFields=0;bool completed=false;
        void ObserveQuality(double now,SNESEmulator.Runtime.WorkerStatus status)
        {
            // The owner marks CatchingUp before advancing its next deadline.
            // Status reports the updated deadline, so it cannot replace actual
            // output counters; a ten-second sample also misses short episodes.
            var presentationCounters=window.PresentationCountersForProbe;
            var currentDropReasons=observed?.ReadDropCounters();
            quality.Observe(now,presentationCounters,currentDropReasons?.Total??0,status.Field,window.ProbeOutput?.CatchingUp??false,status.PacingLateSeconds);
            if(quality.SteadyActualCatchupFields>0&&!actualCatchupFailure){actualCatchupFailure=true;Error("Actual post-readiness owner catchup fields muted local audio");}
            if(quality.CounterRegressions>0&&!counterRegressionFailure){counterRegressionFailure=true;Error("Cumulative presentation/drop counters regressed");}
            if(currentDropReasons is {} dropReasons&&dropReasons.Total!=dropReasons.ClassifiedTotal&&!dropAccountingFailure){dropAccountingFailure=true;Error("Remote drop reasons do not sum to cumulative drops");}
        }
        Row(new{Kind="identity",StartedUtc=began,DurationSeconds=seconds,Region=region,Rom=Path.GetFullPath(rom),RomSha256=Convert.ToHexStringLower(SHA256.HashData(romBytes)),CoreSha256=HashFile(typeof(SnesSystem).Assembly.Location),AppSha256=HashFile(typeof(SoakSmoke).Assembly.Location),Process=Environment.ProcessId,Meaning="Actual SNESEmulator native window, normal pacing, synthetic application input, actual localhost TLS/WinMM and WPF Manager; zero audio gain; no physical DAC/display or hardware-console claim"});
        window.RenderFrame+=frameEvent=>{
            double now=timer.Elapsed.TotalSeconds;var status=window.RuntimeStatus;
            if(status.Fault!=null&&!remoteFailure){remoteFailure=true;Error("Worker: "+status.Fault);}
            if(status.Turbo)Error("Unexpected turbo in normally paced soak");
            if(!firstScreenshot&&status.Field>=20){RuntimeSmoke.Screenshot(window,Path.Combine(directory,"native-start.png"));firstScreenshot=true;}
            if(window.ProbeOutput is {} output&&output.Video.Geometry.EndClock!=lastSource){
                var field=output.Video;lastSource=field.Geometry.EndClock;
                string hash=Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(field.Argb.Span)));
                lock(mediaGate){sourceCount++;localHash.AppendData(MemoryMarshal.AsBytes(field.Native.Span));localHash.AppendData(field.Brightness.Span);localHash.AppendData(MemoryMarshal.AsBytes(field.Argb.Span));var key=(output.Epoch,field.Geometry.EndClock);sourceFields[key]=hash;sourceOrder.Enqueue(key);while(sourceOrder.Count>64)sourceFields.Remove(sourceOrder.Dequeue());}
            }
            long inputSlot=status.Field/2;
            if(inputSlot!=lastInput){lastInput=inputSlot;var keys=new HashSet<string>();long f=status.Field;
                if(f is >=150 and <160)keys.Add("Enter");if(f is >=210 and <220 or >=310 and <320)keys.Add("Z");
                if(f is >=270 and <290)keys.Add("Right");if(f is >=340 and <360)keys.Add("Down");
                if(f>1200){long p=f%1200;if(p is >=200 and <320)keys.Add("Right");if(p is >=500 and <506)keys.Add("Z");if(p is >=800 and <850)keys.Add("Left");}
                window.ProbeInput(true,"SOAK-SYNTHETIC",new HashSet<string>(),keys);
            }
            if(!hosting&&!window.MediaOperationPending&&status.Field>2){hosting=true;window.ProbeNetwork(true,UnusedPort());}
            if(hosting&&window.ProbeServer.IsRunning&&connecting==null){int port=window.ProbeServer.Port;connecting=Task.Run(()=>remote.Connect("localhost",port,"",SnesNetClientRole.Observer,"soak",out _));}
            if(connecting is {IsCompletedSuccessfully:true}&&connecting.Result&&observed==null){observed=remote.Playout;observed.CaptureDiagnostics=true;}
            if(connecting is {IsCompleted:true}&&(!connecting.IsCompletedSuccessfully||!connecting.Result)&&!remoteFailure){remoteFailure=true;Error("Actual TLS observer connection failed");}
            if(observed!=null){
                if(!remote.IsConnected&&!remoteFailure&&now<seconds-.1){remoteFailure=true;Error("Remote disconnected during soak");}
                if(observed.Fault!=null&&!remoteFailure){remoteFailure=true;Error("Playout: "+observed.Fault);}
                maxPending=Math.Max(maxPending,observed.Pending);
                if(readyAt<0&&status.PacingLateSeconds<.05&&remote.TimelineIdentity is {Muted:false}&&observed.DriverComparedVideo>=5&&observed.PresentedAudio>=5){if(stableSince<0)stableSince=now;if(now-stableSince>=.5){readyAt=now;readyDropReasons=observed.ReadDropCounters();coldDrops=readyDropReasons.Value.Total;quality.MarkReady(now,window.PresentationCountersForProbe,coldDrops);}}
                else if(readyAt<0)stableSince=-1;
            }
            ObserveQuality(now,status);
            if(readyAt<0&&now>=30&&!readinessFailure){readinessFailure=true;Error("30-second stable unmuted driver readiness failed; run continues to preserve actual duration");}
            if(manager==null&&window.ManagerPipe!=null){
                var start=new ProcessStartInfo(Path.GetFullPath(managerExe)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
                start.Environment["SNESEMULATOR_MANAGER_TEST_ID"]="soak-"+Guid.NewGuid().ToString("N");
                foreach(string a in new[]{"--connect",Environment.ProcessId.ToString(),"--manager-probe",managerProbe})start.ArgumentList.Add(a);
                manager=Process.Start(start)??throw new IOException("Manager did not start");managerOut=Drain(manager.StandardOutput,Path.Combine(managerProbe,"stdout.log"));managerErr=Drain(manager.StandardError,Path.Combine(managerProbe,"stderr.log"));
            }
            if(manager is {HasExited:true}&&!managerFailure){managerFailure=true;Error("WPF Manager exited during soak: "+manager.ExitCode);}
            if(now>=nextLibrary&&File.Exists(Path.Combine(managerProbe,"ready.json"))&&libraryTask.IsCompleted){nextLibrary=now+300;libraryTask=Task.Run(async()=>{try{await catalog.InitializeAsync(lifetime.Token);int count=await catalog.ScanAsync(libraryRoms,lifetime.Token);var items=await catalog.ListAsync(token:lifetime.Token);Interlocked.Increment(ref scans);if(count!=1||items.Count!=1)Error("Bounded shared library scan/list differs");}catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}catch(Exception e){Error("Library: "+e.Message);}});}
            if(now>=nextState&&now<seconds-10&&stateTask.IsCompleted&&!window.MediaOperationPending){nextState=now+300;int slot=checkpoints++%2;stateTask=Task.Run(async()=>{try{byte[] raw=await window.CaptureStateForProbe();string path=Path.Combine(directory,"checkpoint-"+slot+".state");StateSerializer.SaveCaptured(raw,path,cartridge);_ = StateSerializer.Load(cartridge,path);Row(new{Kind="checkpoint",WallSeconds=timer.Elapsed.TotalSeconds,Path=path,Sha256=HashFile(path)});}catch(Exception e){Error("Checkpoint: "+e.Message);}});}
            if(now>=nextRow){nextRow=now+10;peakManaged=Math.Max(peakManaged,GC.GetTotalMemory(false));
                if(readyAt>=0&&File.Exists(Path.Combine(managerProbe,"ready.json"))){if(managerStatusRequests==priorManagerRequests){if(++noPollSamples==2)Error("Actual WPF Manager supplied no status request for20 seconds");}else noPollSamples=0;priorManagerRequests=managerStatusRequests;}
                if(readyAt>=0&&status.PacingLateSeconds>.1&&++steadyCatchupSamples==1)Error("Steady normal pacing crossed existing100ms catchup/mute boundary");
                if(observed!=null){if(priorReady&&(observed.PresentedAudio==priorAudio||observed.DriverComparedVideo==priorDriver)&&++noMediaSamples==1)Error("A steady10-second observation window had no audio or driver-synchronized video");priorAudio=observed.PresentedAudio;priorDriver=observed.DriverComparedVideo;priorReady=readyAt>=0;}
                ObservePending();
                Row(new{Kind="sample",WallSeconds=now,Metrics=window.RuntimeMetrics,Managed=GC.GetTotalMemory(false),WorkingSet=Environment.WorkingSet,PeakManaged=peakManaged,scans,checkpoints,SourceFields=sourceCount,RemoteFields=remoteCount,Matched=matched,Unmatched=unmatched,Mismatches=mismatches,PcmPairs=pairs,ReadySeconds=readyAt,DriverCompared=observed?.DriverComparedVideo,RemoteAudio=observed?.PresentedAudio,RemoteVideo=observed?.PresentedVideo,RemoteDropped=observed?.Dropped,MaximumSoftwareAv=observed?.MaximumSoftwareAvErrorSeconds,RemotePending=observed?.Pending,observationGaps});
            }
            if(now>=seconds){RuntimeSmoke.Screenshot(window,Path.Combine(directory,"native-end.png"));
                // Screenshot work can span several presentation outputs. Observe
                // once more at the measurement cutoff, before close/cleanup, so
                // a last-window catchup cannot disappear behind the final render.
                var finalStatus=window.RuntimeStatus;ObserveQuality(timer.Elapsed.TotalSeconds,finalStatus);
                endMetrics=window.RuntimeMetrics;endMaster=finalStatus.MasterClock;endFields=finalStatus.Field;closingMaximum=observed?.MaximumSoftwareAvErrorSeconds??0;closingDropReasons=observed?.ReadDropCounters();closingDropped=closingDropReasons?.Total??0;measurementEnd=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;ObservePending();completed=true;window.Close();}
        };
        try{window.Run();}
        finally{
            lifetime.Cancel();remote.Disconnect();connecting?.GetAwaiter().GetResult();libraryTask.GetAwaiter().GetResult();stateTask.GetAwaiter().GetResult();
            if(manager!=null){File.WriteAllText(Path.Combine(managerProbe,"close"),"owned normal soak cleanup");if(!manager.WaitForExit(10000)){Error("Owned Manager failed normal close; killed after join budget");manager.Kill(true);manager.WaitForExit();}if(manager.ExitCode!=0)Error("Manager close exit "+manager.ExitCode);managerOut.GetAwaiter().GetResult();managerErr.GetAwaiter().GetResult();manager.Dispose();}
        }
        if(!completed)Error("Native window closed before requested duration");
        if(!File.Exists(Path.Combine(managerProbe,"ready.json"))||scans==0||managerStatusRequests==0)Error("Actual WPF Manager/status polling/shared library readiness missing");
        if(observed==null||readyAt<0||pairs==0||matched==0||observed.DriverComparedVideo<30)Error("No nonzero actual driver/audio/video/matched evidence");
        ObservePending();
        closingMaximum=Math.Max(closingMaximum,Math.Max(coldMaximum,steadyMaximum));
        if(closingMaximum>=.04)Error("Cumulative software A/V exceeded40ms including Cold");
        if(window.RuntimeStatus.DroppedPresentation!=0)Error("Owner presentation queue discarded native fields");
        if(peakManaged>=256*1024*1024||maxPending>NetworkPlayout.Capacity||mismatches!=0||observationGaps!=0)Error("Memory/queue/native-output/observation bound violated");
        File.WriteAllText(Path.Combine(directory,"max-observations.json"),JsonSerializer.Serialize(new{Cold=coldMaximumRow,Steady=steadyMaximumRow,Last128=observed?.DiagnosticSnapshot().TakeLast(128).ToArray(),ColdBoundary=readyAt<0?(double?)null:beganStopwatch+readyAt,Meaning="Bounded actual-run software evidence; cumulative metric is not reset or clamped"},new JsonSerializerOptions{WriteIndented=true}));
        Row(new{Kind="quality",steadyCatchupSamples,ActualPostReadyMutedFields=quality.SteadyActualCatchupFields,CounterRegressions=quality.CounterRegressions,OmittedCounterEvents=quality.OmittedChangeEvents,noMediaSamples,ManagerStatusRequests=managerStatusRequests,NoPollSamples=noPollSamples,ProducerDropped=window.RuntimeStatus.DroppedPresentation,MaximumAllowedSoftwareAvSeconds=.04,ExistingCatchupMuteBoundarySeconds=.1,Meaning="Ten-second PacingLate samples are not continuous; actual cumulative muted fields are checked at every RenderFrame. Remote freshness/reset discards are classified, not required to be zero. No relaxed Core sustained-window acceptance"});
        var result=new{
            Success=errorCount==0,Errors=errors,ErrorCount=errorCount,StartedUtc=began,EndedUtc=DateTimeOffset.UtcNow,
            ActualWallSeconds=timer.Elapsed.TotalSeconds,RequestedWallSeconds=seconds,Fields=endFields,MasterClocks=endMaster,
            HardwareSeconds=endMaster*window.RuntimeProfileDenominator/(double)window.RuntimeProfileNumerator,ReadySeconds=readyAt,
            ColdMaximum=coldMaximum,SteadyMaximum=steadyMaximum,CumulativeMaximum=closingMaximum,
            PresentationObservation=new{quality.ReadySeconds,quality.ReadyCounters,quality.LastCounters,quality.SteadyActualCatchupFields,quality.SteadyRemoteDropped,quality.CounterRegressions,quality.OmittedChangeEvents,quality.Events,Limits="Each cumulative presentation counter covers every dequeued owner output; separate Interlocked reads are not a joint atomic field-boundary snapshot. Latest output/late-time and event times are render observations, not exact owner mutation timestamps. Detail overflow is explicit; cumulative counts remain complete."},
            RemoteDropReasons=new{Ready=readyDropReasons,MeasurementEnd=closingDropReasons,AfterCleanup=observed?.ReadDropCounters(),Meaning="Disjoint causes sum to Total; phase snapshots preserve original readiness/end/cleanup boundaries. Normal freshness/reset/shutdown discards are not producer loss."},
            ColdDropped=coldDrops,MeasurementDropped=observed==null?(long?)null:closingDropped-coldDrops,
            ShutdownDropped=observed==null?(long?)null:observed.Dropped-closingDropped,
            PeakManaged=peakManaged,MaximumRemotePending=maxPending,ObservationGaps=observationGaps,
            SourceFieldsHashed=sourceCount,RemoteFieldsHashed=remoteCount,MatchedNativeArgb=matched,UnmatchedNativeArgb=unmatched,Mismatches=mismatches,
            PcmPairs=pairs,NonzeroPairs=nonzero,DifferentStereoPairs=differentStereo,
            SourceNativeHash=Convert.ToHexStringLower(localHash.GetHashAndReset()),RemoteArgbHash=Convert.ToHexStringLower(remoteHash.GetHashAndReset()),PcmHash=Convert.ToHexStringLower(pcmHash.GetHashAndReset()),
            LibraryScans=scans,Checkpoints=checkpoints,EndMetrics=endMetrics,
            Limits="App hooks are synthetic; source fields sampled by render polling (not all generated fields); hashes are runtime integrity evidence, not independent hardware oracles; actual software WinMM boundaries, no physical DAC/display latency claim; normal pacing and audio providers active at zero gain; A/V end counters sampled before close, normal cleanup discards separate; media streaming hashes/counts include final owned drain"
        };
        File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return result.Success?0:1;
    }
    private static string HashFile(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static int UnusedPort(){var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    private static async Task Drain(StreamReader reader,string path){using var writer=new StreamWriter(path);char[] buffer=new char[4096];int written=0,count;while((count=await reader.ReadAsync(buffer))>0){int keep=Math.Min(count,65536-written);if(keep>0){await writer.WriteAsync(buffer.AsMemory(0,keep));written+=keep;}}}
}
