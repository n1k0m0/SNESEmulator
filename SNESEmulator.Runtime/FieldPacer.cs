using System.Diagnostics;
using SNESEmulator.Core;

namespace SNESEmulator.Runtime;

public interface IMonotonicClock
{
    long Timestamp {get;}
    long Frequency {get;}
    void WaitUntil(long deadline,WaitHandle changed,CancellationToken cancellation);
}

public sealed class MonotonicClock : IMonotonicClock
{
    public long Timestamp=>Stopwatch.GetTimestamp();
    public long Frequency=>Stopwatch.Frequency;
    public void WaitUntil(long deadline,WaitHandle changed,CancellationToken cancellation)
    {
        long remaining=deadline-Timestamp;
        if(remaining<=0)return;
        int ms=(int)Int128.Clamp(((Int128)remaining*1000+Frequency-1)/Frequency,1,50);
        WaitHandle.WaitAny([changed,cancellation.WaitHandle],ms);
    }
}

/// <summary>Cumulative rational deadlines, with no per-field rounding drift.</summary>
public sealed class FieldPacer
{
    private long origin;
    private Int128 clocks;
    public void Rebase(long timestamp){origin=timestamp;clocks=0;}
    public long Add(long elapsedMasterClocks,HardwareProfile profile,long hostFrequency,int speed=1)
    {
        if(elapsedMasterClocks<0||hostFrequency<=0||speed<1)throw new ArgumentOutOfRangeException();
        clocks+=elapsedMasterClocks;
        return checked(origin+(long)(clocks*profile.MasterHzDenominator*hostFrequency/(profile.MasterHzNumerator*(Int128)speed)));
    }
}
