using SNESEmulator.Core;

namespace SNESEmulator.Runtime;

/// <summary>Bounded-output discontinuities and independent presentation failures.</summary>
public sealed class PresentationHandoff(Action<PpuFrame> video,Action<FieldOutput> audio,Action clear,Action<string> error)
{
    private long epoch=-1,end=-1;
    private bool videoFailed,audioFailed;
    public void UpdateEpoch(long value)
    {
        if(epoch==value)return;
        epoch=value;end=-1;videoFailed=audioFailed=false;
        Clear();
    }
    private void Clear(){try{clear();}catch(Exception e){audioFailed=true;error("AUDIO MUTED UNTIL NEXT TIMELINE: "+e.Message);}}
    public void Present(FieldOutput output,long currentEpoch)
    {
        UpdateEpoch(currentEpoch);
        if(output.Epoch!=epoch)return;
        if(!videoFailed)try{video(output.Video);}catch(Exception e){videoFailed=true;error("NETWORK VIDEO MUTED UNTIL NEXT TIMELINE: "+e.Message);}
        if(end>=0&&end!=output.Video.Geometry.StartClock)Clear();
        end=output.Video.Geometry.EndClock;
        bool supported=(Int128)output.Video.Geometry.ApuHzNumerator==24576000*(Int128)output.Video.Geometry.ApuHzDenominator;
        if(output.Turbo||output.CatchingUp||!supported){Clear();return;}
        if(!audioFailed)try{audio(output);}catch(Exception e){audioFailed=true;Clear();error("AUDIO MUTED UNTIL NEXT TIMELINE: "+e.Message);}
    }
}
