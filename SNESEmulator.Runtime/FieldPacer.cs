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
// Converts emulated fields to cumulative monotonic host deadlines.
// Pacing may omit presentation during catch-up but must not skip hardware execution.
//

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
    internal static int WaitTimeoutMilliseconds(long deadline,long now,long frequency)
    {
        if(frequency<=0)throw new ArgumentOutOfRangeException(nameof(frequency));
        // Compare/subtract in a wider domain: an already-past MinValue deadline
        // must not wrap into a positive50ms wait. Ordinary ceil/cap is unchanged.
        Int128 remaining=(Int128)deadline-now;
        return remaining<=0?0:(int)Int128.Clamp((remaining*1000+frequency-1)/frequency,1,50);
    }
    public void WaitUntil(long deadline,WaitHandle changed,CancellationToken cancellation)=>WaitCore(deadline,changed,cancellation,false);
    internal ClockWaitTiming WaitObserved(long deadline,WaitHandle changed,CancellationToken cancellation)=>WaitCore(deadline,changed,cancellation,true);
    private ClockWaitTiming WaitCore(long deadline,WaitHandle changed,CancellationToken cancellation,bool observe)
    {
        long start=Timestamp;int ms=WaitTimeoutMilliseconds(deadline,start,Frequency);
        if(ms==0)return new(deadline,start,start,WaitReturnReason.DeadlineReached);
        int result=WaitHandle.WaitAny([changed,cancellation.WaitHandle],ms);
        // Return diagnostics directly to the caller, without mutable clock state;
        // injected/shared clock instances retain independent caller observations.
        return observe?new(deadline,start,Timestamp,result==0?WaitReturnReason.Changed:result==1?WaitReturnReason.Cancelled:WaitReturnReason.Timeout):default;
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
