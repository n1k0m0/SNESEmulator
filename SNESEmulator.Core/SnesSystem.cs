namespace SNESEmulator.Core;

public sealed record ExecutionPhase(int CycleRemaining,bool CycleActive,long StallDebt);

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
    private long stallDebt;
    private long target;
    private sealed class ClockBudget : Exception;
    private readonly ClockBudget budget=new();
    public SnesSystem(HardwareProfile? profile=null)
    {Profile=profile??HardwareProfile.Ntsc;Clock=new(Profile);Cpu=new(Bus);ConfigureClock();Bus.Reset();Clock.Reset();Cpu.BeforeCycle=BeforeCycle;Cpu.AfterCycle=_=>SyncInterrupts();}
    private void ConfigureClock()
    {
        Clock.LineCompleted=line=>{if(line>0)Bus.Ppu.RenderScanline(line-1);};
        Clock.LineStarted=line=>Bus.Ppu.Scanline=line;
        Clock.FieldStarted=()=>{Bus.NmiFlag=false;Bus.Ppu.Scanline=0;};
        Clock.VblankStarted=()=>Bus.BeginVblank();
    }
    public void Insert(Cartridge cartridge)
    {Bus.Cartridge=cartridge;if(cartridge.Pal!=Profile.Pal){Profile=cartridge.Pal?HardwareProfile.PalProfile:HardwareProfile.Ntsc;Clock=new(Profile);ConfigureClock();}Reset();}
    public void Reset()
    {
        Bus.Reset();Cpu.BeforeCycle=null;Cpu.Reset();Cpu.BeforeCycle=BeforeCycle;
        Clock.Reset();cycleRemaining=0;cycleActive=false;stallDebt=0;
    }
    public ExecutionPhase CapturePhase()=>new(cycleRemaining,cycleActive,stallDebt);
    public void RestorePhase(ExecutionPhase p)
    {if(p.CycleRemaining is <0 or >12||p.StallDebt<0)throw new InvalidDataException("Invalid CPU phase");cycleRemaining=p.CycleRemaining;cycleActive=p.CycleActive;stallDebt=p.StallDebt;}
    private void SyncInterrupts() {if(Bus.NmiPending){Cpu.PulseNmi();Bus.NmiPending=false;}}
    private void BeforeCycle(CpuCycle cycle)
    {
        if(cycle.Pins=="--------")return;
        if(!cycleActive){cycleRemaining=Profile.AccessClocks(cycle,(Bus.Io[13]&1)!=0);cycleActive=true;}
        while(cycleRemaining>0||Clock.StallUntil>Clock.Now||stallDebt>0)
        {
            if(Clock.Now>=target)throw budget;
            if(Clock.StallUntil>Clock.Now||stallDebt>0)
            {
                long end=Clock.StallUntil>Clock.Now?Clock.StallUntil:Clock.Now+stallDebt;
                long next=Math.Min(target,Math.Min(end,Clock.NextEventClock));long elapsed=next-Clock.Now;
                Clock.AdvanceTo(next);SyncInterrupts();if(stallDebt>0)stallDebt=Math.Max(0,stallDebt-elapsed);continue;
            }
            long until=Math.Min(target,Math.Min(Clock.Now+cycleRemaining,Clock.NextEventClock));int delta=(int)(until-Clock.Now);
            cycleRemaining-=delta;Clock.AdvanceTo(until);SyncInterrupts();
        }
        cycleActive=false;
    }
    public void RunUntil(long masterClock,Action<SnesSystem>? beforeInstruction=null,Action<SnesSystem>? afterInstruction=null)
    {
        if(masterClock<Clock.Now)throw new ArgumentOutOfRangeException(nameof(masterClock));target=masterClock;
        while(Clock.Now<target)
        {
            Bus.Ppu.Scanline=Clock.V;Clock.Interlace=(Bus.Ppu.Registers[0x33]&1)!=0;Clock.VblankLine=(Bus.Ppu.Registers[0x33]&4)!=0?240:225;
            if(Cpu.AtInstructionBoundary)
            {
                SyncInterrupts();
                if(Cpu.Stopped||Cpu.Waiting&&!Cpu.HasWakeSignal)
                {Clock.AdvanceTo(Math.Min(target,Clock.NextEventClock));SyncInterrupts();continue;}
                beforeInstruction?.Invoke(this);
            }
            try
            {
                Cpu.RunInstruction();afterInstruction?.Invoke(this);
                // Legacy DMA is explicitly approximate until M3; stall debt is consumed
                // before further CPU effects, not evidence of accurate DMA timing.
                if(Bus.DmaMasterCycles!=0){stallDebt+=Bus.DmaMasterCycles;Bus.DmaMasterCycles=0;}
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
