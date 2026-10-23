namespace SNESEmulator.Runtime;

/// <summary>UI ticks schedule at most one off-thread save, without pausing the owner.</summary>
public sealed class BatteryAutosave(BatteryPersistence persistence,Action<string> error,double intervalSeconds=30)
{
    private double elapsed;
    public Task Pending {get;private set;}=Task.CompletedTask;
    public long Attempts {get;private set;}
    public void Tick(double seconds,bool enabled)
    {
        elapsed=Math.Min(intervalSeconds,elapsed+Math.Max(0,seconds));
        if(!enabled||elapsed<intervalSeconds||!Pending.IsCompleted)return;
        elapsed=0;Attempts++;
        Pending=Task.Run(async()=>{try{await persistence.FlushAsync().ConfigureAwait(false);}catch(Exception e){error("SRAM SAVE FAILED: "+e.Message);}});
    }
}
