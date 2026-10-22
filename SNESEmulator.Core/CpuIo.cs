namespace SNESEmulator.Core;

/// <summary>All data between CPU ALU edges and asynchronous 5A22 I/O edges.</summary>
public sealed class IoState
{
    public int Multiply {get;set;}
    public int Divide {get;set;}
    public uint Shift {get;set;}
    public ushort Quotient {get;set;}
    public ushort Product {get;set;}
    public bool IrqFlag {get;set;}
    public bool ComparatorHigh {get;set;}
    public bool ExternalIrq {get;set;}
    public long IrqHoldUntil {get;set;}
    public long NmiHoldUntil {get;set;}
    public bool NmiOutput {get;set;}
    public bool IrqLock {get;set;}
    public int IrqGeneration {get;set;}
    public int AutoCounter {get;set;}=33;
    public bool AutoLatch {get;set;}
    public bool CpuLatch {get;set;}
    public byte AutoPort1 {get;set;}
    public byte AutoPort2 {get;set;}
    public ushort[] Joy {get;set;}=new ushort[4];
    public ushort LatchedH {get;set;}
    public ushort LatchedV {get;set;}
    public bool CounterLatched {get;set;}
    public bool ReadHHigh {get;set;}
    public bool ReadVHigh {get;set;}
    public byte Ppu2Mdr {get;set;}
}

