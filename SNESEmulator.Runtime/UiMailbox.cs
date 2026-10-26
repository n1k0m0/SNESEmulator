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
// Marshals bounded work back to the frontend owner thread.
// Shutdown rejects pending work instead of executing callbacks after UI disposal.
//

using System.Threading.Channels;

namespace SNESEmulator.Runtime;

/// <summary>Critical completions apply backpressure; status bursts coalesce separately.</summary>
public sealed class UiMailbox(int capacity=16) : IDisposable
{
    private readonly object gate=new();
    private readonly Channel<Action> completions=Channel.CreateBounded<Action>(new BoundedChannelOptions(capacity){FullMode=BoundedChannelFullMode.Wait,AllowSynchronousContinuations=false});
    private bool disposed;
    private string? status;
    public int Pending=>completions.Reader.Count;
    public void SetStatus(string value){lock(gate){if(!disposed)Interlocked.Exchange(ref status,value);}}
    public string? TakeStatus()=>Interlocked.Exchange(ref status,null);
    public async Task PostAsync(Action action,CancellationToken cancellation=default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed),this);
        // Completing a channel releases blocked writers. Disposing a semaphore
        // alone strands its WaitAsync tasks, even when no UI will ever drain again.
        try{await completions.Writer.WriteAsync(action,cancellation).ConfigureAwait(false);}
        catch(ChannelClosedException){throw new ObjectDisposedException(nameof(UiMailbox));}
    }
    public Task PostOutcomeAsync(Action action,Func<string> visibleStatus,CancellationToken cancellation=default)
        =>PostAsync(()=>{action();SetStatus(visibleStatus());},cancellation);
    public void Drain()
    {
        // Serialize admission with close, but invoke outside the gate: an action
        // can await I/O whose error/status producer needs this same mailbox.
        // Already admitted work may finish; reentrant close prevents later admission.
        for(int i=0;i<capacity;i++){
            Action? action;
            lock(gate){if(disposed||!completions.Reader.TryRead(out action))break;}
            action();
        }
    }
    public void Dispose()
    {
        lock(gate){
            if(disposed)return;
            Volatile.Write(ref disposed,true);completions.Writer.TryComplete();
            while(completions.Reader.TryRead(out _)){}
            Interlocked.Exchange(ref status,null);
        }
    }
}
