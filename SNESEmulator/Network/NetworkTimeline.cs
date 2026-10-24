using System.Buffers.Binary;
using System.Diagnostics;

namespace SNESEmulator.Network;

// Version4 envelopes leave the complete VID3/AUD2 payload byte identities intact.
public sealed record NetworkTimeline(long ConsoleEpoch,long StreamEpoch,long MasterNumerator,long MasterDenominator,
    long ApuNumerator,long ApuDenominator,bool Paused,bool Turbo,bool Muted,bool InvalidatePicture=false,long HardwareGeneration=0,long InputEpoch=0)
{
    public byte[] Encode()
    {
        Validate();byte[] bytes=new byte[68];long[] values=[ConsoleEpoch,StreamEpoch,MasterNumerator,MasterDenominator,ApuNumerator,ApuDenominator];
        for(int i=0;i<values.Length;i++)BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i*8),values[i]);
        bytes[48]=(byte)(Paused?1:0);bytes[49]=(byte)(Turbo?1:0);bytes[50]=(byte)(Muted?1:0);bytes[51]=(byte)(InvalidatePicture?1:0);BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(52),HardwareGeneration);BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(60),InputEpoch);return bytes;
    }
    public static NetworkTimeline Decode(byte[] bytes)
    {
        if(bytes.Length!=68||bytes[48]>1||bytes[49]>1||bytes[50]>1||bytes[51]>1)throw new InvalidDataException("Invalid timeline");
        long Get(int i)=>BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(i*8));
        var value=new NetworkTimeline(Get(0),Get(1),Get(2),Get(3),Get(4),Get(5),bytes[48]!=0,bytes[49]!=0,bytes[50]!=0,bytes[51]!=0,BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(52)),BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(60)));value.Validate();return value;
    }
    private void Validate(){if(HardwareGeneration<0||InputEpoch<0||ConsoleEpoch<0||StreamEpoch<1||MasterNumerator<=0||MasterDenominator<=0||ApuNumerator<=0||ApuDenominator<=0||MasterNumerator>1000000000000||ApuNumerator>1000000000000||MasterDenominator>1000000||ApuDenominator>1000000)throw new InvalidDataException("Invalid timeline identity/rates");}
    public byte[] Wrap(byte[] payload)
    {
        byte[] bytes=new byte[payload.Length+16];BinaryPrimitives.WriteInt64LittleEndian(bytes,ConsoleEpoch);BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8),StreamEpoch);payload.CopyTo(bytes,16);return bytes;
    }
    public bool Unwrap(byte[] bytes,out byte[] payload)
    {
        payload=[];if(bytes.Length<16||BinaryPrimitives.ReadInt64LittleEndian(bytes)!=ConsoleEpoch||BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(8))!=StreamEpoch)return false;
        payload=bytes[16..];return true;
    }
    public double VideoSeconds(long master)=>master*(double)MasterDenominator/MasterNumerator;
    // Crystal is the exact native DAC timestamp (672 + 768*n), not sample index*768.
    public double AudioSeconds(long crystal)=>crystal*(double)ApuDenominator/ApuNumerator;
}

