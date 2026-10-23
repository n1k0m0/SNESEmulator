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
    private SnesSystem machine;
    private bool paused=true,turbo,disposed,exited;
    private string? fault;
    private long epoch=1,dropped,sequence,appliedSequence,hardwareGeneration,inputEpoch;
    private long deadline;
    private FieldInput input;
    private FieldOutput? latest;
    private WorkerStatus status=new(1,0,0,"","",true,false,null,0,0,0,0,24576000,1);
    private interface ICommand {void Execute();void Cancel();}
    private sealed class Command<T>(Func<T> action,CancellationToken cancellation) : ICommand
    {
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
    private Task<T> Enqueue<T>(Func<T> action,CancellationToken cancellation=default)
    {
        var command=new Command<T>(()=>{var result=action();PublishStatus();return result;},cancellation);
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
    public Task InsertAsync(byte[] rom,byte[]? sram=null,string source="",CancellationToken cancellation=default)
    {
        byte[] ownRom=rom.ToArray(),ownSram=sram?.ToArray()??[];
        return Enqueue(()=>{var cart=new Cartridge(ownRom,source);if(ownSram.Length!=0){if(ownSram.Length!=cart.Sram.Length)throw new InvalidDataException("Battery size mismatch");ownSram.CopyTo(cart.Sram,0);}
            machine.Insert(cart);fault=null;Discontinuity();return true;},cancellation);
    }
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
            return (Cart:new Cartridge(cart.Rom,cart.SourcePath,cart.Options),Epoch:epoch);
        },cancellation).ConfigureAwait(false);
        var restored=await Task.Run(()=>{cancellation.ThrowIfCancellationRequested();using var stream=new MemoryStream(owned,false);return StateSerializer.Load(identity.Cart,stream);},cancellation).ConfigureAwait(false);
        BeforeStateInstall?.Invoke();cancellation.ThrowIfCancellationRequested();
        await Enqueue(()=>{
            if(epoch!=identity.Epoch||machine.Bus.Cartridge?.Hash!=identity.Cart.Hash)throw new InvalidOperationException("Machine changed while loading; retry the state load.");
            machine=restored;fault=null;Discontinuity();return true;
        },cancellation).ConfigureAwait(false);
    }
    public Task<byte[]> CaptureAsync(CancellationToken cancellation=default)=>Enqueue(()=>StateSerializer.Capture(machine),cancellation);
    public Task<BatterySnapshot?> BatteryAsync(CancellationToken cancellation=default)=>Enqueue(()=>machine.Bus.Cartridge is {} cart?new BatterySnapshot(cart.Hash,cart.Sram.ToArray()):null,cancellation);
    public Task<FieldOutput> StepAsync(FieldInput value,CancellationToken cancellation=default)=>Enqueue(()=>{
        if(!paused)throw new InvalidOperationException("Deterministic stepping requires pause");
        if(machine.Bus.Cartridge==null)throw new InvalidOperationException("Load a cartridge first");
        return Emulate(value,++appliedSequence);
    },cancellation);
    public bool TryDequeue(out FieldOutput? output){lock(gate){if(outputs.TryDequeue(out var found)){output=found;return true;}output=null;return false;}}
    private void Discontinuity(bool keepFrame=false,bool inputBoundary=true)
    {
        epoch++;if(!keepFrame)hardwareGeneration++;if(inputBoundary)inputEpoch++;machine.Bus.Apu.Dsp.ClearPresentation();
        lock(gate){outputs.Clear();if(inputBoundary){input=default;sequence++;}}
        if(!keepFrame)Volatile.Write(ref latest,null);pacer.Rebase(clock.Timestamp);
        PublishStatus();
    }
    private FieldOutput Emulate(FieldInput value,long inputSequence)
    {
        machine.Bus.Controllers.Buttons[0]=value.Port1;machine.Bus.Controllers.Buttons[1]=value.Port2;
        long inputClock=machine.Clock.Now;machine.RunFrame();
        // CompleteField already allocates its own three planes. They are never
        // reused or written by PPU; handing these readonly planes across is safe.
        var frame=machine.Bus.Ppu.CompletedFrame??throw new InvalidOperationException("No completed raw field");
        var result=new FieldOutput(epoch,frame,machine.Bus.Apu.Dsp.DrainSamples(),inputSequence,inputClock,value,turbo,!paused&&(clock.Timestamp-deadline)/(double)clock.Frequency>.1);
        Volatile.Write(ref latest,result);
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
                // At most one batch; controls are checked between every field.
                for(int i=0;i<CommandCapacity;i++)
                {
                    if(stopping.IsCancellationRequested)break;
                    ICommand? command;lock(gate)command=commands.TryDequeue(out var c)?c:null;
                    if(command==null)break;
                    long oldEpoch=epoch;command.Execute();if(epoch!=oldEpoch)deadline=clock.Timestamp;
                }
                if(stopping.IsCancellationRequested)break;
                PublishStatus();
                if(paused||fault!=null||machine.Bus.Cartridge==null){changed.WaitOne(50);continue;}
                if(clock.Timestamp<deadline){clock.WaitUntil(deadline,changed,stopping.Token);continue;}
                FieldInput next;lock(gate){next=input;appliedSequence=sequence;}
                long before=machine.Clock.Now;
                try{Emulate(next,appliedSequence);deadline=pacer.Add(machine.Clock.Now-before,machine.Profile,clock.Frequency,turbo?4:1);}
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
