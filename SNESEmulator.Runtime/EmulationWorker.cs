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
// Owns the live SnesSystem on one worker thread and accepts bounded commands.
// Published frames/snapshots cross ownership boundaries; UI code never runs the core.
//

using SNESEmulator.Core;
using SNESEmulator.SaveStates;

[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("SNESEmulator.Core.Tests")]

namespace SNESEmulator.Runtime;

public sealed record WorkerStatus(long Epoch,long Field,long MasterClock,string Title,string CartridgeHash,
    bool Paused,bool Turbo,string? Fault,int PendingCommands,int PendingOutput,long DroppedPresentation,long AppliedInputSequence,
    long ApuHzNumerator,long ApuHzDenominator,double PacingLateSeconds=0,long HardwareGeneration=0,long InputEpoch=0)
{
    public bool AudioSupported=>(Int128)ApuHzNumerator==24576000*(Int128)ApuHzDenominator;
}
public sealed record FieldOutput(long Epoch,PpuFrame Video,ReadOnlyMemory<StereoSample> Audio,long InputSequence,long InputMasterClock,FieldInput Input,bool Turbo,bool CatchingUp=false);
public sealed record BatterySnapshot(string Hash,byte[] Bytes);
public readonly record struct FieldInput(ushort Port1,ushort Port2);

