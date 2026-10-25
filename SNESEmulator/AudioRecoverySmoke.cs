using System.Diagnostics;
using System.Text.Json;
using SNESEmulator.Network;

namespace SNESEmulator;

/// <summary>Explicit device regression fixture, not a SNES hardware clock profile.</summary>
internal static class AudioRecoverySmoke
{
    internal static int Run(string directory,int seconds,bool adaptive)
    {
        if(seconds<10||seconds>600)throw new ArgumentOutOfRangeException(nameof(seconds));
        Directory.CreateDirectory(directory);using var log=new StreamWriter(Path.Combine(directory,"progress.jsonl")){AutoFlush=true};object gate=new();
        void Row(object row){lock(gate)log.WriteLine(JsonSerializer.Serialize(row));}
        using var audio=new NetworkAudioOutput(adaptive){Volume=0};var watch=Stopwatch.StartNew();double Now()=>watch.Elapsed.TotalSeconds;
        double? Head()=>audio.PlayedCrystal/24576000d;
        using var playout=new NetworkPlayout(()=>audio.Clear(),Head,Now,needsAudio:()=>true){CaptureDiagnostics=true};
        var exports=new PlayoutDiagnosticExportGate();NetworkPlayout.Observation? maximumRow=null;double maximumObserved=0;
        object Gc()=>new{PauseSeconds=GC.GetTotalPauseDuration().TotalSeconds,AllocatedBytes=GC.GetTotalAllocatedBytes(false),Index=GC.GetGCMemoryInfo().Index};
        playout.DiagnosticObserver=row=>{
            if(row.Delivered){double distance=Math.Max(Math.Abs(row.Source-row.CommitPlayhead.GetValueOrDefault(row.Source)),Math.Abs(row.Source-row.CompletedPlayhead.GetValueOrDefault(row.Source)));
                if(distance>maximumObserved){bool firstViolation=maximumObserved<.04&&distance>=.04;maximumObserved=distance;maximumRow=row;
                    if(firstViolation)Row(new{Kind="first-av-violation",WallSeconds=Now(),Observation=row,Audio=audio.ReadDiagnostics(),Gc=Gc()});}}
            if(exports.TryExport(row))Row(new{Kind="rejected-live",WallSeconds=Now(),Observation=row,Audio=audio.ReadDiagnostics(),Drops=playout.ReadDropCounters()});
        };
        // This fixture injects a source clock500ppm faster than nominal. The
        // physical WinMM device is untouched. The160ms prefill exposes the fixed
        // converter's accumulating delay within a bounded native comparison.
        const double producerRate=32016;const int packet=640,prefill=5120;
        long sent=0;
        void Write(int frames){byte[] pcm=new byte[frames*4];for(int i=0;i<frames;i++){short v=(short)((sent+i)%4096-2048);System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4),v);System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4+2),(short)-v);}audio.Write(pcm,672+sent*768);sent+=frames;}
        Row(new{Kind="identity",StartedUtc=DateTimeOffset.UtcNow,StopwatchTimestamp=Stopwatch.GetTimestamp(),Stopwatch.Frequency,ObservationClock="watch.Elapsed.TotalSeconds, same epoch as WallSeconds",adaptive,seconds,ProducerRate=producerRate,PrefillNativeFrames=prefill,Process=Environment.ProcessId,Meaning="Controlled presentation-only source500ppm injection; no SNES machine, no hardware clock change, actual WinMM cursor/map, zero gain, no physical DAC claim"});
        Write(prefill);playout.Enqueue(sent/32000d,false,()=>{});double next=0;
        while(Now()<seconds){
            double now=Now();long due=prefill+(long)(now*producerRate);
            while(due-sent>=packet){Write(packet);playout.Enqueue(sent/32000d,false,()=>{});}
            if(now>=next){next=now+10;Row(new{Kind="sample",WallSeconds=now,SentNativeFrames=sent,Audio=audio.ReadDiagnostics(),Drops=playout.ReadDropCounters(),LastVideo=playout.DiagnosticSnapshot().LastOrDefault(),MaximumObservation=maximumRow,Gc=Gc(),playout.PresentedVideo,playout.MaximumSoftwareAvErrorSeconds});}
            if(playout.Fault!=null)break;Thread.Sleep(1);
        }
        var drops=playout.ReadDropCounters();double cumulativeAv=Math.Max(playout.MaximumSoftwareAvErrorSeconds,maximumObserved);
        bool qualityAccepted=playout.Fault==null&&drops.Total==0&&cumulativeAv<.04&&playout.PresentedVideo>0;
        var result=new{QualityAccepted=qualityAccepted,MaximumAllowedSoftwareAvSeconds=.04,CumulativeSoftwareAvSeconds=cumulativeAv,adaptive,RequestedSeconds=seconds,ActualSeconds=Now(),SentNativeFrames=sent,Audio=audio.ReadDiagnostics(),Drops=drops,playout.PresentedVideo,playout.MaximumSoftwareAvErrorSeconds,MaximumObservation=maximumRow,Gc=Gc(),playout.Fault,Meaning="Diagnostic injection result; normal regional native acceptance is separate"};
        File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return qualityAccepted?0:1;
    }
}
