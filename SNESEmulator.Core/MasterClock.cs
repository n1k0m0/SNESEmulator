namespace SNESEmulator.Core;

public enum HardwareEventKind { Refresh, LineEnd, VblankNmi, External, CpuStall, Device }
public sealed record HardwareEvent(long Clock,int Priority,long Sequence,HardwareEventKind Kind,int Payload=0);
public sealed record ClockState(long Now,int H,int V,bool Field,long Fields,bool Interlace,long ApuTicks,long ApuRemainder,
    long StallUntil,long NextSequence,HardwareEvent[] Events,int VblankLine=225);

/// <summary>Integer clock and stable typed-event queue. Events at a bus endpoint precede the bus effect.</summary>
public sealed class MasterClock(HardwareProfile profile)
{
    public HardwareProfile Profile {get;}=profile;
    public long Now {get;private set;}
    public int H {get;private set;}
    public int V {get;private set;}
    public bool Field {get;private set;}
    public long Fields {get;private set;}
    public bool Interlace {get;set;}
    public long ApuTicks {get;private set;}
    public long ApuRemainder {get;private set;}
    public long StallUntil {get;private set;}
    private long sequence;
    private readonly List<HardwareEvent> events=[];
    public Action? RefreshStarted {get;set;}
    public Action<HardwareEvent>? DeviceEvent {get;set;}
    public Action<HardwareEvent>? EventObserved {get;set;}
    public Action<int>? LineCompleted {get;set;}
    public Action<int>? LineStarted {get;set;}
    public Action? FieldStarted {get;set;}
    public Action? VblankStarted {get;set;}
    public int VblankLine {get;set;}=225;
    public int Dot
    {
        get
        {
            if(Profile.LineClocks(V,Field,Interlace)==1360)return H/4;
            // Dots 323 and 327 are six clocks; remaining dots are four.
            int h=H;if(h>=1292)h-=Math.Min(2,h-1292);if(H>=1310)h-=Math.Min(2,H-1310);return h/4;
        }
    }
    public void Reset()
    {Profile.Validate();Now=H=V=0;Field=false;Fields=ApuTicks=ApuRemainder=StallUntil=sequence=0;Interlace=false;VblankLine=225;events.Clear();ScheduleLine();}
    public void Schedule(long at,HardwareEventKind kind,int payload=0,int priority=0)
    {if(at<Now)throw new ArgumentOutOfRangeException(nameof(at));events.Add(new(at,priority,sequence++,kind,payload));events.Sort(Compare);}
    private static int Compare(HardwareEvent a,HardwareEvent b) {int n=a.Clock.CompareTo(b.Clock);if(n!=0)return n;n=a.Priority.CompareTo(b.Priority);return n!=0?n:a.Sequence.CompareTo(b.Sequence);}
    private void ScheduleLine()
    {
        // CPU revision 2 refresh phase is a free-running eight-master-clock divider.
        long lineStart=Now-H;int phase=(int)((lineStart+536-2)&7);int refresh=536-phase;if(refresh<534)refresh+=8;
        Schedule(lineStart+refresh,HardwareEventKind.Refresh,priority:-10);
        Schedule(lineStart+Profile.LineClocks(V,Field,Interlace),HardwareEventKind.LineEnd,priority:-20);
        if(V==VblankLine)Schedule(lineStart+2,HardwareEventKind.VblankNmi,priority:-5);
    }
    public void AdvanceTo(long target)
    {
        if(target<Now)throw new ArgumentOutOfRangeException(nameof(target));
        while(events.Count>0&&events[0].Clock<=target)
        {
            var e=events[0];events.RemoveAt(0);AdvanceRaw(e.Clock);
            switch(e.Kind)
            {
                case HardwareEventKind.Device:DeviceEvent?.Invoke(e);break;
                case HardwareEventKind.Refresh:StallUntil=Math.Max(StallUntil,Now+40);RefreshStarted?.Invoke();break;
                case HardwareEventKind.CpuStall:StallUntil=Math.Max(StallUntil,Now+e.Payload);break;
                case HardwareEventKind.LineEnd:
                    LineCompleted?.Invoke(V);H=0;V++;
                    if(V==Profile.FieldLines(Field,Interlace)){V=0;Field=!Field;Fields++;FieldStarted?.Invoke();}
                    LineStarted?.Invoke(V);
                    ScheduleLine();break;
                case HardwareEventKind.VblankNmi:VblankStarted?.Invoke();break;
            }
            EventObserved?.Invoke(e);
        }
        AdvanceRaw(target);
    }
    private void AdvanceRaw(long to)
    {
        long delta=to-Now;
        Int128 sum=(Int128)ApuRemainder+(Int128)delta*Profile.ApuHzNumerator*Profile.MasterHzDenominator;
        long denominator=checked(Profile.MasterHzNumerator*Profile.ApuHzDenominator);
        ApuTicks+=(long)(sum/denominator);ApuRemainder=(long)(sum%denominator);H=checked(H+(int)delta);Now=to;
    }
    public long NextEventClock => events.Count==0?long.MaxValue:events[0].Clock;
    public long ClocksUntilFieldEnd
    {
        get
        {
            long remaining=events.Single(e=>e.Kind==HardwareEventKind.LineEnd).Clock-Now;
            for(int line=V+1;line<Profile.FieldLines(Field,Interlace);line++)remaining+=Profile.LineClocks(line,Field,Interlace);
            return remaining;
        }
    }
    public ClockState Capture()=>new(Now,H,V,Field,Fields,Interlace,ApuTicks,ApuRemainder,StallUntil,sequence,events.ToArray(),VblankLine);
    public void Restore(ClockState s)
    {
        if(s.Now<0||s.H<0||s.H>=Profile.LineClocks(s.V,s.Field,s.Interlace)||s.V<0||s.V>=Profile.FieldLines(s.Field,s.Interlace)||s.Fields<0||s.ApuTicks<0||s.ApuRemainder<0||s.ApuRemainder>=Profile.MasterHzNumerator*Profile.ApuHzDenominator||s.Events.Any(e=>e.Clock<s.Now)||s.Events.Select(e=>e.Sequence).Distinct().Count()!=s.Events.Length||s.Events.Any(e=>e.Sequence>=s.NextSequence))throw new InvalidDataException("Invalid clock state");
        if(s.VblankLine is not (225 or 240))throw new InvalidDataException("Invalid Vblank line");
        Now=s.Now;H=s.H;V=s.V;Field=s.Field;Fields=s.Fields;Interlace=s.Interlace;ApuTicks=s.ApuTicks;ApuRemainder=s.ApuRemainder;StallUntil=s.StallUntil;sequence=s.NextSequence;VblankLine=s.VblankLine;events.Clear();events.AddRange(s.Events);events.Sort(Compare);
    }
}