/// <summary>One bounded software playout timeline for both media kinds. No DAC latency inference.</summary>
public sealed class NetworkPlayout : IDisposable
{
    internal sealed record Observation(long Sequence,long Generation,double Source,double Arrival,double Due,double SampleAt,double CommitAt,double CompletedAt,double? SamplePlayhead,double? CommitPlayhead,double? CompletedPlayhead,bool Delivered,bool GenerationValid,double SchedulingError);
    // Opt-in bounded software diagnostics, including rejected commit probes.
    internal bool CaptureDiagnostics {get;set;}
    private readonly Queue<Observation> observations=new();
    private long observationSequence;
    internal Observation[] DiagnosticSnapshot(){lock(commitGate)return observations.ToArray();}
    private sealed record Item(double Source,double Arrival,bool Audio,long Generation,Action Deliver);
    private readonly object gate=new(),commitGate=new();
    internal Action? BeforeCommit {get;set;}
    private readonly List<Item> queue=[];
    private readonly CancellationTokenSource stop=new();
    private readonly Task task;
    [ThreadStatic] private static NetworkPlayout? current;
    private readonly Action clear;
    private readonly Func<double?> audioPlayhead;
    private readonly Func<bool> needsAudio;
    private long generation;
    private double sourceAnchor,hostAnchor;
    private bool anchored;
    public bool IsCurrent=>current==this;
    public string? Fault {get;private set;}
    public event Action<string>? Failed;
    public long Dropped {get;private set;}
    internal readonly record struct DropCounters(long Total,long TimelineReset,long CapacityReset,long SourceDriftReset,long ShutdownReset,long FailureReset,long InvalidGeneration,long MissingAudioClock,long LateAudio,long DistantVideo,long DeliveryFailure)
    {
        public long ClassifiedTotal=>TimelineReset+CapacityReset+SourceDriftReset+ShutdownReset+FailureReset+InvalidGeneration+MissingAudioClock+LateAudio+DistantVideo+DeliveryFailure;
    }
    private enum ResetCause {Timeline,Capacity,SourceDrift,Shutdown,Failure}
    private long timelineResetDrops,capacityResetDrops,sourceDriftResetDrops,shutdownResetDrops,failureResetDrops,invalidGenerationDrops,missingAudioClockDrops,lateAudioDrops,distantVideoDrops,deliveryFailureDrops;
    // All mutations already occur under commitGate. Capture all reasons together
    // so concurrent pumping/reset/shutdown cannot produce a misleading partial sum.
    internal DropCounters ReadDropCounters(){lock(commitGate)return new(Dropped,timelineResetDrops,capacityResetDrops,sourceDriftResetDrops,shutdownResetDrops,failureResetDrops,invalidGenerationDrops,missingAudioClockDrops,lateAudioDrops,distantVideoDrops,deliveryFailureDrops);}
    public long Reanchors {get;private set;}
    public long PresentedVideo {get;private set;}
    public long PresentedAudio {get;private set;}
    public long DriverComparedVideo {get;private set;}
    public double MaximumQueueLatencySeconds {get;private set;}
    public double MaximumSchedulingErrorSeconds {get;private set;}
    public double MaximumSoftwareAvErrorSeconds {get;private set;}
    public int Pending {get{lock(gate)return queue.Count;}}
    public const int Capacity=32;
    public const double TargetLatencySeconds=.04;
    // Audio is the presentation clock. Old pictures are discarded instead of
    // publishing up to100ms behind it; no sample or reported metric is clamped.
    public const double MaximumVideoDistanceSeconds=.04;
    private readonly Func<double> now;
    private double Now=>now();
    public NetworkPlayout(Action clear,Func<double?>? audioPlayhead=null,Func<double>? clock=null,bool automatic=true,Func<bool>? needsAudio=null)
    {this.clear=clear;this.audioPlayhead=audioPlayhead??(()=>null);this.needsAudio=needsAudio??(()=>false);now=clock??(()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency);task=automatic?Task.Run(Run):Task.CompletedTask;}
    // Explicit client timeline/disconnect resets share this entrypoint; phase
    // snapshots distinguish normal shutdown from a reset during measurement.
    public void Reset(Action? update=null)=>ResetCore(ResetCause.Timeline,update);
    private void ResetCore(ResetCause cause,Action? update=null)
    {
        lock(commitGate){lock(gate){
            int count=queue.Count;Dropped+=count;
            switch(cause){case ResetCause.Timeline:timelineResetDrops+=count;break;case ResetCause.Capacity:capacityResetDrops+=count;break;case ResetCause.SourceDrift:sourceDriftResetDrops+=count;break;case ResetCause.Shutdown:shutdownResetDrops+=count;break;case ResetCause.Failure:failureResetDrops+=count;break;}
            queue.Clear();anchored=false;generation++;
        }update?.Invoke();clear();}
    }
    public void Enqueue(double source,bool audio,Action deliver)
    {
        if(!double.IsFinite(source)||source<0)throw new InvalidDataException("Invalid source time");
        ResetCause? reset;
        lock(gate)reset=queue.Count>=Capacity?ResetCause.Capacity:anchored&&Math.Abs((Now-hostAnchor)-(source-sourceAnchor))>.25?ResetCause.SourceDrift:null;
        if(reset.HasValue)ResetCore(reset.Value);
        lock(gate)
        {
            double now=Now;
            if(!anchored){sourceAnchor=source;hostAnchor=now;anchored=true;Reanchors++;}
            queue.Add(new(source,now,audio,generation,deliver));queue.Sort((a,b)=>a.Source.CompareTo(b.Source));
        }
    }
    private async Task Run()
    {
        try{while(!stop.IsCancellationRequested)
        {
            if(!PumpOnce())await Task.Delay(2,stop.Token);
        }}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        catch(Exception e){Fault=e.ToString();Failed?.Invoke(Fault);}
    }
    public bool PumpOnce()
    {
        Item? selected=null;double error=0;double? playhead=audioPlayhead();double sampleAt=CaptureDiagnostics?Now:0,selectedDue=0;
            lock(gate)
            {
                double now=Now;
                for(int i=0;i<queue.Count;i++)
                {
                    var item=queue[i];double due=hostAnchor+item.Source-sourceAnchor+TargetLatencySeconds;
                    if(now<due)continue;
                    // Audio is submitted ahead to the driver. Video waits for that driver's
                    // consumed-byte source playhead, including source/resampler/device queues.
                    if(!item.Audio&&needsAudio()&&!playhead.HasValue&&now-item.Arrival<.25)continue;
                    if(!item.Audio&&playhead.HasValue&&item.Source>playhead.Value+.008&&now-item.Arrival<.25)continue;
                    queue.RemoveAt(i);selected=item;error=now-due;selectedDue=due;
                    MaximumQueueLatencySeconds=Math.Max(MaximumQueueLatencySeconds,now-item.Arrival);
                    if(item.Audio||!playhead.HasValue)MaximumSchedulingErrorSeconds=Math.Max(MaximumSchedulingErrorSeconds,error);
                    break;
                }
            }
            if(selected!=null)
            {
                BeforeCommit?.Invoke();
                lock(commitGate){
                bool valid;lock(gate)valid=selected.Generation==generation;
                bool observe=CaptureDiagnostics&&!selected.Audio,delivered=false;double commitAt=observe?Now:0;double? commitPlayhead=!selected.Audio?audioPlayhead():null;
                try{
                // Preserve the original short-circuit decision order. Reasons are
                // disjoint: one rejected item increments exactly one reason and Total.
                bool reject=true;
                if(!valid)invalidGenerationDrops++;
                else if(!selected.Audio&&needsAudio()&&!commitPlayhead.HasValue)missingAudioClockDrops++;
                else if(selected.Audio&&error>.1)lateAudioDrops++;
                else if(!selected.Audio&&commitPlayhead.HasValue&&Math.Abs(commitPlayhead.Value-selected.Source)>=MaximumVideoDistanceSeconds)distantVideoDrops++;
                else reject=false;
                if(reject){Dropped++;}
                else
                {
                    try
                    {
                        current=this;selected.Deliver();delivered=true;
                        if(selected.Audio)PresentedAudio++;else{
                            PresentedVideo++;
                            // Account for time spent waiting for the commit lock
                            // AND in the delivery callback. A slow callback can
                            // still violate the bound and must remain visible.
                            double? completed=audioPlayhead();
                            if(commitPlayhead.HasValue){DriverComparedVideo++;MaximumSoftwareAvErrorSeconds=Math.Max(MaximumSoftwareAvErrorSeconds,Math.Abs(commitPlayhead.Value-selected.Source));}
                            if(completed.HasValue)MaximumSoftwareAvErrorSeconds=Math.Max(MaximumSoftwareAvErrorSeconds,Math.Abs(completed.Value-selected.Source));
                        }
                    }
                    catch(Exception e){Fault=e.ToString();Failed?.Invoke(Fault);Dropped++;deliveryFailureDrops++;ResetCore(ResetCause.Failure);}
                    finally{current=null;}
                }
                }finally{if(observe){double? completedPlayhead=audioPlayhead();var observation=new Observation(++observationSequence,selected.Generation,selected.Source,selected.Arrival,selectedDue,sampleAt,commitAt,Now,playhead,commitPlayhead,completedPlayhead,delivered,valid,error);if(observations.Count==4096)observations.Dequeue();observations.Enqueue(observation);}}
                }
            }

        return selected!=null;
    }
    public void Dispose(){stop.Cancel();if(IsCurrent)return;task.GetAwaiter().GetResult();ResetCore(ResetCause.Shutdown);stop.Dispose();}
}

/// <summary>UI reads and epoch publication share one short commit lock. Old callbacks cannot republish after invalidation.</summary>
public sealed class EpochPublication<T> where T:class
{
    private readonly object gate=new();
    private NetworkTimeline? timeline;
    private T? value;
    public void Update(NetworkTimeline next){lock(gate){if(next.InvalidatePicture)value=null;timeline=next;}}
    public bool Publish(NetworkTimeline identity,T owned){lock(gate){if(identity!=timeline)return false;value=owned;return true;}}
    public T? Read(NetworkTimeline? expected){lock(gate)return timeline==expected?value:null;}
    public void Clear(){lock(gate){timeline=null;value=null;}}
}
