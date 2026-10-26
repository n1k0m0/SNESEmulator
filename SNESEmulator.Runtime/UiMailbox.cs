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
// Shutdown cancels pending work instead of executing callbacks after UI disposal.
//

using System.Collections.Concurrent;

namespace SNESEmulator.Runtime;

/// <summary>Critical completions apply backpressure; status bursts coalesce separately.</summary>
public sealed class UiMailbox(int capacity=16) : IDisposable
{
    private readonly ConcurrentQueue<Action> completions=new();
    private readonly SemaphoreSlim slots=new(capacity,capacity);
    private string? status;
    public int Pending=>completions.Count;
    public void SetStatus(string value)=>Interlocked.Exchange(ref status,value);
    public string? TakeStatus()=>Interlocked.Exchange(ref status,null);
    public async Task PostAsync(Action action,CancellationToken cancellation=default)
    {
        await slots.WaitAsync(cancellation).ConfigureAwait(false);completions.Enqueue(action);
    }
    public Task PostOutcomeAsync(Action action,Func<string> visibleStatus,CancellationToken cancellation=default)
        =>PostAsync(()=>{action();SetStatus(visibleStatus());},cancellation);
    public void Drain()
    {
        for(int i=0;i<capacity;i++){if(!completions.TryDequeue(out var action))break;slots.Release();action();}
    }
    public void Dispose()=>slots.Dispose();
}
