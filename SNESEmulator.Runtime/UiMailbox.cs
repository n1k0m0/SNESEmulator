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
