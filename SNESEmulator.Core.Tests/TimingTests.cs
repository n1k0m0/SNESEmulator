using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;
using SNESEmulator.Testing;

static class TimingTests
{
    static void Check(bool yes,string message="Timing assertion failed") {if(!yes)throw new Exception(message);}
    static string Json<T>(T value)=>JsonSerializer.Serialize(value);
    static SnesSystem Machine() {var m=new SnesSystem();m.Insert(new Cartridge(DiagnosticRom.Create()));return m;}
    public static void Run(Action<string,Action> test,string directory)
    {
        test("M2 presentation pacing uses actual rational field duration",()=>
        {
            foreach(var p in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile})foreach(bool interlace in new[]{false,true})
            {
                var c=new MasterClock(p);c.Reset();c.Interlace=interlace;long first=p.FieldMasterClocks(false,interlace);Check(c.ClocksUntilFieldEnd==first);c.AdvanceTo(first);long second=p.FieldMasterClocks(true,interlace);Check(c.ClocksUntilFieldEnd==second);
                c.AdvanceTo(first+101);Check(c.ClocksUntilFieldEnd==second-101);
                Check(Math.Abs(p.ClockSeconds(first)-(double)first*p.MasterHzDenominator/p.MasterHzNumerator)<1e-15);Check(Math.Abs(p.ClockSeconds(first)-(p.Pal?1d/50:1d/60))>1e-9,"pacing must not round to 50/60");
            }
        });
        test("M2 mapped access speeds are separate from 65816 cycle counts",()=>
        {
            var p=HardwareProfile.Ntsc;
            foreach(var (a,slow,fast) in new[]{(0x000000,8,8),(0x7e4000,8,8),(0x002100,6,6),(0x004016,12,12),(0x004100,12,12),(0x004200,6,6),(0x006000,8,8),(0x008000,8,8),(0x808000,8,6),(0xc00000,8,6),(0x802100,6,6)})
            {var c=new CpuCycle(a,null,"d--remx-");Check(p.AccessClocks(c,false)==slow,$"{a:X6} slow");Check(p.AccessClocks(c,true)==fast,$"{a:X6} fast");}
            Check(p.AccessClocks(new(0x4016,null,"---remx-"),false)==6,"internal cycles ignore address speed");
        });
        test("M2 NTSC PAL fields short long lines dots and refresh phase",()=>
        {
            var custom=new MasterClock(HardwareProfile.Ntsc);custom.Reset();custom.VblankLine=240;var restored=new MasterClock(HardwareProfile.Ntsc);restored.Restore(custom.Capture());Check(restored.VblankLine==240,"overscan scheduling latch is serialized");
            foreach(var p in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile})foreach(bool interlace in new[]{false,true})
            {
                var c=new MasterClock(p);c.Reset();c.Interlace=interlace;var refresh=new List<long>();c.EventObserved=e=>{if(e.Kind==HardwareEventKind.Refresh)refresh.Add(e.Clock);};
                long first=1364L*(p.Lines+(interlace?1:0));c.AdvanceTo(first-1);Check(c.Fields==0);c.AdvanceTo(first);Check(c.Fields==1&&c.Field&&c.V==0&&c.H==0);
                long second=1364L*p.Lines+(!p.Pal&&!interlace?-4:p.Pal&&interlace?4:0);c.AdvanceTo(first+second);Check(c.Fields==2&&!c.Field&&c.H==0&&c.V==0);
                Check(refresh[0]==538&&refresh[1]==1898,"reset divider phase");Check(refresh.Zip(refresh.Skip(1)).All(x=>(x.Second-x.First)%8==0),"refresh divider remains eight-clock aligned");
            }
            var d=new MasterClock(HardwareProfile.Ntsc);d.Reset();d.AdvanceTo(1292);Check(d.Dot==323);d.AdvanceTo(1297);Check(d.Dot==323);d.AdvanceTo(1298);Check(d.Dot==324);d.AdvanceTo(1310);Check(d.Dot==327);d.AdvanceTo(1315);Check(d.Dot==327);d.AdvanceTo(1316);Check(d.Dot==328);
        });
        test("M2 independent rational APU oscillator one hour per profile has zero integer drift",()=>
        {
            foreach(var p in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile})
            {
                var c=new MasterClock(p);c.Reset();long duration=checked(p.MasterHzNumerator*3600/p.MasterHzDenominator);c.AdvanceTo(duration);
                Int128 expected=(Int128)duration*p.ApuHzNumerator*p.MasterHzDenominator;long denominator=p.MasterHzNumerator*p.ApuHzDenominator;
                Check(c.Now==duration&&c.ApuTicks==(long)(expected/denominator)&&c.ApuRemainder==(long)(expected%denominator),p.Id);
                long fields=0,left=duration;bool field=false;
                while(true){long length=1364L*p.Lines+(!p.Pal&&field?-4:0);if(left<length)break;left-=length;fields++;field=!field;}
                int line=0;while(true){int length=!p.Pal&&field&&line==240?1360:1364;if(left<length)break;left-=length;line++;}
                Check(c.Fields==fields&&c.Field==field&&c.V==line&&c.H==left,"analytical field count");
            }
        });
        test("M2 stable event ordering before CPU access at exact boundary",()=>
        {
            var m=Machine();var order=new List<string>();m.Clock.Schedule(8,HardwareEventKind.External,1,4);m.Clock.Schedule(8,HardwareEventKind.External,2,4);m.Clock.Schedule(8,HardwareEventKind.External,3,-1);
            m.Clock.EventObserved=e=>{if(e.Kind==HardwareEventKind.External)order.Add("e"+e.Payload);};m.Cpu.CycleObserved=c=>order.Add("bus");
            m.RunUntil(7);Check(order.Count==0);m.RunUntil(8);Check(order.SequenceEqual(new[]{"e3","e1","e2","bus"}),string.Join(',',order));
        });
        test("M2 refresh stalls CPU effects at endpoint and within a cycle",()=>
        {
            foreach(int edge in new[]{7,8})
            {
                var m=Machine();var access=new List<long>();m.Cpu.CycleObserved=c=>access.Add(m.Clock.Now);m.Clock.Schedule(edge,HardwareEventKind.Refresh);
                m.RunUntil(47);Check(access.Count==0,"refresh forbids CPU latch during pause");m.RunUntil(48);Check(access.SequenceEqual(new long[]{48}),"CPU active clocks resume after 40-clock stall");
                Check(m.Clock.ApuTicks>0,"independent oscillator continues while CPU stalled");
            }
        });
        test("M2 exact clock chunking single clocks blocks and frames same state",()=>
        {
            var block=Machine();var tiny=Machine();var frame=Machine();frame.RunFrame();long end=frame.Clock.Now;
            block.RunUntil(end);for(long t=1;t<=end;t++)tiny.RunUntil(t);
            string Save(SnesSystem m,string name){string path=Path.Combine(directory,name);StateSerializer.Save(m,path);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));}
            Check(Save(block,"m2-block")==Save(tiny,"m2-tiny"),"one-clock and block complete state");Check(Save(frame,"m2-frame")==Save(block,"m2-block2"),"frame and block complete state");
        });
        test("M2 savestate at every master clock around instruction refresh and field boundaries",()=>
        {
            var checkpoints=Enumerable.Range(1,120).Select(i=>(long)i).Concat(Enumerable.Range(530,60).Select(i=>(long)i)).Concat(Enumerable.Range(357360,12).Select(i=>(long)i));
            foreach(long t in checkpoints)
            {
                var m=Machine();m.RunUntil(t);string path=Path.Combine(directory,"m2-partial");StateSerializer.Save(m,path);var restored=StateSerializer.Load(m.Bus.Cartridge!,path);
                var a=new List<CpuCycle>();var b=new List<CpuCycle>();m.Cpu.CycleObserved=a.Add;restored.Cpu.CycleObserved=b.Add;
                m.RunUntil(t+91);restored.RunUntil(t+91);StateSerializer.Save(m,path+"a");StateSerializer.Save(restored,path+"b");
                Check(File.ReadAllBytes(path+"a").SequenceEqual(File.ReadAllBytes(path+"b"))&&a.SequenceEqual(b),$"resume at clock {t}");
            }
        });
        test("M2 restartable cycles preserve side effects and every-cycle continuation",()=>
        {
            foreach(byte[] program in new byte[][]{[0xad,0x00,0x40],[0x8d,0x00,0x40],[0xee,0x00,0x40],[0x22,0x00,0x90,0x00],[0x28],[0x40],[0xfc,0,0x90]})
            {
                var reference=new SideEffects(program);var r=new Cpu65816(reference){PC=0x8000,S=0x1f0,P=0x34,A=0x42};var trace=new List<CpuCycle>();r.CycleObserved=trace.Add;r.Step();
                for(int stop=1;stop<=trace.Count;stop++)
                {
                    var bus=new SideEffects(program);var c=new Cpu65816(bus){PC=0x8000,S=0x1f0,P=0x34,A=0x42};var actual=new List<CpuCycle>();c.CycleObserved=actual.Add;
                    for(int i=0;i<stop;i++)Check(c.AdvanceCycle());
                    // Round-trip a data-only continuation, with a distinct CPU instance.
                    var state=JsonSerializer.Deserialize<CpuContinuation>(Json(c.CaptureContinuation()));var interrupts=JsonSerializer.Deserialize<CpuInterruptState>(Json(c.CaptureInterrupts()))!;
                    var resumed=new Cpu65816(bus);resumed.RestoreContinuation(state);resumed.RestoreInterrupts(interrupts);resumed.CycleObserved=actual.Add;while(resumed.AdvanceCycle()){}
                    Check(resumed.Registers==r.Registers&&actual.SequenceEqual(trace)&&bus.Reads==reference.Reads&&bus.Writes==reference.Writes,$"{program[0]:X2} stop {stop}");
                }
            }
        });
        test("M2 interrupt polling CLI SEI delayed mask NMI priority and WAI wake",()=>
        {
            var bus=new SideEffects([0x58,0x78,0xea]);bus.Memory[0xfffe]=0x00;bus.Memory[0xffff]=0x90;
            var c=new Cpu65816(bus){PC=0x8000,S=0x1ff,P=0x34};c.SetInterruptLines(false,true);c.Step();Check(c.PC==0x8001);c.Step();Check(c.PC==0x8002);c.Step();Check(c.PC==0x9000&&c.S==0x1fc,"SEI uses previous mask at last cycle");
            foreach(bool masked in new[]{false,true})
            {
                var b=new SideEffects([0xcb,0xea]);b.Memory[0xfffa]=0;b.Memory[0xfffb]=0x90;var w=new Cpu65816(b){PC=0x8000,S=0x1ff,P=(byte)(masked?0x34:0x30)};w.Step();Check(w.Waiting);w.SetInterruptLines(true,true);w.Step();Check(!w.Waiting&&w.PC==0x9000,"NMI wakes and wins");
            }
            var s=new SideEffects([0xdb]);var stopped=new Cpu65816(s){PC=0x8000,P=0x34};stopped.Step();stopped.SetInterruptLines(true,true);stopped.Step();Check(stopped.Stopped&&stopped.PC==0x8001);stopped.Reset();Check(!stopped.Stopped);
        });
        test("M2 RTI IRQ polling uses restored I independently of delayed width pins",()=>
        {
            foreach(bool e in new[]{false,true})foreach(bool masked in new[]{false,true})
            {
                var bus=new SideEffects([0x40]);var c=new Cpu65816(bus){PC=0x8000,S=0x100,P=0x34,E=e};
                bus.Memory[0x101]=(byte)(masked?0x34:0x30);bus.Memory[0x102]=0x00;bus.Memory[0x103]=0x90;bus.Memory[0x104]=0;
                bus.Memory[0x9000]=0xea;int vector=e?0xfffe:0xffee;bus.Memory[vector]=0;bus.Memory[vector+1]=0xa0;
                c.SetInterruptLines(false,true);c.Step();Check(c.PC==0x9000);c.Step();Check(c.PC==(masked?0x9001:0xa000),$"E={e} I={masked}");
            }
        });
        test("M2 WAI STP suspend CPU clocks while scheduler and APU continue",()=>
        {
            foreach(byte op in new byte[]{0xcb,0xdb})
            {
                var m=Machine();m.Cpu.PC=0;m.Cpu.PBR=0;m.Bus.Wram[0]=op;var cycles=new List<long>();m.Cpu.CycleObserved=c=>cycles.Add(m.Clock.Now);
                m.RunUntil(10000);Check(m.Clock.Now==10000&&m.Clock.ApuTicks>0&&m.Cpu.Instructions==1,"sleep cannot hang budget");Check(cycles.SequenceEqual(new long[]{8,14,20,20}),"quiescent marker has zero duration; sleep produces no CPU bus clocks");
            }
        });
        test("M2 reset IRQ NMI entry continuation survives every cycle",()=>
        {
            foreach(var kind in new[]{CpuEntry.Reset,CpuEntry.Irq,CpuEntry.Nmi})
            {
                Cpu65816 Prepare(SideEffects b){var c=new Cpu65816(b){PC=0x8000,S=0x1ff,P=0x30};if(kind==CpuEntry.Reset)c.RequestReset();else c.RestoreInterrupts(new(false,kind==CpuEntry.Irq,kind==CpuEntry.Nmi,kind==CpuEntry.Nmi,kind==CpuEntry.Irq,CpuEntry.Instruction,null));return c;}
                var baselineBus=new SideEffects([0xea]);var baseline=Prepare(baselineBus);var expected=new List<CpuCycle>();baseline.CycleObserved=expected.Add;while(baseline.AdvanceCycle()){}
                for(int stop=1;stop<=expected.Count;stop++)
                {
                    var b=new SideEffects([0xea]);var c=Prepare(b);var actual=new List<CpuCycle>();c.CycleObserved=actual.Add;for(int n=0;n<stop;n++)Check(c.AdvanceCycle());
                    var copy=new Cpu65816(b);copy.RestoreContinuation(JsonSerializer.Deserialize<CpuContinuation>(Json(c.CaptureContinuation())));copy.RestoreInterrupts(JsonSerializer.Deserialize<CpuInterruptState>(Json(c.CaptureInterrupts()))!);copy.CycleObserved=actual.Add;while(copy.AdvanceCycle()){}
                    Check(copy.Registers==baseline.Registers&&actual.SequenceEqual(expected),$"{kind} cycle {stop}");
                }
            }
        });
        test("M2 timed cycle comparator rejects cycle address value direction and pin mutations",()=>
        {
            using var expected=JsonDocument.Parse("[[32768,234,\"dp-remx-\"],[32769,null,\"---remx-\"]]");
            CpuCycle[] valid=[new(32768,234,"dp-remx-"),new(32769,null,"---remx-")];Check(CpuVector.FirstCycleDifference(valid,expected.RootElement)==null);
            foreach(var bad in new CpuCycle[][]{[valid[0]],[valid[0],valid[1],valid[1]],[valid[0] with{Address=32770},valid[1]],[valid[0] with{Value=233},valid[1]],[valid[0] with{Pins="dp-wemx-"},valid[1]],[valid[0] with{Pins="-p-remx-"},valid[1]]})Check(CpuVector.FirstCycleDifference(bad,expected.RootElement)!=null);
        });
        test("M2 every opcode incremental execution matches external bus vector",()=>
        {
            var bus=new TraceBus{TraceEnabled=false};var cpu=new Cpu65816(bus);var actual=new List<CpuCycle>();cpu.CycleObserved=actual.Add;
            foreach(string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"../../../../testdata/65816"),"*.json"))
            {
                using var doc=JsonDocument.Parse(File.ReadAllBytes(file));var v=doc.RootElement[0];bus.ClearMemory();cpu.RestoreContinuation(null);cpu.RestoreInterrupts(new(false,false,false,false,false,CpuEntry.Instruction,null));CpuVector.Load(cpu,bus,v.GetProperty("initial"));if(cpu.E)cpu.S=(ushort)(0x100|(cpu.S&255));
                actual.Clear();int start=cpu.PBR<<16|cpu.PC,op=bus.Memory[start];
                do {while(cpu.AdvanceCycle()){} }while(op is 0x44 or 0x54 && (cpu.PBR<<16|cpu.PC)==start);
                Check(CpuVector.FirstDifference(cpu,bus,v.GetProperty("final"))==null,file+" semantics");Check(CpuVector.FirstCycleDifference(actual,v.GetProperty("cycles"))==null,file+" cycles");
            }
        });
    }
    sealed class SideEffects : ICpuBus
    {
        public readonly byte[] Memory=new byte[1<<24];public int Reads,Writes;
        public SideEffects(byte[] program){program.CopyTo(Memory,0x8000);Memory[0x4000]=0x20;}
        public byte Read(int address){address&=0xffffff;if(address==0x4000){Reads++;return Memory[address]++;}return Memory[address];}
        public void Write(int address,byte value){address&=0xffffff;if(address==0x4000)Writes++;Memory[address]=value;}
    }
}
