// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE and NOTICE for permissions and attribution.
//
// Implements the independent SuperFX instruction, cache and register model.
// Instruction microphases retain pending reads and writes across budget boundaries.
//

namespace SNESEmulator.Core;

// The sequencer contains data, never delegates or CLR iterators. A snapshot can
// therefore stop inside a cache fill, delayed store, operand fetch or planar RMW.
public enum GsuMicroKind { Delay, ReadRam, BufferWrite, ReadGraphic, WriteGraphic, Execute, Finish }
public sealed record GsuMicro(GsuMicroKind Kind,int A=0,int B=0,int C=0);
public sealed record GsuInstructionTrace(long Tick,int Opcode,int Source,int Destination,int SourceValue,int R4Before,int R6,int R4After,int DestinationAfter,ushort Status);
public sealed record GsuPixel(int Offset,byte Mask,byte[] Colors);
public sealed record GsuState(int Version,long Tick,long Instructions,ushort[] Registers,ushort Status,int Source,int Destination,
    byte ProgramBank,byte RomBank,byte RamBank,ushort CacheBase,byte ScreenBase,byte ScreenMode,byte Config,byte ClockSelect,
    byte Bram,byte Color,byte PlotOptions,byte Pipeline,byte[] Cache,bool[] Valid,GsuPixel[] Pixels,
    int Phase,int Opcode,int OperandCount,int OperandIndex,byte[] Operands,bool PcWritten,int RamAddress,int MemoryValue,
    GsuMicro[] Operations,int OperationIndex,long OperationDue,int FetchAddress,int FetchOffset,int FillIndex,long FetchDue,
    int GraphicByte,long RomDue,byte RomData,int RomAddress,long RamDue,int RamWriteAddress,byte RamData,bool RamSecondPending,int RamSecondAddress,byte RamSecondData);