/// <summary>Exactly one thread owns the live machine. No machine escapes this class.</summary>
/// <summary>Owns all live hardware mutation and publishes bounded, completed field outputs.</summary>
public sealed class EmulationWorker : IDisposable
{
    public const int CommandCapacity=32,OutputCapacity=8;
    private readonly object gate=new(),loadGate=new();
    private Task? activeLoad;
    internal Action? BeforeStateInstall {get;set;}
    private readonly Queue<ICommand> commands=new();
    private readonly Queue<FieldOutput> outputs=new();
    private readonly AutoResetEvent changed=new(false);
    private readonly CancellationTokenSource stopping=new();
    private readonly Thread thread;
    private readonly IMonotonicClock clock;
    private readonly FieldPacer pacer=new();
    private OwnerTimingRecorder? timing;
    public void EnableTimingDiagnostics(){lock(gate){if(timing==null)Volatile.Write(ref timing,new(clock.Frequency));}}
    public OwnerTimingSnapshot? ReadTimingDiagnostics()=>Volatile.Read(ref timing)?.Snapshot();
    public OwnerTimingPoint? FirstSteadyTimingCatchup=>Volatile.Read(ref timing)?.FirstSteady;
    public void MarkTimingReady()=>Volatile.Read(ref timing)?.MarkReady(clock.Timestamp);
    private SnesSystem machine;
    private bool paused=true,turbo,disposed,exited;
    private string? fault;
    private long epoch=1,dropped,sequence,appliedSequence,hardwareGeneration,inputEpoch;
    private long deadline;
    private FieldInput input;
    private FieldOutput? latest;
    private WorkerStatus status=new(1,0,0,"","",true,false,null,0,0,0,0,24576000,1);
    private interface ICommand {string Name {get;}void Execute();void Cancel();}
    private sealed class Command<T>(Func<T> action,CancellationToken cancellation,string name) : ICommand
    {
        public string Name=>name;
        public readonly TaskCompletionSource<T> Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Execute(){if(cancellation.IsCancellationRequested){Completion.TrySetCanceled(cancellation);return;}try{Completion.TrySetResult(action());}catch(Exception e){Completion.TrySetException(e);}}
        public void Cancel()=>Completion.TrySetCanceled();
    }
    public EmulationWorker(HardwareProfile? profile=null,IMonotonicClock? clock=null)
    {
        this.clock=clock??new MonotonicClock();
        // Construction is before publication; all subsequent accesses are on thread.
        machine=new(profile);thread=new Thread(Run){IsBackground=true,Name="SNES emulation"};thread.Start();
    }
    public WorkerStatus Status=>Volatile.Read(ref status);
    public FieldOutput? Latest=>Volatile.Read(ref latest);
    public bool IsAlive=>thread.IsAlive;
    private Task<T> Enqueue<T>(Func<T> action,CancellationToken cancellation=default,[System.Runtime.CompilerServices.CallerMemberName]string name="")
    {
        var command=new Command<T>(()=>{var result=action();PublishStatus();return result;},cancellation,name);
        lock(gate){if(disposed||stopping.IsCancellationRequested)return Task.FromException<T>(new ObjectDisposedException(nameof(EmulationWorker)));
            if(exited)return Task.FromException<T>(new InvalidOperationException("Emulation owner stopped; restart the application. "+fault));
            if(commands.Count>=CommandCapacity)return Task.FromException<T>(new InvalidOperationException("Emulation command queue is full; retry after pending operation."));
            commands.Enqueue(command);changed.Set();}
        return command.Completion.Task;
    }
    public void SubmitInput(FieldInput value)
    {
        lock(gate){if(disposed)return;input=value;sequence++;changed.Set();}
    }
    // Live input is a latest-value mailbox, atomically sampled immediately before
    // the next RunFrame (including a loaded partial field). Replay uses StepAsync.
    public Task SetModeAsync(bool pause,bool fast,CancellationToken cancellation=default)=>Enqueue(()=>{
        if(paused!=pause||turbo!=fast){bool inputBoundary=paused!=pause;paused=pause;turbo=fast;Discontinuity(true,inputBoundary);}return true;
    },cancellation);
    public Task InsertAsync(byte[] rom,byte[]? sram=null,string source="",CancellationToken cancellation=default,ICartridgeConfiguration? hardware=null)
    {
        byte[] ownRom=rom.ToArray(),ownSram=sram?.ToArray()??[];
        return Enqueue(()=>{var cart=new Cartridge(ownRom,source,hardware:hardware);if(ownSram.Length!=0){if(ownSram.Length!=cart.Sram.Length)throw new InvalidDataException("Battery size mismatch");ownSram.CopyTo(cart.Sram,0);}
            machine.Insert(cart);fault=null;Discontinuity();return true;},cancellation);
    }
    public Task PowerCycleAsync(CancellationToken cancellation=default)=>Enqueue(()=>{machine.PowerOn();fault=null;Discontinuity();return true;},cancellation);
    public Task ResetAsync(CancellationToken cancellation=default)=>Enqueue(()=>{machine.Reset();fault=null;Discontinuity();return true;},cancellation);
    public Task LoadAsync(byte[] compressed,CancellationToken cancellation=default)
    {
        lock(loadGate)
        {
            if(Volatile.Read(ref disposed))return Task.FromException(new ObjectDisposedException(nameof(EmulationWorker)));
            if(activeLoad is {IsCompleted:false})return Task.FromException(new InvalidOperationException("A savestate load is already pending; wait for it to finish."));
            if(compressed.Length>StateSerializer.MaximumCompressedBytes)return Task.FromException(new InvalidDataException("Compressed savestate exceeds size limit."));
            activeLoad=LoadCoreAsync(compressed,cancellation);return activeLoad;
        }
    }
    private async Task LoadCoreAsync(byte[] compressed,CancellationToken cancellation)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,stopping.Token);cancellation=linked.Token;
        cancellation.ThrowIfCancellationRequested();
        if(compressed.Length>StateSerializer.MaximumCompressedBytes)throw new InvalidDataException("Compressed savestate exceeds size limit.");
        byte[] owned=compressed.ToArray();
        var identity=await Enqueue(()=>{
            var cart=machine.Bus.Cartridge??throw new InvalidOperationException("Load matching ROM first");
            return (Cart:new Cartridge(cart.Rom,cart.SourcePath,cart.Options,cart.HardwareConfiguration),Epoch:epoch);
        },cancellation).ConfigureAwait(false);
        var restored=await Task.Run(()=>{cancellation.ThrowIfCancellationRequested();using var stream=new MemoryStream(owned,false);return StateSerializer.Load(identity.Cart,stream);},cancellation).ConfigureAwait(false);
        BeforeStateInstall?.Invoke();cancellation.ThrowIfCancellationRequested();
        await Enqueue(()=>{
            if(epoch!=identity.Epoch||machine.Bus.Cartridge?.Hash!=identity.Cart.Hash)throw new InvalidOperationException("Machine changed while loading; retry the state load.");
            machine=restored;fault=null;Discontinuity();return true;
        },cancellation).ConfigureAwait(false);
    }
    /// <summary>Queues a state capture on the hardware owner instead of reading a concurrently running machine.</summary>
    public Task<byte[]> CaptureAsync(CancellationToken cancellation=default)=>Enqueue(()=>StateSerializer.Capture(machine),cancellation);
    public Task<BatterySnapshot?> BatteryAsync(CancellationToken cancellation=default)=>Enqueue(()=>machine.Bus.Cartridge is {} cart?new BatterySnapshot(cart.Hash,cart.Sram.ToArray()):null,cancellation);
    public Task<FieldOutput> StepAsync(FieldInput value,CancellationToken cancellation=default)=>Enqueue(()=>{
        if(!paused)throw new InvalidOperationException("Deterministic stepping requires pause");
        if(machine.Bus.Cartridge==null)throw new InvalidOperationException("Load a cartridge first");
        return Emulate(value,++appliedSequence);
    },cancellation);
    /// <summary>Transfers the next completed field output to the presentation consumer.</summary>
    public bool TryDequeue(out FieldOutput? output){lock(gate){if(outputs.TryDequeue(out var found)){output=found;return true;}output=null;return false;}}
    // A reset/load/session change invalidates queued presentation from the previous epoch.
    // Keep this separate from the hardware state restored by a snapshot.
    private void Discontinuity(bool keepFrame=false,bool inputBoundary=true)
    {
        epoch++;if(!keepFrame)hardwareGeneration++;if(inputBoundary)inputEpoch++;machine.Bus.Apu.Dsp.ClearPresentation();
        lock(gate){outputs.Clear();if(inputBoundary){input=default;sequence++;}}
        if(!keepFrame)Volatile.Write(ref latest,null);pacer.Rebase(clock.Timestamp);
        PublishStatus();
    }
    private FieldOutput Emulate(FieldInput value,long inputSequence,bool paced=false)
    {
        var diagnostic=Volatile.Read(ref timing);
        long started=diagnostic!=null?clock.Timestamp:0,scheduledStart=deadline,allocated=diagnostic!=null?GC.GetAllocatedBytesForCurrentThread():0,pause=diagnostic!=null?GC.GetTotalPauseDuration().Ticks:0;
        OwnerThreadTime threadBefore=diagnostic!=null?OwnerThreadTime.Read():default;
        long cpuBefore=diagnostic!=null?machine.Cpu.Instructions:0,spcBefore=diagnostic!=null?machine.Bus.Apu.Cpu.Instructions:0,dspBefore=diagnostic!=null?machine.Bus.Apu.Dsp.SampleCount:0;
        machine.Bus.Controllers.Buttons[0]=value.Port1;machine.Bus.Controllers.Buttons[1]=value.Port2;
        long inputClock=machine.Clock.Now;machine.RunFrame();long runFrameEnd=diagnostic!=null?clock.Timestamp:0;
        // CompleteField already allocates its own three planes. They are never
        // reused or written by PPU; handing these readonly planes across is safe.
        var frame=machine.Bus.Ppu.CompletedFrame??throw new InvalidOperationException("No completed raw field");
        var samples=machine.Bus.Apu.Dsp.DrainSamples();
        // deadline previously described this field's scheduled START. Comparing
        // completion against it also counted an ordinary field duration as lag.
        // Advance the same rational pacer exactly once, then classify lateness
        // against this field's scheduled END. Paused deterministic Step does not
        // advance the host pacer; hardware clocks and source timestamps are untouched.
        if(paced)deadline=pacer.Add(machine.Clock.Now-inputClock,machine.Profile,clock.Frequency,turbo?4:1);
        long completed=clock.Timestamp;
        var result=new FieldOutput(epoch,frame,samples,inputSequence,inputClock,value,turbo,!paused&&(completed-deadline)/(double)clock.Frequency>.1);
        diagnostic?.Add(new(0,OwnerTimingKind.Field,machine.Frames,inputSequence,started,completed,scheduledStart,deadline,runFrameEnd,result.CatchingUp,default,false,0,null,allocated,GC.GetAllocatedBytesForCurrentThread(),pause,GC.GetTotalPauseDuration().Ticks,threadBefore,OwnerThreadTime.Read(),new(machine.Clock.Now-inputClock,machine.Cpu.Instructions-cpuBefore,machine.Bus.Apu.Cpu.Instructions-spcBefore,machine.Bus.Apu.Dsp.SampleCount-dspBefore,frame.Geometry.Width,frame.Geometry.Height)));
        Volatile.Write(ref latest,result);
        // Overflow drops presentation history only. Core time, completed field
        // ownership and the source sample timestamps above have already committed.
        lock(gate){if(outputs.Count==OutputCapacity){dropped+=outputs.Count;outputs.Clear();}outputs.Enqueue(result);}
        return result;
    }
    private void PublishStatus()
    {
        lock(gate)Volatile.Write(ref status,new(epoch,machine.Frames,machine.Clock.Now,machine.Bus.Cartridge?.Title??"",machine.Bus.Cartridge?.Hash??"",paused,turbo,fault,commands.Count,outputs.Count,dropped,appliedSequence,machine.Profile.ApuHzNumerator,machine.Profile.ApuHzDenominator,paused?0:(clock.Timestamp-deadline)/(double)clock.Frequency,hardwareGeneration,inputEpoch));
    }
    private void Run()
    {
        deadline=clock.Timestamp;pacer.Rebase(deadline);
        try
        {
            while(!stopping.IsCancellationRequested)
            {
                var diagnostic=Volatile.Read(ref timing);long batchStart=diagnostic!=null?clock.Timestamp:0;int batchCount=0;
                // At most one batch; controls are checked between every field.
                for(int i=0;i<CommandCapacity;i++)
                {
                    if(stopping.IsCancellationRequested)break;
                    ICommand? command;lock(gate)command=commands.TryDequeue(out var c)?c:null;
                    if(command==null)break;
                    long commandStart=diagnostic!=null?clock.Timestamp:0,commandAllocated=diagnostic!=null?GC.GetAllocatedBytesForCurrentThread():0,commandPause=diagnostic!=null?GC.GetTotalPauseDuration().Ticks:0;
                    long oldEpoch=epoch;command.Execute();if(epoch!=oldEpoch)deadline=clock.Timestamp;batchCount++;
                    diagnostic?.Add(new(0,OwnerTimingKind.Command,machine.Frames,appliedSequence,commandStart,clock.Timestamp,deadline,deadline,0,false,default,false,1,command.Name,commandAllocated,GC.GetAllocatedBytesForCurrentThread(),commandPause,GC.GetTotalPauseDuration().Ticks));
                }
                if(batchCount>0)diagnostic?.Add(new(0,OwnerTimingKind.CommandBatch,machine.Frames,appliedSequence,batchStart,clock.Timestamp,deadline,deadline,0,false,default,false,batchCount,null,0,0,0,0));
                if(stopping.IsCancellationRequested)break;
                PublishStatus();
                if(paused||fault!=null||machine.Bus.Cartridge==null){changed.WaitOne(50);continue;}
                if(clock.Timestamp<deadline){
                    long start=diagnostic!=null?clock.Timestamp:0;
                    if(diagnostic!=null){
                        ClockWaitTiming wait;
                        if(clock is MonotonicClock actual)wait=actual.WaitObserved(deadline,changed,stopping.Token);
                        else{clock.WaitUntil(deadline,changed,stopping.Token);wait=new(deadline,start,clock.Timestamp,WaitReturnReason.ExternalClock);}
                        diagnostic.Add(new(0,OwnerTimingKind.Wait,machine.Frames,appliedSequence,wait.Start,wait.Wake,deadline,deadline,0,false,wait.Reason,wait.Wake<deadline,0,null,0,0,0,0));
                    }else clock.WaitUntil(deadline,changed,stopping.Token);
                    continue;
                }
                FieldInput next;lock(gate){next=input;appliedSequence=sequence;}
                try{Emulate(next,appliedSequence,paced:true);}
                catch(Exception e){fault=e.ToString();paused=true;Discontinuity();}
                PublishStatus();
            }
        }
        catch(Exception e){fault="Worker failed: "+e;paused=true;}
        finally{lock(gate){exited=true;while(commands.TryDequeue(out var command))command.Cancel();outputs.Clear();}PublishStatus();}
    }
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;stopping.Cancel();changed.Set();}
        if(!thread.Join(TimeSpan.FromSeconds(10)))throw new TimeoutException("Emulation worker did not stop at field boundary");
        Task? load;lock(loadGate)load=activeLoad;
        if(load!=null){try{load.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();}catch(Exception) when(load.IsCompleted){}}
        changed.Dispose();stopping.Dispose();
    }
}
