namespace SNESEmulator.Core;

public sealed record ExecutionPhase(int CycleRemaining,bool CycleActive,int HoldRemaining=0,CpuCycle? HeldRead=null,long CycleStart=0,long LatchClock=0,bool WriteAluDone=false);
public sealed record TimedCpuCycleEvent(string Phase,long MasterClock,long StartClock,long? LatchClock,CpuCycle Cycle);

public sealed class SnesSystem
{
    public SystemBus Bus {get;}=new();
    public Cpu65816 Cpu {get;}
    public HardwareProfile Profile {get;private set;}
    public MasterClock Clock {get;private set;}
    public long Frames {get=>Clock.Fields;set {if(value!=Clock.Fields)throw new InvalidOperationException("Frame count is clock-owned");}}
    public int MasterRemainder {get=>Clock.H;set {if(value!=Clock.H)throw new InvalidOperationException("Raster phase is clock-owned");}}
    private int cycleRemaining;
    private bool cycleActive;
    private int holdRemaining;
    private CpuCycle? heldRead;
    private long cycleStart,latchClock;
    public Action<TimedCpuCycleEvent>? CyclePhaseObserved {get;set;}
    private long target;
    private bool writeAluDone;
    private readonly bool forcedProfile;
    public bool ProfileForced=>forcedProfile;
    private sealed class ClockBudget : Exception;
    private readonly ClockBudget budget=new();
    public SnesSystem(HardwareProfile? profile=null,bool? forceProfile=null)
    {forcedProfile=forceProfile??profile!=null;Profile=profile??HardwareProfile.Ntsc;Clock=new(Profile);Cpu=new(Bus);ConfigureClock();Bus.Reset();Clock.Reset();Cpu.BeforeCycle=BeforeCycle;Cpu.EffectCompleted=OnBusEffect;Cpu.AfterCycle=AfterCycle;Cpu.ResumeCycle=FinishReadHold;Cpu.InterruptPollingAllowed=()=>!Bus.CpuIo.State.IrqLock;}
    private void ConfigureClock()
    {
        Clock.PeripheralBoundary=()=>{
            long tick=Bus.Apu.NextBoundary;if(tick==long.MaxValue)return Bus.Ppu.NextBoundary;
            Int128 numerator=(Int128)tick*Profile.MasterHzNumerator*Profile.ApuHzDenominator;
            long denominator=checked(Profile.ApuHzNumerator*Profile.MasterHzDenominator);
            return Math.Min(Bus.Ppu.NextBoundary,(long)((numerator+denominator-1)/denominator));
        };
        Clock.PeripheralAdvanced=SynchronizeDevices;
        Clock.RefreshStarted=()=>{for(int i=1;i<=5;i++)Clock.Schedule(Clock.Now+i*8,HardwareEventKind.Device,1,-6);};
        Clock.InterlaceInput=()=>Bus.Ppu.FrameGeometry.Interlace;
        Bus.Ppu.Clock=Clock;
        Bus.CpuIo.Clock=Clock;Bus.DeferCpuMdr=true;
        Clock.DeviceEvent=e=>{if((e.Payload&255)==7)Bus.Dma.RequestHdma(e.Payload==7);else Bus.CpuIo.Event(e.Payload);};
        Clock.LineStarted=line=>{Bus.Ppu.StartLine(line);ScheduleIoLine();};
        Clock.FieldStarted=()=>{Bus.Ppu.CompleteField();Bus.CpuIo.NewField();Bus.Ppu.Scanline=0;};
        Clock.VblankStarted=()=>Bus.BeginVblank();
    }
    private void SynchronizeDevices()
    {
        // Both pinned cycle renderers catch the PPU up with a strict '<'
        // comparison before MMIO. A phase exactly on the bus endpoint remains
        // pending until that CPU/DMA effect commits. The condition is derived
        // entirely from the already serialized CPU/DMA remaining-cycle state.
        bool busEndpoint=Clock.StallUntil<=Clock.Now&&((cycleActive&&cycleRemaining==0)||(Bus.Dma.Busy&&Bus.Dma.State.Remaining==0));
        if(!busEndpoint)Bus.Ppu.Synchronize(false);
        Bus.Apu.RunUntil(Clock.ApuTicks);Bus.Ppu.PublishNotifications();
    }
    private void CommitPpuEndpoint(){Bus.Ppu.Synchronize(false);Bus.Ppu.PublishNotifications();}
    private void ScheduleIoLine()
    {
        Bus.CpuIo.ScheduleIrq(false);
        if(Clock.V==0)Clock.Schedule(Clock.Now+12+(Clock.Now&7),HardwareEventKind.Device,7,-2);
        if(Clock.V<Clock.VblankLine)Clock.Schedule(Clock.Now+1104,HardwareEventKind.Device,0x107,-2);
    }
    public void Insert(Cartridge cartridge)
    {Bus.Cartridge=cartridge;if(!forcedProfile&&cartridge.Pal!=Profile.Pal){Profile=cartridge.Pal?HardwareProfile.PalProfile:HardwareProfile.Ntsc;Clock=new(Profile);ConfigureClock();}PowerOn();}
    public void PowerOn()=>ResetCore(true);
    public void Reset()=>ResetCore(false);
    private void ResetCore(bool powerOn)
    {
        Bus.Reset(powerOn);Cpu.BeforeCycle=null;Cpu.EffectCompleted=null;Cpu.AfterCycle=null;Cpu.ResumeCycle=null;Cpu.Reset();Cpu.BeforeCycle=BeforeCycle;Cpu.EffectCompleted=OnBusEffect;Cpu.AfterCycle=AfterCycle;Cpu.ResumeCycle=FinishReadHold;
        Clock.Reset();cycleRemaining=holdRemaining=0;cycleActive=false;heldRead=null;cycleStart=latchClock=0;writeAluDone=false;ScheduleIoLine();
    }
    public ExecutionPhase CapturePhase()=>new(cycleRemaining,cycleActive,holdRemaining,heldRead,cycleStart,latchClock,writeAluDone);
    public void RestorePhase(ExecutionPhase p)
    {
        if(p.CycleRemaining is <0 or >12||p.HoldRemaining is <0 or >4||p.CycleStart<0||p.LatchClock<0||(p.HeldRead!=null&&(!p.HeldRead.Valid||!p.HeldRead.Read))||(p.HoldRemaining>0&&p.HeldRead==null))throw new InvalidDataException("Invalid CPU phase");
        cycleRemaining=p.CycleRemaining;cycleActive=p.CycleActive;holdRemaining=p.HoldRemaining;heldRead=p.HeldRead;cycleStart=p.CycleStart;latchClock=p.LatchClock;writeAluDone=p.WriteAluDone;
    }
    private void SyncInterrupts()
    {
        Cpu.SetInterruptLines(Bus.CpuIo.State.NmiOutput,Bus.CpuIo.IrqOutput||Bus.ExternalIrqLine);
        if(Bus.NmiPending){Cpu.PulseNmi();Bus.NmiPending=false;}
    }
    private void BeforeCycle(CpuCycle cycle)
    {
        if(cycle.Pins=="--------"){cycleStart=Clock.Now;return;}
        if(!cycleActive){
            if(cycle.Valid&&!cycle.Read&&!writeAluDone){Bus.CpuIo.AluEdge();writeAluDone=true;}
            int clocks=Profile.AccessClocks(cycle,(Bus.Io[13]&1)!=0);
            if(Bus.Dma.CpuBoundary(clocks,Clock.Now))AdvanceDma();
            cycleStart=Clock.Now;cycleRemaining=clocks-(cycle.Valid&&cycle.Read?4:0);cycleActive=true;
        }
        AdvanceActive(ref cycleRemaining,false);
        cycleActive=false;
    }
    private void OnBusEffect(CpuCycle cycle)
    {
        Bus.CpuIo.State.IrqLock=false;
        if(cycle.Valid&&!cycle.Read&&cycle.Address is {} a&&(a&0x40ffff)==0x4200)Bus.CpuIo.State.IrqLock=true;
        SyncInterrupts();
        if(cycle.Valid)
        {
            latchClock=Clock.Now;
            if(cycle.Read){heldRead=cycle;holdRemaining=4;}
            CyclePhaseObserved?.Invoke(new(cycle.Read?"read-latch":"write-latch",Clock.Now,cycleStart,latchClock,cycle));
        }
        CommitPpuEndpoint();
    }
    private void AfterCycle(CpuCycle cycle)
    {
        if(cycle.Valid&&cycle.Read){FinishReadHold();return;}
        if(cycle.Pins!="--------"&&(cycle.Read||!cycle.Valid))Bus.CpuIo.AluEdge();writeAluDone=false;
        CyclePhaseObserved?.Invoke(new("cycle-complete",Clock.Now,cycleStart,cycle.Valid?latchClock:null,cycle));
    }
    private void FinishReadHold()
    {
        if(heldRead==null)return;
        AdvanceActive(ref holdRemaining,true);
        var cycle=heldRead;heldRead=null;Bus.CpuIo.AluEdge();
        if(cycle.Address is {} address&&(address&0x40fc00)!=0x4000)Bus.OpenBus=cycle.Value!.Value;
        CyclePhaseObserved?.Invoke(new("cycle-complete",Clock.Now,cycleStart,latchClock,cycle));
    }
    private void AdvanceDma()
    {
        while(Bus.Dma.Busy) {
            if(Clock.Now>=target)throw budget;
            if(Clock.StallUntil>Clock.Now){Clock.AdvanceTo(Math.Min(target,Math.Min(Clock.StallUntil,Clock.NextEventClock)));SyncInterrupts();continue;}
            int remaining=Bus.Dma.State.Remaining;
            long next=Math.Min(target,Math.Min(Clock.Now+remaining,Clock.NextEventClock));
            Clock.ActiveTimeElapsed=elapsed=>Bus.Dma.Elapse(elapsed);try{Clock.AdvanceTo(next);}finally{Clock.ActiveTimeElapsed=null;}SyncInterrupts();
            if(Bus.Dma.State.Remaining==0&&Clock.StallUntil<=Clock.Now){Bus.Dma.Boundary(Clock.Now);CommitPpuEndpoint();}
        }
    }
    private void AdvanceActive(ref int remaining,bool hold)
    {
        while(remaining>0||Clock.StallUntil>Clock.Now)
        {
            if(Clock.Now>=target)throw budget;
            if(Clock.StallUntil>Clock.Now)
            {
                long end=Clock.StallUntil;
                long next=Math.Min(target,Math.Min(end,Clock.NextEventClock));
                Clock.AdvanceTo(next);SyncInterrupts();continue;
            }
            long until=Math.Min(target,Math.Min(Clock.Now+remaining,Clock.NextEventClock));int delta=(int)(until-Clock.Now);
            Clock.ActiveTimeElapsed=elapsed=>{if(hold)holdRemaining-=elapsed;else cycleRemaining-=elapsed;};
            try{Clock.AdvanceTo(until);}finally{Clock.ActiveTimeElapsed=null;}SyncInterrupts();
        }
    }
    public void RunUntil(long masterClock,Action<SnesSystem>? beforeInstruction=null,Action<SnesSystem>? afterInstruction=null)
    {
        if(masterClock<Clock.Now)throw new ArgumentOutOfRangeException(nameof(masterClock));target=masterClock;
        // A snapshot from an APU phase may precede a same-timestamp typed event.
        Clock.AdvanceTo(Clock.Now);SynchronizeDevices();SyncInterrupts();
        while(Clock.Now<target)
        {
            Bus.Ppu.Scanline=Clock.V;
            if(Cpu.AtInstructionBoundary)
            {
                SyncInterrupts();
                // WAI continues the six-master-clock CPU idle oscillator. Its
                // remaining phase must survive an IRQ edge and a saved budget.
                // STP alone suspends the processor until reset.
                if(Cpu.Stopped)
                {
                    if(Bus.Dma.Requested&&!Bus.Dma.Busy){Bus.Dma.CpuBoundary(6,Clock.Now);Bus.Dma.CpuBoundary(6,Clock.Now);}
                    if(Bus.Dma.Busy){try{AdvanceDma();}catch(ClockBudget){break;}continue;}
                    Clock.AdvanceTo(Math.Min(target,Clock.NextEventClock));SyncInterrupts();continue;
                }
                beforeInstruction?.Invoke(this);
            }
            try
            {
                Cpu.RunInstruction();afterInstruction?.Invoke(this);

            }
            catch(ClockBudget){break;}
        }
    }
    public void RunFrame(Action<SnesSystem>? beforeInstruction=null,Action<SnesSystem>? afterInstruction=null)
    {
        if(Bus.Cartridge==null)return;long frame=Frames;
        while(Frames==frame)RunUntil(Clock.NextEventClock,beforeInstruction,afterInstruction);
    }
}