/// <summary>Own digital GSU interpreter; oscillator ticks are independent of the S-CPU master clock.</summary>
public sealed class Gsu
{
    private readonly byte[] rom,ram;
    public ushort[] Registers {get;}=new ushort[16];
    public ushort Status {get;private set;}
    public long Tick {get;private set;}
    public long Instructions {get;private set;}
    public bool Running=>(Status&0x20)!=0;
    public bool RomOwned=>(screenMode&0x10)!=0;
    public bool RamOwned=>(screenMode&8)!=0;
    public bool Irq=>(Status&0x8000)!=0;
    public bool IrqOutput=>Irq&&(config&128)==0;
    public Action<bool>? IrqChanged {get;set;}
    public Action<GsuInstructionTrace>? InstructionObserved {get;set;}
    private int source,destination;
    private byte programBank,romBank,ramBank,screenBase,screenMode,config,clockSelect,bram,color,plotOptions,pipeline=1;
    private ushort cacheBase;
    private readonly byte[] cache=new byte[512],operands=new byte[2];
    private readonly bool[] valid=new bool[32];
    private GsuPixel[] pixels=[new(-1,0,new byte[8]),new(-1,0,new byte[8])];
    private int phase,opcode,operandCount,operandIndex,ramAddress,memoryValue,operationIndex,fetchAddress,fetchOffset,fillIndex;
    private bool pcWritten;
    private readonly List<GsuMicro> operations=[];
    private long operationDue=-1,fetchDue=-1,romDue=-1,ramDue=-1;
    private int romAddress,ramWriteAddress;
    private byte romData,ramData,ramSecondData;
    private bool ramSecondPending;
    private int ramSecondAddress;
    private int MemoryTicks=>clockSelect==0?6:5;
    private int AluTicks=>clockSelect==0?2:1;
    private bool A1=>(Status&0x100)!=0;
    private bool A2=>(Status&0x200)!=0;
    private bool Carry=>(Status&4)!=0;
    public Gsu(byte[] rom,byte[] ram){if(rom.Length==0||ram.Length==0)throw new ArgumentException("GSU memories must exist.");this.rom=rom;this.ram=ram;Reset(true);}
    private void Flag(int mask,bool value){Status=(ushort)(value?Status|mask:Status&~mask);}
    private void Sz(int value,int signMask=0x8000){Flag(8,(value&signMask)!=0);Flag(2,(ushort)value==0);}
    private void Put(int index,int value)
    {
        Registers[index]=(ushort)value;
        if(index==15)pcWritten=true;
        if(index==14){romAddress=(romBank<<16)|Registers[14];romDue=Tick+MemoryTicks;Flag(0x40,true);}
    }
    private void ClearPrefixes(){source=destination=0;Status&=0xecff;}
    public void Reset(bool powerOn)
    {
        // Undefined power-on registers use a deterministic seed. Warm reset
        // retains physical cartridge RAM; this is not a measured RAM pattern.
        Array.Clear(Registers);Array.Clear(cache);Array.Clear(valid);Array.Clear(operands);
        pixels=[new(-1,0,new byte[8]),new(-1,0,new byte[8])];
        Tick=Instructions=0;Status=cacheBase=0;source=destination=0;
        programBank=romBank=ramBank=screenBase=screenMode=config=clockSelect=bram=color=plotOptions=0;pipeline=1;
        phase=opcode=operandCount=operandIndex=ramAddress=memoryValue=operationIndex=fetchAddress=fetchOffset=fillIndex=0;
        pcWritten=false;operations.Clear();operationDue=fetchDue=romDue=ramDue=-1;romAddress=ramWriteAddress=0;romData=ramData=ramSecondData=0;ramSecondPending=false;ramSecondAddress=0;IrqChanged?.Invoke(false);
    }
    public byte ReadIo(int address)
    {
        int a=address&1023;
        if(a is >=0x100 and <=0x2ff)return cache[(a-0x100+cacheBase)&511];
        if(a<32)return (byte)(Registers[a>>1]>>((a&1)*8));
        if(a==0x31){byte value=(byte)(Status>>8);Flag(0x8000,false);IrqChanged?.Invoke(false);return value;}
        return a switch {0x30=>(byte)Status,0x34=>programBank,0x36=>romBank,0x3b=>4,0x3c=>ramBank,0x3e=>(byte)cacheBase,0x3f=>(byte)(cacheBase>>8),_=>0};
    }
    public void WriteIo(int address,byte value)
    {
        int a=address&1023;
        if(a is >=0x100 and <=0x2ff){int p=(a-0x100+cacheBase)&511;cache[p]=value;if((p&15)==15)valid[p>>4]=true;return;}
        if(a<32){int n=a>>1;Put(n,(Registers[n]&(a%2==0?0xff00:255))|(value<<((a&1)*8)));if(a==31)Flag(32,true);return;}
        switch(a)
        {
            case 0x30:
                bool wasRunning=Running;Status=(ushort)((Status&0xffc0)|(value&0x3e));
                if(wasRunning&&!Running){cacheBase=0;Array.Clear(valid);phase=0;operations.Clear();operationDue=fetchDue=-1;pipeline=1;}
                break;
            case 0x31:Status=(ushort)((Status&255)|((value&0x9f)<<8));IrqChanged?.Invoke(IrqOutput);break;
            case 0x33:bram=(byte)(value&1);break;
            case 0x34:programBank=(byte)(value&127);Array.Clear(valid);break;
            case 0x37:config=(byte)(value&0xa0);IrqChanged?.Invoke(IrqOutput);break;
            case 0x38:screenBase=value;break;
            case 0x39:clockSelect=(byte)(value&1);break;
            case 0x3a:screenMode=(byte)(value&63);break;
        }
    }
    private bool BusReady(int address)=>address<0x600000?RomOwned:RamOwned;
    private byte ReadMemory(int address)
    {
        int bank=(address>>16)&127;
        if(bank<0x40)return rom[Cartridge.Mirror((bank<<15)|(address&32767),rom.Length)];
        if(bank<0x60)return rom[Cartridge.Mirror(address&0x1fffff,rom.Length)];
        return ram[Cartridge.Mirror(address&0x1fffff,ram.Length)];
    }
    private int RamIndex(int address)=>Cartridge.Mirror(address&0x1fffff,ram.Length);
    private long Due(long value,bool ready)=>value<0||!ready?long.MaxValue:Math.Max(Tick,value);
    private bool FetchReady=>fetchOffset>=0&&valid[fetchOffset>>4]||BusReady((programBank<<16)|fetchAddress);
    public long NextBoundary
    {
        get
        {
            long next=Math.Min(Due(romDue,BusReady(romAddress)),Due(ramDue,RamOwned));
            if(!Running)return next;
            if(phase is 1 or 3)return Math.Min(next,Due(fetchDue,FetchReady));
            if(phase==4&&operationIndex<operations.Count)
            {
                var op=operations[operationIndex];
                if((op.Kind==GsuMicroKind.ReadRam||op.Kind==GsuMicroKind.BufferWrite&&!(operationIndex>0&&operations[operationIndex-1].Kind==GsuMicroKind.BufferWrite))&&ramDue>=0)return next;
                if(op.Kind is GsuMicroKind.ReadRam or GsuMicroKind.ReadGraphic or GsuMicroKind.WriteGraphic or GsuMicroKind.BufferWrite&&!RamOwned)return next;
                if(op.Kind==GsuMicroKind.Execute&&((opcode==0xef||opcode==0xdf&&!A2)&&romDue>=0||opcode==0xdf&&A2&&(A1?romDue>=0:ramDue>=0)))return next;
                return Math.Min(next,operationDue<0?Tick:Math.Max(Tick,operationDue));
            }
            return Math.Min(next,Tick);
        }
    }
    public void AdvanceTo(long target)
    {
        if(target<Tick)throw new ArgumentOutOfRangeException(nameof(target));
        while(NextBoundary<=target)
        {
            long next=NextBoundary;ElapseTo(next);
            if(romDue>=0&&romDue<=Tick&&BusReady(romAddress)){romData=ReadMemory(romAddress);romDue=-1;Flag(0x40,false);}
            if(ramDue>=0&&ramDue<=Tick&&RamOwned){ram[RamIndex(ramWriteAddress)]=ramData;if(ramSecondPending){ramWriteAddress=ramSecondAddress;ramData=ramSecondData;ramDue=Tick+MemoryTicks;ramSecondPending=false;}else ramDue=-1;}
            if(!Running)continue;
            if(phase==0){opcode=pipeline;pcWritten=false;operandCount=opcode is >=5 and <=15 or >=0xa0 and <=0xaf?1:opcode>=0xf0?2:0;operandIndex=0;StartFetch(false);}
            else if(phase is 1 or 3){if(fetchDue<=Tick&&FetchReady)CompleteFetch();}
            else if(phase==2)
            {
                if(operandIndex<operandCount){operands[operandIndex++]=pipeline;Registers[15]++;StartFetch(true);}
                else {BuildOperations();phase=4;}
            }
            else RunOperation();
        }
        ElapseTo(target);
    }
    private void ElapseTo(long target)
    {
        long elapsed=target-Tick;
        // WAIT suspends the outstanding bus transfer, not just the interpreter.
        // Advancing the other processor cannot consume denied-bus latency.
        if(elapsed>0){
            if(romDue>=0&&!BusReady(romAddress))romDue=checked(romDue+elapsed);
            if(ramDue>=0&&!RamOwned)ramDue=checked(ramDue+elapsed);
            if(fetchDue>=0&&!FetchReady)fetchDue=checked(fetchDue+elapsed);
            if(operationDue>=0&&phase==4&&operations[operationIndex].Kind is GsuMicroKind.ReadRam or GsuMicroKind.ReadGraphic or GsuMicroKind.WriteGraphic&&!RamOwned)operationDue=checked(operationDue+elapsed);
        }
        Tick=target;
    }
    private void StartFetch(bool operand)
    {
        phase=operand?3:1;fetchAddress=Registers[15];int offset=(ushort)(fetchAddress-cacheBase);fetchOffset=offset<512?offset:-1;fillIndex=0;
        if(fetchOffset>=0&&!valid[fetchOffset>>4])fetchDue=Tick+MemoryTicks;
        else
        {
            // A non-cache instruction fetch shares its memory port with the
            // corresponding data buffer. Cache hits require neither bus grant.
            long buffer=programBank<0x60?romDue:ramDue;
            fetchDue=Tick+(fetchOffset>=0?AluTicks:MemoryTicks);
            if(fetchOffset<0&&buffer>=0)fetchDue=Math.Max(fetchDue,buffer+MemoryTicks);
        }
    }
    private void CompleteFetch()
    {
        if(fetchOffset>=0&&!valid[fetchOffset>>4])
        {
            int start=fetchOffset&~15;
            cache[start+fillIndex]=ReadMemory((programBank<<16)|(ushort)(cacheBase+start+fillIndex));
            if(++fillIndex<16){fetchDue=Tick+MemoryTicks;return;}
            valid[fetchOffset>>4]=true;
        }
        pipeline=fetchOffset>=0?cache[fetchOffset]:ReadMemory((programBank<<16)|fetchAddress);
        fetchDue=-1;phase=2;
    }
    private void Add(GsuMicroKind kind,int a=0,int b=0,int c=0)=>operations.Add(new(kind,a,b,c));
    private void ReadWord(int address,bool bytes=false){ramAddress=address;Add(GsuMicroKind.ReadRam,(ramBank<<16)|address,0);if(!bytes)Add(GsuMicroKind.ReadRam,(ramBank<<16)|(address^1),8);if(clockSelect==0&&(bytes||opcode>=0xa0))Add(GsuMicroKind.Delay,AluTicks);}
    private void StoreWord(int address,int value,bool bytes=false){ramAddress=address;Add(GsuMicroKind.BufferWrite,(ramBank<<16)|address,(byte)value);if(!bytes)Add(GsuMicroKind.BufferWrite,(ramBank<<16)|(address^1),(byte)(value>>8));}
    private int Bpp=>(screenMode&3) switch {0=>2,3=>8,_=>4};
    private int GraphicAddress(int x,int y)
    {
        int tile;
        int height=(plotOptions&16)!=0?3:((screenMode>>2)&1)|((screenMode>>4)&2);
        if(height==3)tile=(y/128)*512+(x/128)*256+((y&127)/8)*16+(x&127)/8;
        else tile=(x/8)*(16+height*4)+y/8;
        return (screenBase<<10)+tile*Bpp*8+(y&7)*2;
    }
    private void FlushPixel(int index)
    {
        var p=pixels[index];if(p.Mask==0)return;
        int address=GraphicAddress((p.Offset&31)*8,p.Offset>>5);
        for(int plane=0;plane<Bpp;plane++)
        {
            int bits=0;for(int bit=0;bit<8;bit++)bits|=((p.Colors[bit]>>plane)&1)<<bit;
            int a=address+(plane/2)*16+plane%2;
            if(p.Mask!=255)Add(GsuMicroKind.ReadGraphic,a);
            Add(GsuMicroKind.WriteGraphic,a,bits,p.Mask);
        }
        pixels[index]=p with{Mask=0};
    }
    private void PlanPlot()
    {
        int x=(byte)Registers[1],y=(byte)Registers[2];
        if(A1)
        {
            FlushPixel(1);FlushPixel(0);int address=GraphicAddress(x,y);memoryValue=0;
            for(int plane=0;plane<Bpp;plane++)Add(GsuMicroKind.ReadGraphic,address+(plane/2)*16+plane%2,plane+1,7-(x&7));
            return;
        }
        byte c=color;if((plotOptions&2)!=0&&Bpp!=8)c=(byte)((((x^y)&1)!=0?c>>4:c)&15);
        if((plotOptions&1)==0&&((Bpp==8&&(plotOptions&8)==0?c:c&(Bpp==2?3:15))==0))return;
        int row=y*32+x/8;
        if(pixels[0].Offset!=row){FlushPixel(1);pixels[1]=pixels[0];pixels[0]=new(row,0,new byte[8]);}
        int bit=7-(x&7);pixels[0].Colors[bit]=c;pixels[0]=pixels[0] with{Mask=(byte)(pixels[0].Mask|(1<<bit))};
        if(pixels[0].Mask==255){FlushPixel(1);pixels[1]=pixels[0];pixels[0]=new(row,0,new byte[8]);}
    }
    private void BuildOperations()
    {
        operations.Clear();operationIndex=0;operationDue=-1;memoryValue=0;int n=opcode&15,s=Registers[source];
        if(opcode is >=0x30 and <=0x3b)StoreWord(Registers[n],s,A1);
        else if(opcode is >=0x40 and <=0x4b)ReadWord(Registers[n],A1);
        else if(opcode==0x90)StoreWord(ramAddress,s);
        else if(opcode is >=0xa0 and <=0xaf or >=0xf0)
        {
            int address=operandCount==1?operands[0]*2:operands[0]|operands[1]<<8;
            if(A1)ReadWord(address);else if(A2)StoreWord(address,Registers[n]);
        }
        else if(opcode==0x4c)PlanPlot();
        Add(GsuMicroKind.Execute);
        if(opcode is >=0x80 and <=0x8f&&(config&32)==0)Add(GsuMicroKind.Delay,AluTicks);
        if(opcode==0x9f)Add(GsuMicroKind.Delay,((config&32)!=0?3:7)*AluTicks);
        Add(GsuMicroKind.Finish);
    }
    private int graphicByte;
    private void RunOperation()
    {
        if(operationIndex>=operations.Count)throw new InvalidOperationException("Missing GSU finish operation.");
        var op=operations[operationIndex];
        if(op.Kind is GsuMicroKind.Delay or GsuMicroKind.ReadGraphic or GsuMicroKind.WriteGraphic or GsuMicroKind.ReadRam)
        {if(operationDue<0){operationDue=Tick+(op.Kind==GsuMicroKind.Delay?op.A:MemoryTicks);return;}if(Tick<operationDue)return;}
        switch(op.Kind)
        {
            case GsuMicroKind.ReadRam:memoryValue|=ram[RamIndex(op.A)]<<op.B;break;
            case GsuMicroKind.BufferWrite:if(ramDue>=0){ramSecondPending=true;ramSecondAddress=op.A;ramSecondData=(byte)op.B;}else{ramWriteAddress=op.A;ramData=(byte)op.B;ramDue=Tick+MemoryTicks;}break;
            case GsuMicroKind.ReadGraphic:
                graphicByte=ram[RamIndex(op.A)];
                if(op.B>0)memoryValue|=((graphicByte>>op.C)&1)<<(op.B-1);
                break;
            case GsuMicroKind.WriteGraphic:ram[RamIndex(op.A)]=(byte)((op.B&op.C)|(graphicByte&~op.C));break;
            case GsuMicroKind.Execute:
                Execute();
                if(!Running){if(!pcWritten)Registers[15]++;Instructions++;phase=0;operations.Clear();operationIndex=0;operationDue=-1;return;}
                break;
            case GsuMicroKind.Finish:
                if(!pcWritten)Registers[15]++;
                Instructions++;phase=0;operations.Clear();operationIndex=0;operationDue=-1;return;
        }
        operationIndex++;operationDue=-1;
    }
    private byte SelectColor(int value)=>(byte)((plotOptions&4)!=0?(color&0xf0)|((value>>4)&15):(plotOptions&8)!=0?(color&0xf0)|(value&15):value);
    private void Execute()
    {
        int sourceIndex=source,r4Before=Registers[4],r6Before=Registers[6];
        int n=opcode&15,s=Registers[source],v=Registers[n],d=destination,r=0;bool reset=true;
        if(opcode is >=5 and <=15)
        {
            bool sign=(Status&8)!=0,overflow=(Status&16)!=0;
            bool take=opcode switch {5=>true,6=>sign==overflow,7=>sign!=overflow,8=>(Status&2)==0,9=>(Status&2)!=0,10=>!sign,11=>sign,12=>!Carry,13=>Carry,14=>!overflow,_=>overflow};
            if(take)Put(15,Registers[15]+(sbyte)operands[0]);reset=false;
        }
        else if(opcode is >=0x10 and <=0x1f){if((Status&0x1000)==0){destination=n;reset=false;}else Put(n,s);}
        else if(opcode is >=0x20 and <=0x2f){source=destination=n;Flag(0x1000,true);reset=false;}
        else if(opcode is >=0x30 and <=0x3b||opcode==0x90){}
        else if(opcode is >=0x40 and <=0x4b)Put(d,memoryValue);
        else if(opcode is >=0x50 and <=0x5f)
        {
            int rhs=A2?n:v;r=s+rhs+(A1&&Carry?1:0);Flag(16,((~(s^rhs)&(s^r))&0x8000)!=0);Flag(4,r>65535);Put(d,r);Sz(r);
        }
        else if(opcode is >=0x60 and <=0x6f)
        {
            int rhs=A2&&!A1?n:v;r=s-rhs-(A1&&!A2&&!Carry?1:0);Flag(16,((s^rhs)&(s^r)&0x8000)!=0);Flag(4,r>=0);if(!(A1&&A2))Put(d,r);Sz(r);
        }
        else if(opcode is >=0x71 and <=0x7f){v=A2?n:v;r=s&(A1?~v:v);Put(d,r);Sz(r);}
        else if(opcode is >=0x80 and <=0x8f){v=A2?n:v;r=A1?(byte)s*(byte)v:(sbyte)s*(sbyte)v;Put(d,r);Sz(r);}
        else if(opcode is >=0x91 and <=0x94)Put(11,Registers[15]+n);
        else if(opcode is >=0x98 and <=0x9d)
        {
            if(A1){programBank=(byte)(v&127);Put(15,s);cacheBase=(ushort)(s&0xfff0);Array.Clear(valid);}else Put(15,v);
        }
        else if(opcode is >=0xa0 and <=0xaf or >=0xf0)
        {if(A1)Put(n,memoryValue);else if(!A2)Put(n,operandCount==1?(sbyte)operands[0]:operands[0]|operands[1]<<8);}
        else if(opcode is >=0xb0 and <=0xbf)
        {if((Status&0x1000)==0){source=n;reset=false;}else{Put(d,v);Sz(v);Flag(16,(v&128)!=0);}}
        else if(opcode is >=0xc1 and <=0xcf){v=A2?n:v;r=A1?s^v:s|v;Put(d,r);Sz(r);}
        else if(opcode is >=0xd0 and <=0xde){Put(n,v+1);Sz(Registers[n]);}
        else if(opcode is >=0xe0 and <=0xee){Put(n,v-1);Sz(Registers[n]);}
        else switch(opcode)
        {
            case 0:Flag(32,false);pipeline=1;Flag(0x8000,true);IrqChanged?.Invoke(IrqOutput);break;
            case 1:break;
            case 2:ushort next=(ushort)(Registers[15]&0xfff0);if(cacheBase!=next){cacheBase=next;Array.Clear(valid);}break;
            case 3:Flag(4,(s&1)!=0);r=s>>1;Put(d,r);Sz(r);break;
            case 4:r=(s<<1)|(Carry?1:0);Flag(4,(s&0x8000)!=0);Put(d,r);Sz(r);break;
            case 0x3c:Put(12,Registers[12]-1);Sz(Registers[12]);if(Registers[12]!=0)Put(15,Registers[13]);break;
            case 0x3d:Flag(0x1000,false);Flag(0x100,true);reset=false;break;
            case 0x3e:Flag(0x1000,false);Flag(0x200,true);reset=false;break;
            case 0x3f:Flag(0x1000,false);Flag(0x300,true);reset=false;break;
            case 0x4c:if(A1){Put(d,memoryValue);Sz(memoryValue);}else Put(1,Registers[1]+1);break;
            case 0x4d:r=(s>>8)|(s<<8);Put(d,r);Sz(r);break;
            case 0x4e:if(A1)plotOptions=(byte)(s&31);else color=SelectColor(s);break;
            case 0x4f:r=~s;Put(d,r);Sz(r);break;
            case 0x70:r=(Registers[7]&0xff00)|(Registers[8]>>8);Put(d,r);Flag(16,(r&0xc0c0)!=0);Flag(8,(r&0x8080)!=0);Flag(4,(r&0xe0e0)!=0);Flag(2,(r&0xf0f0)!=0);break;
            case 0x95:r=(sbyte)s;Put(d,r);Sz(r);break;
            case 0x96:Flag(4,(s&1)!=0);r=((short)s>>1)+(A1&&s==0xffff?1:0);Put(d,r);Sz(r);break;
            case 0x97:r=(s>>1)|(Carry?0x8000:0);Flag(4,(s&1)!=0);Put(d,r);Sz(r);break;
            case 0x9e:r=(byte)s;Put(d,r);Sz(r,128);break;
            case 0x9f:int product=(short)s*(short)Registers[6];if(A1)Put(4,product);r=(int)((uint)product>>16);if(A1||d!=4)Put(d,r);Sz(r);Flag(4,(product&0x8000)!=0);break;
            case 0xc0:r=s>>8;Put(d,r);Sz(r,128);break;
            case 0xdf:if(!A2)color=SelectColor(romData);else if(A1)romBank=(byte)(s&127);else ramBank=(byte)(s&1);break;
            case 0xef:r=(A1,A2) switch {(false,false)=>romData,(true,false)=>(romData<<8)|(s&255),(false,true)=>(s&0xff00)|romData,_=>(sbyte)romData};Put(d,r);break;
            default:throw new InvalidOperationException("Undecoded GSU opcode.");
        }
        InstructionObserved?.Invoke(new(Tick,opcode,sourceIndex,d,s,r4Before,r6Before,Registers[4],Registers[d],Status));
        if(reset)ClearPrefixes();
    }
    public GsuState Capture()=>new(1,Tick,Instructions,Registers.ToArray(),Status,source,destination,programBank,romBank,ramBank,cacheBase,screenBase,screenMode,config,clockSelect,bram,color,plotOptions,pipeline,cache.ToArray(),valid.ToArray(),pixels.Select(p=>p with{Colors=p.Colors.ToArray()}).ToArray(),phase,opcode,operandCount,operandIndex,operands.ToArray(),pcWritten,ramAddress,memoryValue,operations.ToArray(),operationIndex,operationDue,fetchAddress,fetchOffset,fillIndex,fetchDue,graphicByte,romDue,romData,romAddress,ramDue,ramWriteAddress,ramData,ramSecondPending,ramSecondAddress,ramSecondData);
    private static void ValidateContinuation(GsuState s)
    {
        if((s.Config&~0xa0)!=0)throw new InvalidDataException("Invalid GSU configuration bits.");
        if((s.Status&~0x9f7e)!=0||s.OperationDue>s.Tick+14||s.FetchDue>s.Tick+12||s.RomDue>s.Tick+6||s.RamDue>s.Tick+6)
            throw new InvalidDataException("Invalid GSU status or deadline distance.");
        if(s.RamSecondPending&&s.RamSecondAddress!=(s.RamWriteAddress^1))throw new InvalidDataException("Invalid GSU buffered word pair.");
        if(s.Phase is 1 or 3)
        {
            int expected=(ushort)(s.FetchAddress-s.CacheBase);expected=expected<512?expected:-1;
            if(s.FetchOffset!=expected||s.FillIndex>15||s.Phase==1&&s.OperandIndex!=0||s.Phase==3&&s.OperandIndex==0)
                throw new InvalidDataException("Invalid GSU fetch phase.");
        }
        else if(s.FetchDue!=-1)throw new InvalidDataException("Fetch deadline without fetch.");
        if(s.Phase!=4)
        {
            if(s.Operations.Length!=0||s.OperationIndex!=0||s.OperationDue!=-1)throw new InvalidDataException("Micro-operations outside execution phase.");
            return;
        }
        var ops=s.Operations;
        if(ops.Length<2||ops[^1].Kind!=GsuMicroKind.Finish||ops.Count(o=>o.Kind==GsuMicroKind.Finish)!=1||ops.Count(o=>o.Kind==GsuMicroKind.Execute)!=1)
            throw new InvalidDataException("Invalid GSU instruction terminator.");
        int execute=Array.FindIndex(ops,o=>o.Kind==GsuMicroKind.Execute);
        bool alt1=(s.Status&0x100)!=0,alt2=(s.Status&0x200)!=0;
        // Prefix flags have already reset after Execute, so check ALT-dependent
        // queue shapes only while the memory work is still pending.
        bool beforeExecute=s.OperationIndex<=execute;
        bool immediate=s.Opcode is >=0xa0 and <=0xaf or >=0xf0;
        bool load=s.Opcode is >=0x40 and <=0x4b||immediate&&(!beforeExecute||alt1);
        bool store=s.Opcode is >=0x30 and <=0x3b or 0x90||immediate&&(!beforeExecute||!alt1&&alt2);
        for(int i=0;i<ops.Length;i++)
        {
            var o=ops[i];bool good=o.Kind switch {
                GsuMicroKind.ReadRam=>load&&i<execute&&o.A<=0x1ffff&&o.B is 0 or 8&&o.C==0,
                GsuMicroKind.BufferWrite=>store&&i<execute&&o.A<=0x1ffff&&o.C==0,
                GsuMicroKind.ReadGraphic=>s.Opcode==0x4c&&i<execute&&o.A<=0x7ffff&&o.B<=8&&o.C<=7,
                GsuMicroKind.WriteGraphic=>s.Opcode==0x4c&&i<execute&&o.A<=0x7ffff&&o.C>0,
                GsuMicroKind.Delay=>o.B==0&&o.C==0&&o.A>0&&o.A<=14&&(i<execute&&load||i>execute&&s.Opcode is >=0x80 and <=0x8f or 0x9f),
                GsuMicroKind.Execute or GsuMicroKind.Finish=>o.A==0&&o.B==0&&o.C==0,
                _=>false};
            if(!good)throw new InvalidDataException("Invalid GSU opcode/micro-operation relationship.");
        }
        bool timed=ops[s.OperationIndex].Kind is GsuMicroKind.Delay or GsuMicroKind.ReadRam or GsuMicroKind.ReadGraphic or GsuMicroKind.WriteGraphic;
        if(!timed&&s.OperationDue!=-1)throw new InvalidDataException("Deadline on an instantaneous GSU operation.");
    }
    public void Restore(GsuState s)
    {
        if(s.Version!=1||s.Tick<0||s.Instructions<0||s.Registers is not{Length:16}||s.Source is <0 or >15||s.Destination is <0 or >15||s.ProgramBank>127||s.RomBank>127||s.RamBank>1||(s.CacheBase&15)!=0||s.ScreenMode>63||s.ClockSelect>1||s.Bram>1||s.PlotOptions>31||s.Cache is not{Length:512}||s.Valid is not{Length:32}||s.Pixels is not{Length:2}||s.Pixels.Any(p=>p==null||p.Colors is not{Length:8}||p.Offset is <-1 or >8191)||s.GraphicByte is <0 or >255||s.Phase is <0 or >4||s.Opcode is <0 or >255||s.OperandCount is <0 or >2||s.OperandIndex<0||s.OperandIndex>s.OperandCount||s.Operands is not{Length:2}||s.Operations==null||s.Operations.Length>100||s.OperationIndex<0||s.OperationIndex>s.Operations.Length||s.FetchOffset is <-1 or >511||s.FillIndex is <0 or >16||s.FetchAddress is <0 or >65535||s.RamAddress is <0 or >65535||s.RomAddress is <0 or >0x7fffff||s.RamWriteAddress is <0 or >0x1ffff||s.RamSecondAddress is <0 or >0x1ffff||s.RamSecondPending&&s.RamDue<0||new[]{s.OperationDue,s.FetchDue,s.RomDue,s.RamDue}.Any(t=>t< -1)||s.Operations.Any(op=>op==null||!Enum.IsDefined(op.Kind)||op.A<0||op.A>0x1fffff||op.B<0||op.B>255||op.C<0||op.C>255))throw new InvalidDataException("Invalid GSU continuation.");
        if(s.Phase==4&&s.OperationIndex>=s.Operations.Length||s.Phase is 1 or 3&&s.FetchDue<0||s.RomDue>=0!=( (s.Status&0x40)!=0))throw new InvalidDataException("Inconsistent GSU pending operation.");
        ValidateContinuation(s);
        Tick=s.Tick;Instructions=s.Instructions;s.Registers.CopyTo(Registers,0);Status=s.Status;source=s.Source;destination=s.Destination;programBank=s.ProgramBank;romBank=s.RomBank;ramBank=s.RamBank;cacheBase=s.CacheBase;screenBase=s.ScreenBase;screenMode=s.ScreenMode;config=s.Config;clockSelect=s.ClockSelect;bram=s.Bram;color=s.Color;plotOptions=s.PlotOptions;pipeline=s.Pipeline;s.Cache.CopyTo(cache,0);s.Valid.CopyTo(valid,0);pixels=s.Pixels.Select(p=>p with{Colors=p.Colors.ToArray()}).ToArray();phase=s.Phase;opcode=s.Opcode;operandCount=s.OperandCount;operandIndex=s.OperandIndex;s.Operands.CopyTo(operands,0);pcWritten=s.PcWritten;ramAddress=s.RamAddress;memoryValue=s.MemoryValue;operations.Clear();operations.AddRange(s.Operations);operationIndex=s.OperationIndex;operationDue=s.OperationDue;fetchAddress=s.FetchAddress;fetchOffset=s.FetchOffset;fillIndex=s.FillIndex;fetchDue=s.FetchDue;graphicByte=s.GraphicByte;romDue=s.RomDue;romData=s.RomData;romAddress=s.RomAddress;ramDue=s.RamDue;ramWriteAddress=s.RamWriteAddress;ramData=s.RamData;ramSecondPending=s.RamSecondPending;ramSecondAddress=s.RamSecondAddress;ramSecondData=s.RamSecondData;IrqChanged?.Invoke(IrqOutput);
    }
}
