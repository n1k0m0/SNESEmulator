namespace SNESEmulator.Runtime;

/// <summary>Transport status and encoding are independently fallible presentation sinks.</summary>
public sealed class IsolatedNetworkPresentation(Action<WorkerStatus> status,Action<FieldOutput,Func<WorkerStatus>> output,Action<string> error)
{
    public long Failures {get;private set;}
    public string? LastError {get;private set;}
    public void Update(WorkerStatus value)=>Run(()=>status(value));
    public void Present(FieldOutput field,Func<WorkerStatus> read)=>Run(()=>output(field,read));
    private void Run(Action sink)
    {
        try{sink();}
        catch(Exception e){Failures++;LastError=e.ToString();error("NETWORK PRESENTATION FAILED: "+e.Message);}
    }
}
