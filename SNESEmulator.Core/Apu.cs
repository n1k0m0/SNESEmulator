using System.Security.Cryptography;
namespace SNESEmulator.Core;

public sealed record ApuTimer(byte Target,byte Counter,byte Output,int Divider,bool Level,bool Line,bool Enabled);
public sealed record ApuState(byte[] Ram,byte[] CpuToApu,byte[] ApuToCpu,byte[] Dsp,byte Test,byte Control,byte DspAddress,byte Aux8,byte Aux9,ApuTimer[] Timers,
    SpcRegisters Cpu,SpcContinuation? Continuation,long Crystal,long NextEffect,int Hold,int CycleLength,int TimerLength,bool CycleActive,bool Holding,long Cycles,string FirmwareHash,bool Isolated,DspState Sound);
public sealed record ApuPhase(string Phase,long Crystal,SpcCycle Cycle);

/// <summary>S-SMP digital interface, own SPC700 and crystal scheduling. native S-DSP crystal scheduling.</summary>
public sealed class Apu : ISpcBus
{
    public byte[] Ram {get;}=new byte[65536];
    public byte[] CpuToApu {get;}=new byte[4];
    public byte[] ApuToCpu {get;}=new byte[4];
    public byte[] DspRegisters {get;}=new byte[128];
    public Spc700 Cpu {get;}
    public SnesDsp Dsp {get;}
    private Action<DspPhase>? dspObserved;
    private DspPhase? pendingDsp;
    public Action<DspPhase>? DspPhaseObserved {get=>dspObserved;set {dspObserved=value;Dsp.Committed=value==null?null:row=>pendingDsp=row;}}
    public const bool IsImplemented=true;
    // Explicit electrical laboratory fixture for isolated CPU/DMA oracles.
    public bool Isolated {get;set;}
    public byte Test {get;private set;}=0x0a;
    public byte Control {get;private set;}=0xb0;
    public byte DspAddress {get;private set;}
    private byte aux8,aux9;
    private readonly byte[] firmware;
    public string FirmwareHash {get;}
    public bool OriginalFirmware {get;}
    public bool ExternallySuppliedFirmware {get;}
    public long Crystal {get;private set;}
    public long Cycles {get;private set;}
    /// <summary>Native S-DSP sample-period position (768 crystal clocks =32 nominal SPC cycles). Native digital sample period.</summary>
    public int DspCrystalPhase=>(int)(Crystal%768);
    public Action<ApuPhase>? PhaseObserved {get;set;}
    private long budget,nextEffect;
    private int hold,cycleLength,timerLength;
    private bool cycleActive,holding;
    private SpcCycle? heldCycle;
    private sealed class Timer
    {
        public byte Target,Counter,Output;public int Divider;public bool Level,Line,Enabled;
        public ApuTimer Capture()=>new(Target,Counter,Output,Divider,Level,Line,Enabled);
        public void Restore(ApuTimer t){Target=t.Target;Counter=t.Counter;Output=t.Output;Divider=t.Divider;Level=t.Level;Line=t.Line;Enabled=t.Enabled;}
    }
    private readonly Timer[] timers=[new(),new(),new()];
    public Apu(byte[]? ipl=null)
    {
        // Own IPL implements the upload protocol with independently authored
        // code, not Nintendo-identical instruction timing. Identity is retained
        // in states; choosing firmware must never silently change a golden.
        firmware=(ipl??OwnIpl.Bytes).ToArray();if(firmware.Length!=64)throw new InvalidDataException("SPC IPL must contain64 bytes");
        FirmwareHash=Convert.ToHexStringLower(SHA256.HashData(firmware));ExternallySuppliedFirmware=ipl!=null;
        OriginalFirmware=FirmwareHash=="c95f88b299030d5afa55b1031e2b5ef2dff650c4b4e6bb6f8b1359436521278f";
        Dsp=new(Ram,DspRegisters);Cpu=new(this);Cpu.PrepareBusCycle=BeforeBusCycle;Cpu.CompleteBusCycle=AfterBusCycle;Reset();
    }
    public static byte[]? ResolveLocalFirmware()
    {
        string? path=Environment.GetEnvironmentVariable("SNESEMULATOR_IPL");if(string.IsNullOrWhiteSpace(path))return null;
        byte[] data=File.ReadAllBytes(path);if(data.Length!=64)throw new InvalidDataException("SNESEMULATOR_IPL must reference a64-byte locally supplied IPL image");return data;
    }
    /// <summary>Own laboratory SPC v0.30 container: TEST0A, IPL disabled, RAM contains underlay, zero extension.
    /// Ports start with the RAM F4–F7 values in both directions. The format omits hidden pipeline,
    /// so playback starts at the documented deterministic phase0 epoch, not an exact savestate.</summary>
    public void LoadLaboratorySpcSnapshot(ReadOnlySpan<byte> file)
    {
        if(file.Length<0x10200||!file[..33].SequenceEqual("SNES-SPC700 Sound File Data v0.30"u8)||file[0x21]!=26||file[0x22]!=26)throw new InvalidDataException("Expected a complete SPC v0.30 snapshot");
        if(file[0x1f0]!=0x0a||(file[0x1f1]&128)!=0||file.Slice(0x101c0,64).IndexOfAnyExcept((byte)0)>=0)throw new InvalidDataException("Laboratory SPC convention requires TEST0A, IPL disabled, RAM underlay, zero extension; ambiguous generic SPC dumps are unsupported");
        Reset();file.Slice(0x100,65536).CopyTo(Ram);file.Slice(0x10100,128).CopyTo(DspRegisters);
        Test=0x0a;Control=Ram[0xf1];DspAddress=Ram[0xf2];aux8=Ram[0xf8];aux9=Ram[0xf9];
        for(int i=0;i<4;i++)CpuToApu[i]=ApuToCpu[i]=Ram[0xf4+i];
        Cpu.Restore(new((ushort)(file[0x25]|file[0x26]<<8),file[0x27],file[0x28],file[0x29],file[0x2b],file[0x2a],false,false,0));
        for(int i=0;i<3;i++){timers[i].Target=Ram[0xfa+i];timers[i].Enabled=(Control&(1<<i))!=0;timers[i].Output=(byte)(Ram[0xfd+i]&15);}
        Dsp.Restore(new(){Directory=DspRegisters[0x5d],EchoPage=DspRegisters[0x6d],NewKeys=DspRegisters[0x4c]},0);
    }
    public void Reset(bool powerOn=true)
    {
        if(powerOn)Array.Clear(Ram);Dsp.Reset(powerOn);pendingDsp=null;
        Array.Clear(CpuToApu);Array.Clear(ApuToCpu);Test=0x0a;Control=0xb0;DspAddress=aux8=aux9=0;
        for(int i=0;i<3;i++){byte target=powerOn?(byte)0:timers[i].Target;timers[i].Restore(new(target,0,powerOn?(byte)15:(byte)0,0,false,false,false));}
        Cpu.Reset();Crystal=Cycles=0;budget=nextEffect=0;hold=cycleLength=timerLength=0;cycleActive=holding=false;heldCycle=null;
    }
    public byte Read(ushort a)
    {
        if(a is >=0xf0 and <=0xff)return a switch {0xf2=>DspAddress,0xf3=>DspRegisters[DspAddress&127],>=0xf4 and <=0xf7=>CpuToApu[a-0xf4],0xf8=>aux8,0xf9=>aux9,>=0xfd=>ReadTimer(a-0xfd),_=>0};
        if(a>=0xffc0&&(Control&128)!=0)return firmware[a-0xffc0];
        return (Test&4)!=0?(byte)0x5a:Ram[a];
    }
    private byte ReadTimer(int i){byte n=timers[i].Output;timers[i].Output=0;return n;}
    public void Write(ushort a,byte v)
    {
        if((Test&6)==2)Ram[a]=v;
        if(a is <0xf0 or >0xff)return;
        switch(a){
            case 0xf0:if((Cpu.PSW&32)==0){Test=v;foreach(var t in timers)SyncLine(t);}break;
            case 0xf1:for(int i=0;i<3;i++){bool enable=(v&(1<<i))!=0;var t=timers[i];if(enable&&!t.Enabled)t.Counter=t.Output=0;t.Enabled=enable;}
                if((v&16)!=0)CpuToApu[0]=CpuToApu[1]=0;if((v&32)!=0)CpuToApu[2]=CpuToApu[3]=0;Control=v;break;
            case 0xf2:DspAddress=v;break;
            case 0xf3:if(DspAddress<128)WriteDsp(DspAddress,v);break;
            case >=0xf4 and <=0xf7:ApuToCpu[a-0xf4]=v;break;
            case 0xf8:aux8=v;break;case 0xf9:aux9=v;break;
            case >=0xfa and <=0xfc:timers[a-0xfa].Target=v;break;
        }
    }
    private void WriteDsp(byte a,byte v)
    {
        Dsp.WriteRegister(a,v);
    }
    private void SyncLine(Timer t)
    {
        bool line=t.Level&&(Test&9)==8;bool falling=t.Line&&!line;t.Line=line;
        if(falling&&t.Enabled){t.Counter++;if(t.Counter==t.Target){t.Counter=0;t.Output=(byte)((t.Output+1)&15);}}
    }
    private void AdvanceTimers(int inputClocks)
    {
        for(int i=0;i<3;i++){var t=timers[i];int half=i==2?16:128;t.Divider+=inputClocks;
            while(t.Divider>=half){t.Divider-=half;t.Level=!t.Level;SyncLine(t);}}
    }
    // The internal electrical hooks admit/complete a bus cycle using its value
    // fields; allocate public records only for a retained port hold or observer.
    private int Selector(ushort? address)=>address==null||address is >=0xf0 and <=0xff||address>=0xffc0&&(Control&128)!=0?Test>>6:(Test>>4)&3;
    // Port reads latch halfway through their selected bus period. The trailing
    // hold is still real APU time: DSP and timers advance before the SPC decoder
    // may consume the saved byte. Both halves survive a budget stop/save/load.
    private bool BeforeBusCycle(ushort? address,string direction)
    {
        if(!FinishHold())return false;
        if(!cycleActive){int s=Selector(address);cycleLength=(s switch {0=>2,1=>4,2=>10,_=>20})*12;timerLength=2<<s;hold=direction=="read"&&address is >=0xf4 and <=0xf7?cycleLength/2:0;nextEffect=Crystal+cycleLength-hold;cycleActive=true;}
        if(nextEffect>budget){AdvanceCrystal(budget,false);return false;}
        AdvanceSegment(nextEffect,hold>0?timerLength/2:timerLength);cycleActive=false;return true;
    }
    private bool AdvanceSegment(long end,int timersAtEnd)
    {
        if(end>budget){AdvanceCrystal(budget,false);return false;}AdvanceCrystal(end,true);AdvanceTimers(timersAtEnd);return true;
    }
    private bool AfterBusCycle(ushort? address,byte? value,string direction)
    {
        Cycles++;holding=hold>0;if(holding){nextEffect=Crystal+hold;heldCycle=new(address,value,direction);}
        FlushDsp();
        PhaseObserved?.Invoke(new(direction=="read"?"read-latch":direction=="write"?"write-latch":"internal-complete",Crystal,new(address,value,direction)));
        return FinishHold();
    }
    private bool FinishHold()
    {
        if(!holding)return true;if(!AdvanceSegment(nextEffect,timerLength/2))return false;holding=false;hold=0;var c=heldCycle;heldCycle=null;
        FlushDsp();if(c!=null)PhaseObserved?.Invoke(new("read-complete",Crystal,c));return true;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void RunUntil(long crystal)
    {
        if(crystal<Crystal)throw new ArgumentOutOfRangeException(nameof(crystal));budget=crystal;
        if(Isolated){Crystal=crystal;return;}
        // Most PPU half-dots end before either crystal-domain effect. Only the
        // current crystal moves there: timers, held ports, DSP and SPC registers
        // retain their pending endpoints. Equality still executes the real DSP
        // boundary below, including its existing notification order.
        if((cycleActive||holding)&&nextEffect>crystal){
            if(crystal<Dsp.NextBoundary)Crystal=crystal;
            else AdvanceCrystal(crystal,false);
            return;
        }
        RunEndpoint();
    }
    private void RunEndpoint()
    {
        while(true){if(!FinishHold()||!Cpu.RunInstruction()){FlushDsp();return;}}
    }
    private void FlushDsp(){var row=pendingDsp;pendingDsp=null;if(row!=null)dspObserved?.Invoke(row);}
    // At a shared SMP/DSP endpoint, finish the DSP phase before the SMP effect,
    // but defer its observer until the endpoint commit is complete. This order
    // is a modeled digital contract; reset alignment to physical boards remains
    // unverified (see the explicit R3 laboratory epoch experiment).
    private void AdvanceCrystal(long target,bool deferFinal)
    {
        while(Dsp.NextBoundary<=target){Crystal=Dsp.NextBoundary;Dsp.AdvanceTo(Crystal);if(Crystal!=target||!deferFinal)FlushDsp();}
        Crystal=target;
    }
    public long NextBoundary=>Isolated?long.MaxValue:Math.Min(Dsp.NextBoundary,cycleActive||holding?nextEffect:Crystal+1);
    public ApuState Capture()=>new(Ram.ToArray(),CpuToApu.ToArray(),ApuToCpu.ToArray(),DspRegisters.ToArray(),Test,Control,DspAddress,aux8,aux9,timers.Select(t=>t.Capture()).ToArray(),Cpu.Capture(),Cpu.CaptureContinuation(),Crystal,nextEffect,hold,cycleLength,timerLength,cycleActive,holding,Cycles,FirmwareHash,Isolated,Dsp.Capture());
    public void Restore(ApuState s)
    {
        if(s.FirmwareHash!=FirmwareHash)throw new InvalidDataException("Savestate SPC firmware identity does not match configured IPL");
        if(s.Ram.Length!=65536||s.CpuToApu.Length!=4||s.ApuToCpu.Length!=4||s.Dsp.Length!=128||s.Timers.Length!=3||s.Crystal<0||s.NextEffect<0||s.Hold is <0 or >120||s.Timers.Any(t=>t.Output>15||t.Divider<0||t.Divider>=128))throw new InvalidDataException("Invalid APU state");
        if(s.Cycles<0||s.Cpu.Instructions<0||s.Timers[2].Divider>=16||s.CycleLength is not (0 or 24 or 48 or 120 or 240)||s.TimerLength is not (0 or 2 or 4 or 8 or 16)||(s.CycleActive||s.Holding)&&s.NextEffect<s.Crystal||s.Holding&&(s.Continuation==null||s.Continuation.Completed.Length==0||s.Continuation.Completed[^1].Address is not (>=0xf4 and <=0xf7)||s.Continuation.Completed[^1].Direction!="read"||s.Hold!=s.CycleLength/2))throw new InvalidDataException("Invalid SPC bus phase");
        if(s.Sound==null||s.Sound.Phase!=((s.Sound.NextCrystal/24-1)&31)||s.Sound.Samples!=(s.Sound.NextCrystal/24+3)/32)throw new InvalidDataException("Inconsistent native DSP clock/phase/sample state");
        s.Ram.CopyTo(Ram,0);s.CpuToApu.CopyTo(CpuToApu,0);s.ApuToCpu.CopyTo(ApuToCpu,0);s.Dsp.CopyTo(DspRegisters,0);Test=s.Test;Control=s.Control;DspAddress=s.DspAddress;aux8=s.Aux8;aux9=s.Aux9;
        for(int i=0;i<3;i++)timers[i].Restore(s.Timers[i]);Cpu.Restore(s.Cpu);Cpu.RestoreContinuation(s.Continuation);Crystal=s.Crystal;nextEffect=s.NextEffect;hold=s.Hold;cycleLength=s.CycleLength;timerLength=s.TimerLength;cycleActive=s.CycleActive;holding=s.Holding;Cycles=s.Cycles;Isolated=s.Isolated;
        heldCycle=holding?s.Continuation?.Completed.LastOrDefault():null;Dsp.Restore(s.Sound,Crystal,Isolated);pendingDsp=null;
    }
}
