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
// Preserves admitted CPU cycles and exactly-once bus effects across suspension.
// Replay reconstructs decoder locals without issuing the same hardware access twice.
//

namespace SNESEmulator.Core;

public sealed partial class Cpu65816
{
    public Func<bool>? InterruptPollingAllowed {get;set;}
    public Action<CpuCycle>? BeforeCycle { get; set; }
    /// <summary>Initialize scheduler phase state before exposing an actual bus effect.</summary>
    public Action<CpuCycle>? EffectCompleted { get; set; }
    public Action<CpuCycle>? AfterCycle { get; set; }
    /// <summary>Resume scheduler-owned trailing time before consuming a latched read.</summary>
    public Action? ResumeCycle { get; set; }
    public Action<CpuCycle>? CycleObserved { get; set; }
    private readonly List<CpuCycle> completed = [];
    private readonly List<InterruptSample> samples = [];
    private CpuRegisters? initial;
    private int cursor, lastAddress, count;
    private byte lastRead;
    private byte opcode;
    private bool locked, vector, incremental;
    private bool nmiLine,irqLine,nmiLatched,pollNmi,pollIrq,sampleNmi,sampleIrq;
    private CpuEntry entry;
    public bool HasWakeSignal=>nmiLatched||irqLine||pollNmi||pollIrq;
    private InterruptSample? pendingSample;
    public void SetInterruptLines(bool nmi,bool irq)
    {if(nmi&&!nmiLine)nmiLatched=true;nmiLine=nmi;irqLine=irq;}
    public void PulseNmi()=>nmiLatched=true;
    public CpuInterruptState CaptureInterrupts()=>new(nmiLine,irqLine,nmiLatched,pollNmi,pollIrq,entry,pendingSample);
    public void RestoreInterrupts(CpuInterruptState s)
    {nmiLine=s.NmiLine;irqLine=s.IrqLine;nmiLatched=s.NmiLatched;pollNmi=s.PollNmi;pollIrq=s.PollIrq;entry=s.Entry;pendingSample=s.PendingSample;}
    private sealed class CycleBoundary : Exception;
    private readonly CycleBoundary boundary = new();
    public bool AtInstructionBoundary => initial == null;
    private CpuRegisters? registerCache;
    private CpuCycle? idleCache;
    private static readonly InterruptSample[] interruptSamples=[new(false,false),new(true,false),new(false,true),new(true,true)];
    private static InterruptSample Sample(bool nmi,bool irq)=>interruptSamples[(nmi?1:0)+(irq?2:0)];
    public CpuRegisters Registers {
        get {
            var r=registerCache;
            if(r!=null&&r.PC==PC&&r.Instructions==Instructions&&r.A==A&&r.X==X&&r.Y==Y&&r.S==S&&r.D==D&&r.P==P&&r.PBR==PBR&&r.DBR==DBR&&r.E==E&&r.Waiting==Waiting&&r.Stopped==Stopped)return r;
            return registerCache=new(A,X,Y,S,D,PC,P,PBR,DBR,E,Waiting,Stopped,Instructions);
        }
    }
    private void Restore(CpuRegisters r)
    { A=r.A;X=r.X;Y=r.Y;S=r.S;D=r.D;PC=r.PC;P=r.P;PBR=r.PBR;DBR=r.DBR;E=r.E;Waiting=r.Waiting;Stopped=r.Stopped;Instructions=r.Instructions; }
    public CpuContinuation? CaptureContinuation() => initial == null ? null : new(initial,completed.ToArray(),entry,samples.ToArray());
    public void RestoreContinuation(CpuContinuation? state)
    {
      // Format 9 predates the (direct,X) high-byte correction. A journal that
      // already latched the obsolete next-page byte cannot be migrated: its
      // read value (and possibly a following store) is an irreversible effect.
      // Reject while loading the temporary machine, before replacing a worker.
      if(state is {Entry:CpuEntry.Instruction,Completed.Length:>=6} &&
         state.Initial.E && (state.Initial.D&255)!=0 &&
         state.Completed[0].Value is {} op && (op&31)==1 &&
         state.Completed[1].Value is {} operand){
          int pointer=(state.Initial.D+operand+state.Initial.X)&65535;
          if((pointer&255)==255 && state.Completed[5].Address!=(pointer&0xff00))
              throw new InvalidDataException("Savestate contains an obsolete emulation (direct,X) pointer read; restart the ROM or load an earlier state.");
      }
      initial=state?.Initial; completed.Clear();samples.Clear();cursor=count=lastAddress=0;lastRead=opcode=0;locked=vector=false;
      if(state!=null) {if(state.Completed.Length!=state.Samples.Length)throw new InvalidDataException("Invalid CPU continuation");entry=state.Entry;completed.AddRange(state.Completed);samples.AddRange(state.Samples);Restore(state.Initial);} }
    /// <summary>Performs at most one external cycle. Returns false at instruction commit.
    /// Architectural registers commit at instruction completion; latched cycles contain in-flight pins.</summary>
    public bool AdvanceCycle()
    {
        if(initial==null){SelectEntry();initial=Registers;}
        Restore(initial); cursor=0;count=0;lastAddress=0;lastRead=opcode=0;locked=vector=false;incremental=true;
        try { ExecuteEntry();CommitPoll();initial=null;completed.Clear();samples.Clear();return false; }
        catch(CycleBoundary) {Restore(initial!);return true;}
        catch {Restore(initial!);throw;}
        finally {incremental=false;}
    }
    public int Step()
    {
        if(initial!=null) {int n=0;while(AdvanceCycle())n++;return n;}
        SelectEntry();count=cursor=0;lastAddress=0;lastRead=opcode=0;locked=vector=false;ExecuteEntry();CommitPoll();return count;
    }
    /// <summary>Runs until instruction commit or a scheduler budget exception. Completed
    /// effects remain serializable if interrupted. Avoids exception dispatch per cycle.</summary>
    public void RunInstruction()
    {
        if(initial==null){SelectEntry();initial=Registers;}
        Restore(initial);cursor=count=lastAddress=0;lastRead=opcode=0;locked=vector=false;
        try {ExecuteEntry();CommitPoll();initial=null;completed.Clear();samples.Clear();}
        catch {Restore(initial!);throw;}
    }
    private void SelectEntry()
    {
        if(entry!=CpuEntry.Instruction)return;
        if(Stopped)return;
        bool waking=Waiting;
        if(Waiting&&(nmiLatched||irqLine))Waiting=false;
        if(waking&&(nmiLatched||irqLine))
        {entry=nmiLatched?CpuEntry.WakeNmi:(P&I)==0?CpuEntry.WakeIrq:CpuEntry.WakeOnly;if(nmiLatched)nmiLatched=false;}
        else if(pollNmi){entry=CpuEntry.Nmi;nmiLatched=false;}
        else if(pollIrq)entry=CpuEntry.Irq;
    }
    private void CommitPoll(){pollNmi=sampleNmi;pollIrq=sampleIrq;entry=CpuEntry.Instruction;}
    private void ExecuteEntry()
    {
        if(entry==CpuEntry.Instruction){Execute();return;}
        if(entry==CpuEntry.Reset)
        {
            Idle(PBR<<16|PC);Idle();Idle(S);Idle(S);Idle(S);vector=true;PC=(ushort)Read16(0xfffc);vector=false;return;
        }
        if(entry is CpuEntry.WakeNmi or CpuEntry.WakeIrq or CpuEntry.WakeOnly)
        {Idle(PBR<<16|PC);Idle();if(entry==CpuEntry.WakeOnly)return;}
        CycleRead(PBR<<16|PC,"dp-");Idle(PBR<<16|PC);Interrupt(entry is CpuEntry.Nmi or CpuEntry.WakeNmi?(E?0xfffa:0xffea):(E?0xfffe:0xffee),false);
    }
    private static readonly string[] pinNames=CreatePinNames();
    private static string[] CreatePinNames(){var names=new string[160];string[] selects=["d--","dp-","-p-","d-v","---"];for(int s=0;s<5;s++)for(int f=0;f<32;f++)names[s*32+f]=selects[s]+((f&1)!=0?"r":"w")+((f&2)!=0?"e":"-")+((f&4)!=0?"m":"-")+((f&8)!=0?"x":"-")+((f&16)!=0?"l":"-");return names;}
    private string Pins(string select,bool read){int s=select switch{"d--"=>0,"dp-"=>1,"-p-"=>2,"d-v"=>3,"---"=>4,_=>throw new InvalidOperationException("Unknown CPU pin selection")};return pinNames[s*32+(read?1:0)+(E?2:0)+((P&Memory8)!=0?4:0)+((P&Index8)!=0?8:0)+(locked?16:0)];}
    private byte CycleRead(int address,string select="d--") => Cycle(address,null,Pins(vector?"d-v":select,true));
    private byte Read(int address) {lastRead=CycleRead(address);return lastRead;}
    private byte ReadLatchedValue(int address) => lastRead;
    private static bool IsRmw(byte op) => (op&15) is 6 or 14 && (op>>5) is 0 or 1 or 2 or 3 or 6 or 7 || op is 0x04 or 0x0c or 0x14 or 0x1c;
    private int StackAddress() {int a=(S+Fetch())&65535;Idle();return a;}
    private int StackIndirect() {int a=Read16(StackAddress());Idle();return ((DBR<<16|a)+Y)&0xffffff;}
    private int Indexed(int address,ushort index)
    {if(!X8 || (address&0xff00)!=((address+index)&0xff00) || (opcode>>5)==4 || IsRmw(opcode) || opcode is 0x9e)Idle((address&0xffff00)|((address+index)&255));return (address+index)&0xffffff;}
    private void Write(int address,byte value) => Cycle(address,value,Pins("d--",false));
    private void Idle(int? address=null) => Cycle(address??lastAddress,null,Pins("---",true));
    private void InvalidWrite(int address, byte value) => Cycle(address,value,Pins("---",false));
    private byte Cycle(int? address,byte? value,string pins)
    {
        count++;if(address.HasValue)address &= 0xffffff;
        // Re-execute decoder decisions from the entry registers, but consume the
        // journal rather than touching MMIO again. ResumeCycle first completes
        // any scheduler-owned read hold; the latched byte and IRQ/NMI sample
        // belong to the original access even if the live pins have since changed.
        if(initial!=null && cursor<completed.Count) {ResumeCycle?.Invoke();var prior=completed[cursor];var sampled=samples[cursor++];sampleNmi=sampled.Nmi;sampleIrq=sampled.Irq;lastAddress=address??0;return prior.Value??0;}
        // Invalid idle pins often repeat unchanged throughout WAI. These immutable
        // observations and register caches contain only derived data; saves
        // retain exactly the original architectural state and journal bytes.
        bool idle=pins.StartsWith("---",StringComparison.Ordinal);
        var pending=idle&&idleCache is {} cached&&cached.Address==address&&cached.Value==value&&cached.Pins==pins?cached:new CpuCycle(address,value,pins);
        if(idle)idleCache=pending;
        // Admission may yield before the bus effect. Keep the first interrupt
        // sample across that retry so host budget boundaries cannot resample it.
        pendingSample??=Sample(nmiLatched&&(InterruptPollingAllowed?.Invoke()??true),irqLine&&(P&I)==0&&(InterruptPollingAllowed?.Invoke()??true));
        sampleNmi=pendingSample.Nmi;sampleIrq=pendingSample.Irq;
        BeforeCycle?.Invoke(pending);
        byte? data=value;
        if(pending.Valid && address.HasValue) {if(pending.Read)data=bus.Read(address.Value);else bus.Write(address.Value,value!.Value);}
        var actual=data==value?pending:pending with {Value=data};lastAddress=address??0;
        pendingSample=null;
        if(initial!=null) {completed.Add(actual);samples.Add(Sample(sampleNmi,sampleIrq));cursor++;}
        EffectCompleted?.Invoke(actual);CycleObserved?.Invoke(actual);AfterCycle?.Invoke(actual);
        if(incremental)throw boundary;
        return data??0;
    }
    private static bool ImpliedIdle(byte op) => op is 0x08 or 0x0b or 0x18 or 0x1b or 0x28 or 0x2b or 0x38 or 0x3b or 0x40 or 0x48 or 0x4b or 0x58 or 0x5a or 0x5b or 0x60 or 0x68 or 0x6b or 0x78 or 0x7a or 0x7b or 0x88 or 0x8a or 0x8b or 0x98 or 0x9a or 0x9b or 0xa8 or 0xaa or 0xab or 0xb8 or 0xba or 0xbb or 0xc8 or 0xca or 0xcb or 0xd8 or 0xda or 0xdb or 0xe8 or 0xea or 0xeb or 0xf8 or 0xfa or 0xfb or 0x0a or 0x2a or 0x4a or 0x6a or 0x1a or 0x3a;
}
