using System.Runtime.InteropServices;

namespace SNESEmulator.Runtime;

public enum OwnerTimingKind { CommandBatch,Command,Wait,Field }
public enum WaitReturnReason { DeadlineReached,Changed,Cancelled,Timeout,ExternalClock }
public readonly record struct ClockWaitTiming(long Deadline,long Start,long Wake,WaitReturnReason Reason);
public readonly record struct OwnerFieldWork(long MasterClocks,long CpuInstructions,long SpcInstructions,long DspSamples,int Width,int Height);
public readonly record struct OwnerThreadTime(bool Available,long User100ns,long Kernel100ns,ulong Cycles,int Processor)
{
    // Own current-thread counters only. Units are100ns; effective OS resolution
    // is coarser. Cycle counts are not instruction counts or a fixed CPU clock.
    internal static OwnerThreadTime Read(){
        if(!OperatingSystem.IsWindows())return default;
        IntPtr thread=GetCurrentThread();
        bool times=GetThreadTimes(thread,out _,out _,out long kernel,out long user);
        bool cycles=QueryThreadCycleTime(thread,out ulong count);
        return new(times&&cycles,user,kernel,count,Thread.GetCurrentProcessorId());
    }
    [DllImport("kernel32.dll")]private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetThreadTimes(IntPtr thread,out long creation,out long exit,out long kernel,out long user);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool QueryThreadCycleTime(IntPtr thread,out ulong cycles);
}
public readonly record struct OwnerTimingPoint(long Sequence,OwnerTimingKind Kind,long Field,long InputSequence,long Start,long End,long ScheduledStart,long ScheduledEnd,long RunFrameEnd,bool CatchingUp,WaitReturnReason WaitReason,bool BeforeDeadline,int Commands,string? CommandName,long OwnerAllocatedBefore,long OwnerAllocatedAfter,long GcPauseBefore,long GcPauseAfter,OwnerThreadTime ThreadBefore=default,OwnerThreadTime ThreadAfter=default,OwnerFieldWork Work=default);
public sealed record OwnerTimingSnapshot(long Frequency,long Total,long Overwritten,long? ReadyAt,OwnerTimingPoint? FirstCatchup,OwnerTimingPoint? FirstSteadyCatchup,OwnerTimingPoint? MaximumWaitOvershoot,OwnerTimingPoint? MaximumField,OwnerTimingPoint[] FirstSteadyContext,OwnerTimingPoint[] Recent);

/// <summary>Opt-in owner writes only bounded value records. JSON and snapshots belong to readers.</summary>
internal sealed class OwnerTimingRecorder(long frequency)
{
    internal const int Capacity=1024,ContextCapacity=64;
    private readonly object gate=new();
    private readonly OwnerTimingPoint[] ring=new OwnerTimingPoint[Capacity],context=new OwnerTimingPoint[ContextCapacity];
    private long total;private int count,next,contextCount;
    private long? readyAt;private OwnerTimingPoint? first,steady,maximumWait,maximumField;
    internal OwnerTimingPoint? FirstSteady{get{lock(gate)return steady;}}
    internal void MarkReady(long at){lock(gate)readyAt??=at;}
    internal void Add(OwnerTimingPoint point)
    {
        lock(gate){
            point=point with{Sequence=++total};
            if(point.Kind==OwnerTimingKind.Wait&&point.End>=point.ScheduledEnd&&(!maximumWait.HasValue||point.End-point.ScheduledEnd>maximumWait.Value.End-maximumWait.Value.ScheduledEnd))maximumWait=point;
            if(point.Kind==OwnerTimingKind.Field){
                if(!maximumField.HasValue||point.End-point.Start>maximumField.Value.End-maximumField.Value.Start)maximumField=point;
                if(point.CatchingUp){first??=point;
                    if(!steady.HasValue&&readyAt is{} at&&point.Start>=at){
                        steady=point;contextCount=Math.Min(count,ContextCapacity-1);
                        for(int i=0;i<contextCount;i++)context[i]=ring[(next-contextCount+i+Capacity)%Capacity];
                        context[contextCount++]=point;
                    }
                }
            }
            ring[next]=point;next=(next+1)%Capacity;count=Math.Min(count+1,Capacity);
        }
    }
    internal OwnerTimingSnapshot Snapshot(){lock(gate){var recent=new OwnerTimingPoint[count];for(int i=0;i<count;i++)recent[i]=ring[(next-count+i+Capacity)%Capacity];return new(frequency,total,Math.Max(0,total-Capacity),readyAt,first,steady,maximumWait,maximumField,context.AsSpan(0,contextCount).ToArray(),recent);}}
}
