namespace SNESEmulator.Core;

public enum DmaJob { None, Dma, Setup, Line }
public enum DmaPhase { Idle, Align, Global, Channel, Read, Hold, ReloadRead, ReloadHold, PointerLowRead, PointerLowHold, PointerHighRead, PointerHighHold, Sync }
public sealed class DmaState
{
    public DmaJob Job {get;set;}
    public DmaPhase Phase {get;set;}
    public int Remaining {get;set;}
    public byte Enabled {get;set;}
    public bool Pending {get;set;}
    public int HdmaPending {get;set;}
    public bool Armed {get;set;}
    public int CpuClocks {get;set;}
    public long ActiveClocks {get;set;}
    public int Channel {get;set;}
    public int Index {get;set;}
    public int DmaChannel {get;set;}
    public int DmaIndex {get;set;}
    public bool ResumeDma {get;set;}
    public bool DmaReady {get;set;}
    public bool AdvancePass {get;set;}
    public int AddressA {get;set;}
    public int AddressB {get;set;}
    public bool ValidB {get;set;}
    public bool Reverse {get;set;}
    public byte Data {get;set;}
    public bool[] Completed {get;set;}=new bool[8];
    public bool[] DoTransfer {get;set;}=new bool[8];
}
public sealed record DmaTrace(long Clock,string Phase,DmaJob Job,int Channel,int Address,byte Value,bool Valid);