public sealed class CpuIo(SystemBus bus)
{
    public IoState State {get;private set;}=new();
    public MasterClock? Clock {get;set;}
    public bool AutoBusy => (bus.Io[0]&1)!=0 && State.AutoCounter<33;
    public bool IrqOutput => State.IrqFlag && Clock?.Now>=State.IrqHoldUntil && (bus.Io[0]&0x30)!=0;
    public void Restore(IoState state)
    {
        if(state.Multiply is <0 or >8||state.Divide is <0 or >16||state.AutoCounter is <0 or >33||state.Joy==null||state.Joy.Length!=4||state.IrqGeneration is <0 or >0x7fffff||state.NmiHoldUntil<0||state.IrqHoldUntil<0||state.LatchedH>511||state.LatchedV>511)throw new InvalidDataException("Invalid 5A22 I/O state");
        State=state;
    }
    public void Reset(){State=new();}
    public void AluEdge()
    {
        var s=State;
        if(s.Multiply>0) {
            s.Multiply--;
            if((s.Quotient&1)!=0)s.Product=unchecked((ushort)(s.Product+s.Shift));
            s.Quotient>>=1;s.Shift<<=1;
        }
        if(s.Divide>0) {
            s.Divide--;s.Quotient=unchecked((ushort)(s.Quotient<<1));s.Shift>>=1;
            if(s.Product>=s.Shift){s.Product=unchecked((ushort)(s.Product-s.Shift));s.Quotient|=1;}
        }
    }
    public void StartMultiply(byte multiplier)
    {
        State.Product=0;
        if(State.Multiply!=0||State.Divide!=0)return;
        State.Quotient=(ushort)(multiplier<<8|bus.Io[2]);State.Shift=multiplier;State.Multiply=8;
    }
    public void StartDivide(byte divisor)
    {
        State.Product=(ushort)(bus.Io[4]|bus.Io[5]<<8);
        if(State.Multiply!=0||State.Divide!=0)return;
        State.Shift=(uint)divisor<<16;State.Divide=16;
    }
    public void BeginVblank()
    {
        bus.NmiFlag=true;
        if(Clock==null){State.NmiOutput=(bus.Io[0]&128)!=0;return;}
        State.NmiHoldUntil=Clock.Now+4;
        Clock.Schedule(State.NmiHoldUntil,HardwareEventKind.Device,2,-4);
        long start=(Clock.Now-Clock.H+130+255)&~255L;
        Clock.Schedule(start,HardwareEventKind.Device,3,-3);
    }
    public void NewField(){bus.NmiFlag=false;State.NmiOutput=false;State.AutoCounter=33;}
    public void WriteControl(byte old,byte value)
    {
        if((value&0x30)==0)State.IrqFlag=false;
        if((old&128)==0&&(value&128)!=0&&bus.NmiFlag)State.NmiOutput=true;
        if((value&128)==0)State.NmiOutput=false;
        State.IrqLock=true;
        if(State.AutoCounter==0){State.AutoLatch=(value&1)!=0;UpdateLatch();}
        else if((value&1)==0&&State.AutoCounter>=2)State.AutoCounter=33;
        ScheduleIrq();
    }
    public void WriteStrobe(byte value){State.CpuLatch=(value&1)!=0;UpdateLatch();}
    private void UpdateLatch()=>bus.Controllers.Write((byte)(State.CpuLatch||State.AutoLatch?1:0));
    public void ScheduleIrq(bool reprogram=true)
    {
        if(Clock==null)return;
        // Tokens live for at most one line plus ten clocks; at least one CPU
        // bus cycle separates hardware writes, so a 23-bit token cannot be
        // reused while an old event is pending. Keep payload sign bit clear.
        if(reprogram)State.IrqGeneration=(State.IrqGeneration+1)&0x7fffff;
        int generation=State.IrqGeneration;
        int mode=(bus.Io[0]>>4)&3;if(mode==0){State.ComparatorHigh=false;return;}
        int htime=bus.Io[7]|(bus.Io[8]&1)<<8;
        int vtime=bus.Io[9]|(bus.Io[10]&1)<<8;
        if(mode==2){
            // V-only is a level comparator, sampled on the next four-clock
            // poll after register changes, and at H10 after line transitions.
            long next=reprogram?Clock.Now+(((2-Clock.H-1)&3)+1):Clock.Now-Clock.H+10;
            Clock.Schedule(next,HardwareEventKind.Device,(generation<<8)|8,-4);
            if(reprogram&&Clock.H<10&&next<Clock.Now-Clock.H+10)Clock.Schedule(Clock.Now-Clock.H+10,HardwareEventKind.Device,(generation<<8)|8,-4);return;
        }
        State.ComparatorHigh=false;
        // Comparator input is delayed ten clocks. H timer internally compares
        // (HTIME+1)*4, including the documented +1-dot comparator offset.
        int edge=mode==2?10:(htime+1)*4+10;
        if(mode!=2&&(htime+1)*4>=Clock.Profile.LineClocks(Clock.V,Clock.Field,Clock.Interlace))return;
        if((mode&2)!=0&&Clock.V!=vtime)return;
        long at=Clock.Now-Clock.H+edge;
        if(at<Clock.Now)return;
        Clock.Schedule(at,HardwareEventKind.Device,(generation<<8)|4,-4);
    }
    public void Event(int payload)
    {
        int kind=payload&255;
        if(kind==1){AluEdge();return;}
        if(kind==2){if(bus.NmiFlag&&(bus.Io[0]&128)!=0)State.NmiOutput=true;return;}
        if(kind==4) {
            if((payload>>8)!=State.IrqGeneration||(bus.Io[0]&0x30)==0||!IrqCounterTapValid())return;
            RaiseIrq();return;
        }
        if(kind==8) {
            if((payload>>8)!=State.IrqGeneration)return;
            int delayedV=Clock!.CounterBefore(10).V;
            bool level=(bus.Io[0]&0x30)==0x20&&delayedV==(bus.Io[9]|(bus.Io[10]&1)<<8)&&IrqCounterTapValid();
            if(level&&!State.ComparatorHigh)RaiseIrq();
            State.ComparatorHigh=level;return;
        }
        if(kind==3) {
            State.AutoCounter=0;State.AutoLatch=(bus.Io[0]&1)!=0;UpdateLatch();
            if((bus.Io[0]&1)!=0)Array.Clear(State.Joy);
            Clock!.Schedule(Clock.Now+128,HardwareEventKind.Device,6,-3);return;
        }
        if(kind!=6||State.AutoCounter>=33)return;
        State.AutoCounter++;
        if(State.AutoCounter==1){State.AutoLatch=false;UpdateLatch();}
        if(State.AutoCounter!=1&&(bus.Io[0]&1)==0){State.AutoCounter=33;return;}
        if(State.AutoCounter>=2) {
            if((State.AutoCounter&1)==0){State.AutoPort1=bus.Controllers.Read(0);State.AutoPort2=bus.Controllers.Read(1);}
            else for(int i=0;i<4;i++)State.Joy[i]=(ushort)(State.Joy[i]<<1|((i&1)==0?State.AutoPort1:State.AutoPort2)>>(i/2)&1);
        }
        if(State.AutoCounter<33)Clock!.Schedule(Clock.Now+128,HardwareEventKind.Device,6,-3);
    }
    private bool IrqCounterTapValid()
    {
        var tap=Clock!.CounterBefore(6);
        return tap.H!=0||tap.V!=0;
    }
    private void RaiseIrq()
    {
        State.IrqFlag=true;State.IrqHoldUntil=Clock!.Now+4;
        Clock.Schedule(State.IrqHoldUntil,HardwareEventKind.Device,5,-4);
    }
    public byte ReadNmi(byte mdr)
    {
        byte value=(byte)((mdr&0x70)|(bus.NmiFlag?128:0)|2);
        if(Clock==null||Clock.Now>=State.NmiHoldUntil){bus.NmiFlag=false;State.NmiOutput=false;}
        return value;
    }
    public byte ReadIrq(byte mdr)
    {
        byte value=(byte)((mdr&127)|(State.IrqFlag?128:0));
        if(Clock==null||Clock.Now>=State.IrqHoldUntil)State.IrqFlag=false;
        return value;
    }
    public void LatchCounters()
    {
        if(Clock==null)return;
        State.LatchedH=(ushort)Clock.Dot;State.LatchedV=(ushort)Clock.V;State.CounterLatched=true;
    }
    public byte ReadCounter(bool vertical)
    {
        ushort value=vertical?State.LatchedV:State.LatchedH;
        bool high=vertical?State.ReadVHigh:State.ReadHHigh;
        byte result=high?(byte)((State.Ppu2Mdr&0xfe)|(value>>8)&1):(byte)value;
        if(vertical)State.ReadVHigh=!high;else State.ReadHHigh=!high;
        return State.Ppu2Mdr=result;
    }
    public byte ReadStatus()
    {
        byte result=(byte)((State.Ppu2Mdr&0x20)|3|(Clock?.Profile.Pal==true?16:0)|(State.CounterLatched?64:0)|(Clock?.Field==true?128:0));
        State.ReadHHigh=State.ReadVHigh=false;
        if((bus.Io[1]&128)!=0)State.CounterLatched=false;
        return State.Ppu2Mdr=result;
    }
}
