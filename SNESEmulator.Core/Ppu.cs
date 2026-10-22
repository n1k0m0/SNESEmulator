namespace SNESEmulator.Core;

/// <summary>Own two-chip PPU. Timing/evidence contract: docs/m5-ppu-contract.md.</summary>
public sealed partial class Ppu
{
    public const int Width=256,Height=224;
    public byte[] Vram {get;}=new byte[65536];
    public byte[] Cgram {get;}=new byte[512];
    public byte[] Oam {get;}=new byte[544];
    public byte[] Registers {get;}=new byte[64];
    public uint[] Pixels {get;}=new uint[Width*Height];
    // Native digital RGB555 before display brightness, plus the independent DAC control.
    public ushort[] NativePixels {get;}=new ushort[Width*Height];
    public byte[] Brightness {get;}=new byte[Width*Height];
    public ushort[] Scroll {get;}=new ushort[8];
    public ushort VramAddress {get;set;}
    public ushort CgramAddress {get;set;}
    public ushort OamAddress {get;set;}
    public byte ScrollLatch {get;set;}
    public int Scanline {get;set;}
    public byte Ppu1Mdr {get;set;}
    public byte Ppu2Mdr {get;set;}
    public MasterClock? Clock {get;set;}
    private ushort vramBuffer,oamReload,fixedColor;
    private byte oamLatch,cgramLatch,scrollFineLatch,mode7Latch;
    private readonly ushort[] mode7=new ushort[8];
    public bool RangeOver {get;private set;}
    public bool TimeOver {get;private set;}
    public bool ForcedBlank=>(Registers[0]&128)!=0;
    public bool InVblank=>Scanline>=((Registers[0x33]&4)!=0?240:225);
    private bool MemoryBlank=>ForcedBlank||InVblank;
    private int Increment=>(Registers[0x15]&3) switch{0=>1,1=>32,_=>128};
    private int OamIndex(int address)=>(address&512)!=0?512+(address&31):address&511;
    private int VramByteAddress(bool high)
    {
        int a=VramAddress;
        a=((Registers[0x15]>>2)&3) switch{
            1=>(a&0xff00)|((a&31)<<3)|((a>>5)&7),
            2=>(a&0xfe00)|((a&63)<<3)|((a>>6)&7),
            3=>(a&0xfc00)|((a&127)<<3)|((a>>7)&7),_=>a};
        return ((a<<1)|(high?1:0))&65535;
    }
    private void PrefetchVram()=>vramBuffer=MemoryBlank?(ushort)(Vram[VramByteAddress(false)]|Vram[VramByteAddress(true)]<<8):(ushort)0;
    public void Reset()
    {
        Array.Clear(Vram);Array.Clear(Cgram);Array.Clear(Oam);Array.Clear(Registers);Array.Clear(Scroll);Array.Clear(mode7);
        Array.Clear(NativePixels);Array.Clear(Brightness);Array.Fill(Pixels,0xff000000u);
        VramAddress=CgramAddress=OamAddress=vramBuffer=oamReload=fixedColor=0;
        ScrollLatch=oamLatch=cgramLatch=scrollFineLatch=mode7Latch=Ppu1Mdr=Ppu2Mdr=0;
        Scanline=0;RangeOver=TimeOver=false;Registers[0]=128;ResetPipeline();
    }
    public void StartLine(int line)
    {
        Scanline=line;
        if(line==0){RangeOver=TimeOver=false;mosaicStart=1;}
        if(line==((Registers[0x33]&4)!=0?240:225)&&!ForcedBlank)OamAddress=oamReload;
        BeginPipelineLine();
    }
    public void Write(int register,byte value)
    {
        register&=63;if(register>=0x34)return;
        byte old=Registers[register];Registers[register]=value;
        switch(register)
        {
            case 0:if((old&128)!=0&&Scanline==((Registers[0x33]&4)!=0?240:225))OamAddress=oamReload;break;
            case 2:case 3:
                oamReload=(ushort)((Registers[2]|(Registers[3]&1)<<8)<<1);OamAddress=oamReload;break;
            case 4:
                int oa=OamAddress&1023;
                // During active display the OAM address bus belongs to evaluation/fetch.
                int target=MemoryBlank?oa:internalOamAddress;
                if((oa&1)==0)oamLatch=value;
                if((oa&512)!=0)Oam[OamIndex(target)]=value;
                else if((oa&1)!=0){
                    Oam[OamIndex(MemoryBlank?(oa&~1):target)]=oamLatch;
                    Oam[OamIndex(MemoryBlank?(oa|1):target)]=value;
                }
                OamAddress=(ushort)((oa+1)&1023);break;
            case 6:if((old&15)==0&&(value&15)!=0)mosaicRemaining=(value>>4)+2;break;
            case >=0x0d and <=0x14:
                int i=register-0x0d;
                if((i&1)==0){Scroll[i]=(ushort)(((value<<8)|(ScrollLatch&0xf8)|(scrollFineLatch&7))&1023);scrollFineLatch=value;}
                else Scroll[i]=(ushort)(((value<<8)|ScrollLatch)&1023);
                ScrollLatch=value;
                if(register<=0x0e)WriteMode7(6+register-0x0d,value);
                break;
            case 0x16:VramAddress=(ushort)((VramAddress&0xff00)|value);PrefetchVram();break;
            case 0x17:VramAddress=(ushort)((VramAddress&255)|value<<8);PrefetchVram();break;
            case 0x18:case 0x19:
                bool high=register==0x19;
                if(MemoryBlank)Vram[VramByteAddress(high)]=value;
                if(((Registers[0x15]&128)!=0)==high)VramAddress=unchecked((ushort)(VramAddress+Increment));
                break;
            case >=0x1b and <=0x20:WriteMode7(register-0x1b,value);break;
            case 0x21:CgramAddress=(ushort)(value*2);break;
            case 0x22:
                int ca=MemoryBlank||Clock==null||Scanline==0||Clock.H<88||Clock.H>=1096?CgramAddress:internalCgramAddress;
                if((CgramAddress&1)==0)cgramLatch=value;
                else{Cgram[ca&510]=cgramLatch;Cgram[(ca&510)+1]=(byte)(value&127);}
                CgramAddress=(ushort)((CgramAddress+1)&511);break;
            case 0x32:
                if((value&32)!=0)fixedColor=(ushort)((fixedColor&~31)|(value&31));
                if((value&64)!=0)fixedColor=(ushort)((fixedColor&~992)|((value&31)<<5));
                if((value&128)!=0)fixedColor=(ushort)((fixedColor&~31744)|((value&31)<<10));
                break;
        }
    }
    private void WriteMode7(int i,byte value){mode7[i]=(ushort)(value<<8|mode7Latch);mode7Latch=value;}
    public byte Read(int register,byte cpuMdr=0)
    {
        register&=63;
        switch(register)
        {
            case >=0x34 and <=0x36:
                int product=(short)mode7[0]*(sbyte)(mode7[1]>>8);return Ppu1Mdr=(byte)(product>>((register-0x34)*8));
            case 0x38:
                byte data=Oam[OamIndex(MemoryBlank?OamAddress:internalOamAddress)];OamAddress=(ushort)((OamAddress+1)&1023);return Ppu1Mdr=data;
            case 0x39:case 0x3a:
                bool high=register==0x3a;byte result=(byte)(vramBuffer>>(high?8:0));
                if(((Registers[0x15]&128)!=0)==high){PrefetchVram();VramAddress=unchecked((ushort)(VramAddress+Increment));}
                return Ppu1Mdr=result;
            case 0x3b:
                int ca=MemoryBlank||Clock==null||Scanline==0||Clock.H<88||Clock.H>=1096?CgramAddress:internalCgramAddress;
                byte cg=Cgram[(ca&510)|(CgramAddress&1)];if((CgramAddress&1)!=0)cg=(byte)((cg&127)|(Ppu2Mdr&128));
                CgramAddress=(ushort)((CgramAddress+1)&511);return Ppu2Mdr=cg;
            case 0x3e:return Ppu1Mdr=(byte)((Ppu1Mdr&16)|1|(RangeOver?64:0)|(TimeOver?128:0));
        }
        return register<0x30&&(register&15) is 4 or 5 or 6 or 8 or 9 or 10?Ppu1Mdr:cpuMdr;
    }
    private ushort Palette(int index)
    {internalCgramAddress=(index&255)*2;return (ushort)((Cgram[internalCgramAddress]|Cgram[internalCgramAddress+1]<<8)&32767);}
    public static ushort ApplyBrightness(ushort color,byte brightness)
    {
        if(brightness>15)throw new ArgumentOutOfRangeException(nameof(brightness));
        int result=0;for(int shift=0;shift<15;shift+=5)result|=((((color>>shift)&31)*brightness*2+15)/30)<<shift;
        return (ushort)result;
    }
    public static uint ToArgb(ushort color,byte brightness)
    {
        int digital=ApplyBrightness(color,brightness);
        int r=(digital&31)*255/31,g=((digital>>5)&31)*255/31,b=((digital>>10)&31)*255/31;
        return 0xff000000u|(uint)(r<<16|g<<8|b);
    }
    public PpuFrame Frame=>new(new(Width,Height,Width,Clock?.Field??false),NativePixels,Brightness,Pixels);
}
public sealed record PpuGeometry(int Width,int Height,int Stride,bool Field);
public sealed record PpuFrame(PpuGeometry Geometry,ReadOnlyMemory<ushort> Native,ReadOnlyMemory<byte> Brightness,ReadOnlyMemory<uint> Argb);