/// <summary>Restartable bus owner. Each transfer samples once after four active
/// clocks and writes after four more; no deferred copy or speculative MMIO.</summary>
public sealed class DmaController(SystemBus bus)
{
    public DmaState State {get;private set;}=new();
    public Action<DmaTrace>? Observed {get;set;}
    private DmaTrace? notice;
    private byte[] R=>bus.DmaRegisters;
    public bool Busy=>State.Phase!=DmaPhase.Idle;
    public bool Requested=>State.Pending||State.HdmaPending!=0;
    public static readonly int[][] Patterns=[[0],[0,1],[0,0],[0,0,1,1],[0,1,2,3],[0,1,0,1],[0,0],[0,0,1,1]];
    private int Word(int d)=>R[d]|R[d+1]<<8;
    private void Word(int d,int value){R[d]=(byte)value;R[d+1]=(byte)(value>>8);}
    public void Reset()=>State=new();
    public void Restore(DmaState s)
    {
        if(!Enum.IsDefined(s.Job)||!Enum.IsDefined(s.Phase)||s.Remaining is <0 or >12||s.Channel is <0 or >8||s.DmaChannel is <0 or >8||s.Completed==null||s.DoTransfer==null||s.Completed.Length!=8||s.DoTransfer.Length!=8||s.HdmaPending is <0 or >2||s.ActiveClocks<0||s.Index is <0 or >65536||s.DmaIndex is <0 or >65536||s.AddressA is <0 or >0xffffff||s.AddressB is <0 or >0xffff||(s.Phase!=DmaPhase.Idle&&s.CpuClocks is not (6 or 8 or 12))||(s.Phase is DmaPhase.Read or DmaPhase.Hold or DmaPhase.ReloadRead or DmaPhase.ReloadHold or DmaPhase.PointerLowRead or DmaPhase.PointerLowHold or DmaPhase.PointerHighRead or DmaPhase.PointerHighHold && s.Channel>=8))throw new InvalidDataException("Invalid DMA phase");State=s;
    }
    public void Request(byte channels){State.Enabled=channels;State.Pending=channels!=0;}
    public void RequestHdma(bool setup)
    {
        if(setup){Array.Clear(State.Completed);Array.Fill(State.DoTransfer,true);}
        if(setup?bus.Io[12]!=0:Enumerable.Range(0,8).Any(HdmaActive))State.HdmaPending=setup?1:2;
    }
    private bool HdmaActive(int ch)=>ch<8&&(bus.Io[12]&(1<<ch))!=0&&!State.Completed[ch];
    // Returns true when the CPU must yield; the first pending boundary only arms.
    public bool CpuBoundary(int clocks,long now)
    {
        if(Busy)return true;
        if(!Requested){State.Armed=false;return false;}
        if(!State.Armed){State.Armed=true;return false;}
        State.Armed=false;State.CpuClocks=clocks;State.ActiveClocks=0;
        State.ResumeDma=State.Pending&&State.Enabled!=0;
        if(State.HdmaPending!=0)StartHdma();else StartDma();
        Set(DmaPhase.Align,8-(int)(now&7));return true;
    }
    private void Set(DmaPhase phase,int clocks){State.Phase=phase;State.Remaining=clocks;}
    public void Elapse(int clocks){State.Remaining-=clocks;State.ActiveClocks+=clocks;}
    private void StartDma(){State.Job=DmaJob.Dma;State.Pending=false;State.ResumeDma=false;State.Channel=0;State.Index=0;State.DmaChannel=0;State.DmaIndex=0;}
    private void StartHdma(){State.Job=State.HdmaPending==1?DmaJob.Setup:DmaJob.Line;State.HdmaPending=0;State.Channel=0;State.Index=0;State.AdvancePass=false;}
    private void FinishJob()
    {
        if(State.Job!=DmaJob.Dma&&State.ResumeDma&&State.Enabled!=0){
            State.Job=DmaJob.Dma;State.Channel=State.DmaChannel;State.Index=State.DmaIndex;State.ResumeDma=false;
            if(State.Pending){State.Pending=false;State.Channel=0;State.Index=0;Set(DmaPhase.Global,8);}
            else if(State.DmaReady&&State.Channel<8&&(State.Enabled&(1<<State.Channel))!=0)PrepareTransfer();else NextDma(false);
            return;
        }
        if(State.HdmaPending!=0){StartHdma();Set(DmaPhase.Global,8);return;}
        if(State.Enabled==0){State.Pending=false;bus.Io[11]=0;}
        bus.CpuIo.State.IrqLock=true;
        int clocks=State.CpuClocks-(int)(State.ActiveClocks%State.CpuClocks);
        Set(DmaPhase.Sync,clocks);
    }
    private void NextDma(bool nextChannel)
    {
        if(nextChannel){State.Channel++;State.Index=0;}
        while(State.Channel<8&&(State.Enabled&(1<<State.Channel))==0){State.Channel++;State.Index=0;}
        if(State.Channel==8){bus.Io[11]=0;FinishJob();return;}
        if(nextChannel||State.Index==0){Set(DmaPhase.Channel,8);return;}
        PrepareTransfer();
    }
    private void PrepareTransfer()
    {
        int d=State.Channel*16;var pattern=Patterns[R[d]&7];
        State.AddressB=0x2100|((R[d+1]+pattern[State.Index%pattern.Length])&255);
        int pointer;
        if(State.Job==DmaJob.Dma)pointer=Word(d+2);
        else if((R[d]&64)==0){pointer=Word(d+8);Word(d+8,pointer+1);}
        else {pointer=Word(d+5);Word(d+5,pointer+1);}
        int bank=State.Job!=DmaJob.Dma&&(R[d]&64)!=0?R[d+7]:R[d+4];
        State.AddressA=bank<<16|pointer;
        State.ValidB=(State.AddressB&255)!=0x80||!SystemBus.IsWram(State.AddressA);
        State.Reverse=(R[d]&128)!=0;Set(DmaPhase.Read,4);
    }
    private void TransferFinished()
    {
        int d=State.Channel*16;State.Index++;
        if(State.Job==DmaJob.Dma) {
            if((R[d]&8)==0)Word(d+2,Word(d+2)+((R[d]&16)!=0?-1:1));
            int count=(Word(d+5)-1)&65535;Word(d+5,count);
            if(count==0)State.Enabled&=(byte)~(1<<State.Channel);
            if(State.HdmaPending!=0){
                SuspendForHdma(true);return;
            }
            if(count==0)NextDma(true);else PrepareTransfer();return;
        }
        if(State.Index<Patterns[R[d]&7].Length){PrepareTransfer();return;}
        State.Channel++;State.Index=0;NextHdma();
    }
    private void SuspendForHdma(bool ready)
    {
        State.DmaChannel=State.Channel;State.DmaIndex=State.Index;State.DmaReady=ready;State.ResumeDma=true;
        StartHdma();Set(DmaPhase.Global,8);
    }
    private void NextHdma()
    {
        while(State.Channel<8&&!HdmaActive(State.Channel))State.Channel++;
        if(State.Channel==8){
            if(State.Job==DmaJob.Line&&!State.AdvancePass){State.AdvancePass=true;State.Channel=0;NextHdma();return;}
            FinishJob();return;
        }
        int d=State.Channel*16;
        State.Enabled&=(byte)~(1<<State.Channel);
        if(State.Job==DmaJob.Setup){Word(d+8,Word(d+2));R[d+10]=0;State.DoTransfer[State.Channel]=true;PrepareReload();return;}
        if(State.AdvancePass){R[d+10]--;State.DoTransfer[State.Channel]=(R[d+10]&128)!=0;PrepareReload();return;}
        if(!State.DoTransfer[State.Channel]){State.Channel++;NextHdma();return;}
        PrepareTransfer();
    }
    private void PrepareReload(){State.AddressA=R[State.Channel*16+4]<<16|Word(State.Channel*16+8);Set(DmaPhase.ReloadRead,4);}
    private void ReadA(long now,string phase)
    {
        bool valid=SystemBus.ValidDmaA(State.AddressA);
        State.Data=valid?bus.Read(State.AddressA,State.Job==DmaJob.Dma?AccessOrigin.DmaA:AccessOrigin.HdmaA):(byte)0;
        bus.OpenBus=State.Data;
        notice=new(now,phase,State.Job,State.Channel,State.AddressA,State.Data,valid);
    }
    private void NextReloadChannel(){State.Channel++;State.Index=0;NextHdma();}
    public void Boundary(long now)
    {
        if(State.Remaining!=0)throw new InvalidOperationException("DMA boundary before clock completion");
        int d=State.Channel*16;
        switch(State.Phase) {
            case DmaPhase.Align:Set(DmaPhase.Global,8);break;
            case DmaPhase.Global:if(State.Job==DmaJob.Dma){if(State.HdmaPending!=0)SuspendForHdma(false);else NextDma(false);}else NextHdma();break;
            case DmaPhase.Channel:if(State.HdmaPending!=0)SuspendForHdma(true);else PrepareTransfer();break;
            case DmaPhase.Read:
                if(!State.Reverse)ReadA(now,"read-A");
                else {
                    State.Data=State.ValidB?bus.Read(State.AddressB,State.Job==DmaJob.Dma?AccessOrigin.DmaB:AccessOrigin.HdmaB):(byte)0;bus.OpenBus=State.Data;
                    notice=new(now,"read-B",State.Job,State.Channel,State.AddressB,State.Data,State.ValidB);
                }
                Set(DmaPhase.Hold,4);break;
            case DmaPhase.Hold:
                int address=State.Reverse?State.AddressA:State.AddressB;
                bool valid=State.Reverse?SystemBus.ValidDmaA(address):State.ValidB;
                if(valid)bus.Write(address,State.Data,State.Job==DmaJob.Dma?(State.Reverse?AccessOrigin.DmaA:AccessOrigin.DmaB):(State.Reverse?AccessOrigin.HdmaA:AccessOrigin.HdmaB));
                notice=new(now,State.Reverse?"write-A":"write-B",State.Job,State.Channel,address,State.Data,valid);
                TransferFinished();break;
            case DmaPhase.ReloadRead:ReadA(now,"table-read");Set(DmaPhase.ReloadHold,4);break;
            case DmaPhase.ReloadHold:
                if((R[d+10]&127)!=0){NextReloadChannel();break;}
                R[d+10]=State.Data;Word(d+8,Word(d+8)+1);
                State.Completed[State.Channel]=State.Data==0;State.DoTransfer[State.Channel]=State.Data!=0;
                if((R[d]&64)==0){NextReloadChannel();break;}
                State.AddressA=R[d+4]<<16|Word(d+8);Word(d+8,Word(d+8)+1);Set(DmaPhase.PointerLowRead,4);break;
            case DmaPhase.PointerLowRead:ReadA(now,"indirect-low");Set(DmaPhase.PointerLowHold,4);break;
            case DmaPhase.PointerLowHold:
                Word(d+5,State.Data<<8);
                if(State.Completed[State.Channel]&&!Enumerable.Range(State.Channel+1,7-State.Channel).Any(HdmaActive)){NextReloadChannel();break;}
                State.AddressA=R[d+4]<<16|Word(d+8);Word(d+8,Word(d+8)+1);Set(DmaPhase.PointerHighRead,4);break;
            case DmaPhase.PointerHighRead:ReadA(now,"indirect-high");Set(DmaPhase.PointerHighHold,4);break;
            case DmaPhase.PointerHighHold:Word(d+5,State.Data<<8|R[d+6]);NextReloadChannel();break;
            case DmaPhase.Sync:State.Job=DmaJob.None;State.Phase=DmaPhase.Idle;break;
            default:throw new InvalidOperationException("Invalid DMA transition");
        }
        var completedNotice=notice;notice=null;if(completedNotice!=null)Observed?.Invoke(completedNotice);
    }
}
