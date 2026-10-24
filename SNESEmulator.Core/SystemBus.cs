namespace SNESEmulator.Core;

public interface ICpuBus { byte Read(int address); void Write(int address, byte value); }
public enum AccessOrigin { Cpu, DmaA, DmaB, HdmaA, HdmaB }
public sealed record BusAccess(AccessOrigin Origin,string Direction,int Address,byte Value);

public sealed class SystemBus : ICpuBus
{
    public Action<string,int,byte>? AccessObserved {get;set;}
    public Action<BusAccess>? AccessOriginObserved {get;set;}
    public byte[] Wram {get;}=new byte[128*1024];
    public byte[] Io {get;}=new byte[0x400];
    public byte[] DmaRegisters {get;}=new byte[128];
    public Ppu Ppu {get;}=new();
    public Apu Apu {get;}=new(Apu.ResolveLocalFirmware());
    public ControllerPorts Controllers {get;}=new();
    public CpuIo CpuIo {get;}
    public DmaController Dma {get;}
    public Cartridge? Cartridge {get;set;}
    public byte OpenBus {get;set;}
    internal bool DeferCpuMdr {get;set;}
    public int WramAddress {get;set;}
    public bool NmiFlag {get;set;}
    public bool NmiPending {get;set;}
    public bool ExternalIrqLine {get=>CpuIo.State.ExternalIrq;set=>CpuIo.State.ExternalIrq=value;}
    public SystemBus(){CpuIo=new(this);Dma=new(this);}
    public static bool LowBank(int address)=>(address&0x400000)==0;
    public static bool IsWram(int address)=>(address&0xfe0000)==0x7e0000 || (address&0x40e000)==0;
    public static bool ValidDmaA(int address)=>!LowBank(address)||!((address&65535) is >=0x2100 and <=0x21ff or >=0x4000 and <=0x41ff or >=0x4200 and <=0x421f or >=0x4300 and <=0x437f);
    public byte Read(int address)=>Read(address,AccessOrigin.Cpu);
    public byte Read(int address,AccessOrigin origin)
    {
        address&=0xffffff;int bank=address>>16,a=address&65535;byte result=OpenBus;
        if(bank is 0x7e or 0x7f)result=Wram[address&0x1ffff];
        else if(LowBank(address)&&a<0x2000)result=Wram[a];
        else if(LowBank(address)&&a is >=0x2100 and <0x2140) {
            result=a switch {
                0x2137=>LatchCounters(),0x213c=>CpuIo.ReadCounter(false),0x213d=>CpuIo.ReadCounter(true),0x213f=>CpuIo.ReadStatus(),
                _=>Ppu.Read(a,OpenBus)
            };
        }
        else if(LowBank(address)&&a is >=0x2140 and <0x2180)result=Apu.ApuToCpu[a&3];
        else if(LowBank(address)&&a==0x2180){result=Wram[WramAddress];WramAddress=(WramAddress+1)&0x1ffff;}
        else if(LowBank(address)&&a==0x4016)result=(byte)((OpenBus&0xfc)|Controllers.Read(0));
        else if(LowBank(address)&&a==0x4017)result=(byte)((OpenBus&0xe0)|0x1c|Controllers.Read(1));
        else if(LowBank(address)&&a is >=0x4300 and <0x4380) {
            int r=a&15;if(r<=11||r==15)result=DmaRegisters[(a&0x70)|(r==15?11:r)];
        }
        else if(LowBank(address)&&a is >=0x4200 and <0x4400) {
            result=a switch {
                0x4210=>CpuIo.ReadNmi(OpenBus),0x4211=>CpuIo.ReadIrq(OpenBus),
                0x4212=>(byte)((OpenBus&0x3e)|(Ppu.InVblank?128:0)|((CpuIo.Clock?.H is <=2 or >=1096)?64:0)|(CpuIo.AutoBusy?1:0)),
                0x4213=>Io[1],0x4214=>(byte)CpuIo.State.Quotient,0x4215=>(byte)(CpuIo.State.Quotient>>8),
                0x4216=>(byte)CpuIo.State.Product,0x4217=>(byte)(CpuIo.State.Product>>8),
                >=0x4218 and <=0x421f=>(byte)(CpuIo.State.Joy[(a-0x4218)/2]>>((a&1)*8)),_=>OpenBus
            };
        }
        else if(Cartridge is {} cart) {
            if(cart.Hardware is {} hardware&&hardware.Decodes(address,out bool status))
                result=hardware.Read(status,CpuIo.Clock?.Now??0,CpuIo.Clock?.Profile??HardwareProfile.Ntsc);
            else {
            int sram=cart.SramOffset(address);
            if(sram>=0)result=cart.Sram[sram];
            else if(a>=0x8000||(bank&127)>=0x40)result=cart.Rom[cart.RomOffset(address)];
            }
        }
        // CPU's internal I/O read path does not drive the external MDR.
        if(origin!=AccessOrigin.Cpu||(!DeferCpuMdr&&(address&0x40fc00)!=0x4000))OpenBus=result;
        AccessOriginObserved?.Invoke(new(origin,"read",address,result));AccessObserved?.Invoke("read",address,result);return result;
    }
    private byte LatchCounters(){if((Io[1]&128)!=0)CpuIo.LatchCounters();return OpenBus;}
    public void Write(int address,byte value)=>Write(address,value,AccessOrigin.Cpu);
    public void Write(int address,byte value,AccessOrigin origin)
    {
        address&=0xffffff;OpenBus=value;int bank=address>>16,a=address&65535;
        if(bank is 0x7e or 0x7f)Wram[address&0x1ffff]=value;
        else if(LowBank(address)&&a<0x2000)Wram[a]=value;
        else if(LowBank(address)&&a is >=0x2100 and <0x2140)Ppu.Write(a,value);
        else if(LowBank(address)&&a is >=0x2140 and <0x2180)Apu.CpuToApu[a&3]=value;
        else if(LowBank(address)&&a==0x2180){Wram[WramAddress]=value;WramAddress=(WramAddress+1)&0x1ffff;}
        else if(LowBank(address)&&a is >=0x2181 and <=0x2183){int shift=(a-0x2181)*8;WramAddress=((WramAddress&~(255<<shift))|value<<shift)&0x1ffff;}
        else if(LowBank(address)&&a==0x4016)CpuIo.WriteStrobe(value);
        else if(LowBank(address)&&a is >=0x4300 and <0x4380){int r=a&15;if(r<=11||r==15)DmaRegisters[(a&0x70)|(r==15?11:r)]=value;}
        else if(LowBank(address)&&a is >=0x4200 and <=0x420d) {
            byte old=Io[a-0x4200];Io[a-0x4200]=value;
            switch(a) {
                case 0x4200:CpuIo.WriteControl(old,value);break;
                case 0x4201:if((old&128)!=0&&(value&128)==0)CpuIo.LatchCounters();break;
                case 0x4203:CpuIo.StartMultiply(value);break;
                case 0x4206:CpuIo.StartDivide(value);break;
                case >=0x4207 and <=0x420a:CpuIo.ScheduleIrq();break;
                case 0x420b:Dma.Request(value);break;
            }
        }
        else if(Cartridge is {} cart) {
            if(cart.Hardware is {} hardware&&hardware.Decodes(address,out bool status))hardware.Write(status,value,CpuIo.Clock?.Now??0,CpuIo.Clock?.Profile??HardwareProfile.Ntsc);
            else if(cart.SramOffset(address) is var sram&&sram>=0)cart.Sram[sram]=value;
        }
        AccessOriginObserved?.Invoke(new(origin,"write",address,value));AccessObserved?.Invoke("write",address,value);
    }
    public void BeginVblank()=>CpuIo.BeginVblank();
    public void RunDma(byte channels)=>Dma.Request(channels);
    // Warm reset retains WRAM and physical pad buttons. PowerOn defines a
    // reproducible RAM seed, not a measured power-on RAM pattern.
    public void Reset(bool powerOn=true)
    {
        // A lazily clocked chip may have changed retained working RAM since
        // its last port access. Commit that interval before asserting warm reset.
        if(!powerOn&&Cartridge?.Hardware is {} hardware&&CpuIo.Clock is {} clock)hardware.Synchronize(clock.Now,clock.Profile);
        if(powerOn)Array.Clear(Wram);
        Array.Clear(Io);Array.Fill(DmaRegisters,(byte)255);Io[1]=255;Io[7]=Io[9]=255;Io[8]=Io[10]=1;
        Cartridge?.Hardware?.Reset(powerOn);Ppu.Reset();Apu.Reset(powerOn);CpuIo.Reset();Dma.Reset();WramAddress=0;OpenBus=0;NmiPending=NmiFlag=false;
        if(powerOn)Array.Clear(Controllers.Buttons);
        Controllers.Latch();Controllers.Write(0);
    }
}
