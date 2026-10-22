using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;

static class IoDmaTests
{
    static void Check(bool yes,string reason="M3 assertion failed"){if(!yes)throw new Exception(reason);}
    static SnesSystem Machine(bool stopped=true){var m=new SnesSystem();m.Insert(new Cartridge(DiagnosticRom.Create()));m.Cpu.Stopped=stopped;return m;}
    static void Channel(SnesSystem m,int ch,int control,int b,int a,int count,int indirectBank=0)
    {
        int d=0x4300+ch*16;
        foreach(var (r,v) in new[]{(0,control),(1,b),(2,a&255),(3,(a>>8)&255),(4,a>>16),(5,count&255),(6,count>>8),(7,indirectBank),(8,0),(9,0),(10,0)})m.Bus.Write(d+r,(byte)v);
    }
    static string Snapshot(SnesSystem m,string directory,string name)
    {
        string path=Path.Combine(directory,name);StateSerializer.Save(m,path);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    }
    // Explicit mode specification, deliberately independent of DmaController.Patterns.
    static readonly int[][] Targets=[[0],[0,1],[0,0],[0,0,1,1],[0,1,2,3],[0,1,0,1],[0,0],[0,0,1,1]];
    static List<DmaTrace> Trace(SnesSystem m){var rows=new List<DmaTrace>();m.Bus.Dma.Observed=rows.Add;return rows;}
    static string? Protocol(IReadOnlyList<DmaTrace> rows,int mode,bool reverse,int count,int source,int target,int firstValue)
    {
        if(rows.Count!=count*2)return "pair count";
        for(int i=0;i<count;i++) {
            int b=target+Targets[mode][i%Targets[mode].Length];
            var r=rows[i*2];var w=rows[i*2+1];
            if(r.Clock!=28+i*8||w.Clock!=32+i*8)return $"byte{i} clock";
            if(r.Channel!=0||w.Channel!=0)return $"byte{i} channel";
            if(r.Address!=(reverse?b:source+i)||w.Address!=(reverse?source+i:b))return $"byte{i} address";
            if(r.Value!=(reverse?0xa0+b-target:firstValue+i)||w.Value!=r.Value)return $"byte{i} value";
        }
        return null;
    }
    public static void Run(Action<string,Action> test,string directory)
    {
        test("M3 recursive ROM mirror and mapper metadata manual overrides",()=>{
            Check(Cartridge.Mirror(0x300000,0x300000)==0x200000,"3 MiB absent address line mirrors last chip");
            Check(Cartridge.Mirror(0x3fffff,0x300000)==0x2fffff);
            byte[] oddImage=new byte[0x300000];DiagnosticRom.Create().CopyTo(oddImage,0);oddImage[0]=0x33;oddImage[0x200000]=0x5a;var odd=new Cartridge(oddImage);var oddBus=new SystemBus{Cartridge=odd};Check(oddBus.Read(0x608000)==0x5a&&oddBus.Read(0x408000)==0x5a&&oddBus.Read(0x008000)==0x33,"actual 3 MiB CPU ROM windows must mirror chip tail, not modulo");
            foreach(int size in new[]{0x8000,0x18000,0x280000,0x300000,0x600000})for(int a=0;a<0x800000;a+=7919)Check(Cartridge.Mirror(a,size)<size);
            byte[] rom=new byte[0x600000];DiagnosticRom.Create().AsSpan(0x7fc0,64).CopyTo(rom.AsSpan(0x40ffc0));rom[0x40ffd5]=0x35;rom[0x40ffd9]=2;
            var ex=new Cartridge(rom);Check(ex.Mapping==CartridgeMapping.ExHiRom&&ex.Metadata.HeaderOffset==0x40ffc0&&ex.Pal);
            Check(ex.RomOffset(0x008000)==0x408000&&ex.RomOffset(0xc08000)==0x8000);
            var lo=new Cartridge(rom,"",new(CartridgeMapping.ExLoRom,false));Check(!lo.Pal&&lo.Metadata.ManualMapping&&lo.Metadata.ManualRegion);
            Check(lo.RomOffset(0x008000)==0x400000&&lo.RomOffset(0x808000)==0);
            var forced=new SnesSystem(HardwareProfile.Ntsc);forced.Insert(ex);Check(!forced.Profile.Pal,"explicit console profile must survive PAL header");
            string path=Path.Combine(directory,"m3-forced");StateSerializer.Save(forced,path);var copy=StateSerializer.Load(ex,path);Check(!copy.Profile.Pal&&copy.Bus.Cartridge!.Mapping==CartridgeMapping.ExHiRom);
            var auto=new SnesSystem();auto.Insert(ex);Check(auto.Profile.Pal);StateSerializer.Save(auto,path);var automaticCopy=StateSerializer.Load(ex,path);Check(!automaticCopy.ProfileForced);
            try{StateSerializer.Load(lo,path);throw new Exception("same hash wrong mapper accepted");}catch(InvalidDataException){}
        });
        test("M3 SRAM bank mirrors warm reset and deterministic power seed",()=>{
            byte[] rom=DiagnosticRom.Create();rom[0x7fd8]=1;var m=Machine();m.Insert(new Cartridge(rom));m.Bus.Write(0x700800,0x5a);Check(m.Bus.Read(0xf00000)==0x5a,"2 KiB SRAM chip mask");
            m.Bus.Wram[77]=0x42;m.Bus.Controllers.Buttons[0]=0x8000;m.Reset();Check(m.Bus.Wram[77]==0x42&&m.Bus.Cartridge!.Sram[0]==0x5a&&m.Bus.Controllers.Buttons[0]==0x8000);
            m.PowerOn();Check(m.Bus.Wram[77]==0&&m.Bus.Cartridge!.Sram[0]==0x5a);Check(m.Bus.Io[1]==255&&m.Bus.DmaRegisters.All(v=>v==255));
        });
        test("M3 CPU MDR internal IO exemptions register masks and side effects",()=>{
            var managed=Machine(false);managed.Cpu.PC=0;new byte[]{0xad,0,0x10}.CopyTo(managed.Bus.Wram,0);managed.Bus.Wram[0x1000]=0x5a;managed.RunUntil(28);Check(managed.Bus.OpenBus==0x10,"external read MDR changes after hold, not at latch");managed.RunUntil(32);Check(managed.Bus.OpenBus==0x5a);

            var b=new SystemBus();b.Reset();b.OpenBus=0x6d;b.NmiFlag=true;
            Check(b.Read(0x4210)==0xe2&&b.OpenBus==0x6d&&!b.NmiFlag);
            Check(b.Read(0x4200)==0x6d&&b.Read(0x4380)==0x6d);
            b.Controllers.Buttons[0]=0x8000;b.Write(0x4016,1);b.Write(0x4016,0);b.OpenBus=0xa0;
            Check(b.Read(0x4016)==0xa1&&b.OpenBus==0xa0);Check(b.Read(0x4017)==0xbc&&b.OpenBus==0xa0);
            b.Write(0x430f,0x37);Check(b.Read(0x430b)==0x37);b.OpenBus=0x99;Check(b.Read(0x430c)==0x99);
            b.Wram[0]=0x42;b.WramAddress=0x1ffff;b.Wram[0x1ffff]=0x77;Check(b.Read(0x2180)==0x77&&b.WramAddress==0&&b.OpenBus==0x77);Check(b.Read(0x2180)==0x42&&b.WramAddress==1);
        });
        test("M3 all DMA modes forward reverse protocol and exact read hold edges",()=>{
            for(int mode=0;mode<8;mode++)foreach(bool reverse in new[]{false,true}){
                var m=Machine();for(int i=0;i<12;i++)m.Bus.Wram[0x100+i]=(byte)(0x40+i);
                for(int i=0;i<4;i++)m.Bus.Apu.ApuToCpu[i]=(byte)(0xa0+i);
                Channel(m,0,mode|(reverse?128:0),0x40,0x7e0100,8);var rows=Trace(m);m.Bus.Write(0x420b,1);
                Check(rows.Count==0&&m.Bus.Wram[0x100]==0x40,"request must not copy immediately");
                m.RunUntil(88);Check(Protocol(rows,mode,reverse,8,0x7e0100,0x2140,0x40)==null,Protocol(rows,mode,reverse,8,0x7e0100,0x2140,0x40)??"");
                for(int i=0;i<8;i++){
                    var read=rows[i*2];var write=rows[i*2+1];int target=0x2140+Targets[mode][i%Targets[mode].Length];
                    Check(read.Clock==28+i*8&&write.Clock==32+i*8,"alignment8 + global8 + channel8 + read4 + hold4");
                    Check(read.Address==(reverse?target:0x7e0100+i)&&write.Address==(reverse?0x7e0100+i:target));
                    Check(write.Value==(reverse?0xa0+target-0x2140:0x40+i));
                }
                Check(m.Bus.DmaRegisters[2]==8&&m.Bus.DmaRegisters[3]==1&&m.Bus.DmaRegisters[5]==0);
            }
        });
        test("M3 original CPU ROM all DMA modes directions and critical raster starts",()=>{
            foreach(var position in Enum.GetValues<IoDmaDiagnosticRom.StartPosition>())for(int mode=0;mode<8;mode++)foreach(bool reverse in new[]{false,true}) {
                var m=new SnesSystem();m.Insert(new Cartridge(IoDmaDiagnosticRom.Create(mode,reverse,position)));var rows=Trace(m);
                long requestClock=-1,acknowledgeClock=-1;
                m.Bus.AccessOriginObserved=access=>{if(access.Origin==AccessOrigin.Cpu&&access.Address==0x420b&&access.Direction=="write")requestClock=m.Clock.Now;if(access.Address==0x4211&&access.Direction=="read")acknowledgeClock=m.Clock.Now;};
                m.RunUntil(310000);
                Check(m.Bus.Wram[0x1ff0]==0xa5&&m.Bus.Wram[0x1ff1]==mode&&m.Bus.Wram[0x1ff2]==(reverse?1:0),$"ROM signature {position} mode{mode} reverse{reverse}");
                Check(rows.Count==16&&m.Cpu.Stopped,"exactly eight DMA bus pairs then CPU stops");
                if(mode==0&&!reverse)Console.WriteLine($"M3 ROM {position}: first DMA read {rows[0].Clock}, H={rows[0].Clock%1364}, last write {rows[^1].Clock}");
                var expectedWram=new Dictionary<int,byte>();int pointer=0x4000;
                for(int i=0;i<8;i++) {
                    int target=(reverse?0x2140:0x2180)+Targets[mode][i%Targets[mode].Length];var read=rows[i*2];var write=rows[i*2+1];
                    Check(read.Address==(reverse?target:0x009000+i)&&write.Address==(reverse?0x7e2000+i:target));
                    Check(read.Value==(reverse?0:0x20+i)&&write.Value==read.Value&&write.Clock-read.Clock==4,"actual CPU configured DMA phases/data");
                    if(reverse)Check(m.Bus.Wram[0x2000+i]==0,"idle APU read was actually transferred");
                    else switch(target) {
                        case 0x2180:expectedWram[pointer]=(byte)(0x20+i);pointer=(pointer+1)&0x1ffff;break;
                        case 0x2181:pointer=(pointer&0x1ff00)|(0x20+i);break;
                        case 0x2182:pointer=(pointer&0x100ff)|((0x20+i)<<8);break;
                        case 0x2183:pointer=(pointer&65535)|(((0x20+i)&1)<<16);break;
                    }
                }
                foreach(var pair in expectedWram)Check(m.Bus.Wram[pair.Key]==pair.Value,$"WRAM port oracle {pair.Key:X5}");
                Check(requestClock>=0&&rows[0].Clock>requestClock+8,"one full CPU bus cycle before DMA alignment");
                if(position==IoDmaDiagnosticRom.StartPosition.Refresh){Check(rows[0].Clock%1364 is >=500 and <534&&rows[^1].Clock%1364>574,"actual ROM DMA straddles line1 refresh H534..574");Check(rows.Zip(rows.Skip(1)).Any(p=>p.Second.Clock-p.First.Clock>4),"refresh pauses DMA mid-transfer");}
                if(position==IoDmaDiagnosticRom.StartPosition.Irq)Check(acknowledgeClock>=1364+398&&acknowledgeClock<requestClock,"actual IRQ vector acknowledged before DMA request");
                if(position==IoDmaDiagnosticRom.StartPosition.Vblank)Check(rows[0].Clock>=225L*1364,"VBlank polling actually reached blank");
                if(position==IoDmaDiagnosticRom.StartPosition.Hblank)Check(rows[0].Clock%1364>=1096,"HBlank polling actually reached blank");
            }
        });
        test("M3 DMA fixed decrement bank wrap zero means 65536 and channel priority",()=>{
            foreach(int control in new[]{0,8,16}){
                var m=Machine();Channel(m,0,control,0x40,0x7effff,2);var rows=Trace(m);m.Bus.Write(0x420b,1);m.RunUntil(50);
                Check(rows[2].Address==(control==0?0x7e0000:control==8?0x7effff:0x7efffe));Check(m.Bus.DmaRegisters[4]==0x7e);
            }
            var full=Machine();Channel(full,0,8,0x40,0x7e0000,0);int writes=0;full.Bus.Dma.Observed=r=>{if(r.Phase=="write-B")writes++;};full.Bus.Write(0x420b,1);full.RunUntil(550000);
            Check(writes==65536&&full.Bus.DmaRegisters[5]==0&&full.Bus.DmaRegisters[6]==0,"zero length means entire 16-bit counter range");
            var ordered=Machine();Channel(ordered,1,0,0x40,0x7e0100,1);Channel(ordered,5,0,0x41,0x7e0200,1);var log=Trace(ordered);ordered.Bus.Write(0x420b,0x22);ordered.RunUntil(64);
            Check(log.Select(r=>r.Channel).SequenceEqual(new[]{1,1,5,5})&&log[2].Clock==44,"one global overhead then eight clocks per selected channel");
        });
        test("M3 precise DMA forbidden A ranges and WRAM2180 conflict",()=>{
            foreach(int bank in new[]{0,0x3f,0x80,0xbf,0x40,0x7e,0xc0})foreach(int a in new[]{0x20ff,0x2100,0x21ff,0x2200,0x3fff,0x4000,0x41ff,0x4200,0x421f,0x4220,0x42ff,0x4300,0x437f,0x4380}){
                bool forbidden=(bank&0x40)==0&&(a is >=0x2100 and <=0x21ff or >=0x4000 and <=0x421f or >=0x4300 and <=0x437f);
                Check(SystemBus.ValidDmaA(bank<<16|a)==!forbidden,$"A decode {bank:X2}:{a:X4}");
            }
            foreach(bool reverse in new[]{false,true})foreach(int a in new[]{0x7e0100,0x7f0100,0x000100,0x800100}){
                var m=Machine();m.Bus.WramAddress=0x1234;m.Bus.Wram[0x100]=0x55;m.Bus.Wram[0x1234]=0x77;
                Channel(m,0,reverse?128:0,0x80,a,1);var rows=Trace(m);m.Bus.Write(0x420b,1);m.RunUntil(40);
                Check(m.Bus.WramAddress==0x1234,"conflicting2180 must not increment WRAM port");
                Check(reverse?rows[0].Value==0:!rows[1].Valid,"WRAM conflict invalid B read returns0/write suppressed");
            }
            var invalidWrite=Machine();invalidWrite.Bus.Apu.ApuToCpu[0]=0x99;Channel(invalidWrite,0,128,0x40,0x004016,1);var invalidWriteLog=Trace(invalidWrite);invalidWrite.Bus.Write(0x420b,1);invalidWrite.RunUntil(40);Check(!invalidWriteLog[1].Valid&&invalidWriteLog[1].Value==0x99&&invalidWrite.Bus.Controllers.Position[0]==0,"reverse DMA suppresses forbidden A write");
            var invalid=Machine();Channel(invalid,0,0,0x40,0x004016,1);invalid.Bus.Controllers.Position[0]=0;var invalidLog=Trace(invalid);invalid.Bus.Write(0x420b,1);invalid.RunUntil(40);
            Check(invalid.Bus.Controllers.Position[0]==0&&invalidLog[0].Value==0&&!invalidLog[0].Valid&&invalid.Bus.Apu.CpuToApu[0]==0,"invalid A read must not touch serial MMIO");
        });
        test("M3 DMA clock budgets chunking save every phase and refresh collision",()=>{
            foreach(int start in new[]{0,509,530,1090,1100,1360,225*1364-16}){
                var m=Machine();m.RunUntil(start);Channel(m,0,1,0x40,0x7e0100,8);for(int i=0;i<8;i++)m.Bus.Wram[0x100+i]=(byte)(10+i);m.Bus.Write(0x420b,1);
                string initial=Path.Combine(directory,"m3-dma-initial");StateSerializer.Save(m,initial);var block=StateSerializer.Load(m.Bus.Cartridge!,initial);var a=Trace(m);var b=Trace(block);
                long end=start+180;block.RunUntil(end);string expectedState=Snapshot(block,directory,"m3-block-expected");
                for(long now=start+1;now<=end;now++){
                    m.RunUntil(now);Check(m.Clock.Now==now);
                    string p=Path.Combine(directory,"m3-dma-phase");StateSerializer.Save(m,p);var copy=StateSerializer.Load(m.Bus.Cartridge!,p);
                    m.RunUntil(now);copy.RunUntil(end);
                    Check(Snapshot(copy,directory,"m3-copy")==expectedState,$"phase state {now}");
                }
                block.RunUntil(end);Check(a.SequenceEqual(b)&&Snapshot(m,directory,"m3-tiny")==Snapshot(block,directory,"m3-block"),$"clock chunk {start}");
                if(start==530)Check(a[0].Clock>=578,"refresh postpones transfer latch");
            }
        });
        test("M3 DMA public phase observer saves committed 2180 read and write",()=>{
            var cpu=new SnesSystem();cpu.Insert(new Cartridge(IoDmaDiagnosticRom.Create(1,false,IoDmaDiagnosticRom.StartPosition.Immediate)));string cpuInitial=Path.Combine(directory,"m3-cpu-dma-initial");StateSerializer.Save(cpu,cpuInitial);var continuous=StateSerializer.Load(cpu.Bus.Cartridge!,cpuInitial);continuous.RunUntil(1400);string expected=Snapshot(continuous,directory,"m3-cpu-dma-final");
            for(int t=1000;t<=1180;t++){cpu.RunUntil(t);string p=Path.Combine(directory,"m3-cpu-dma-phase");StateSerializer.Save(cpu,p);var copy=StateSerializer.Load(cpu.Bus.Cartridge!,p);copy.RunUntil(1400);Check(Snapshot(copy,directory,"m3-cpu-dma-copy")==expected,$"CPU continuation DMA checkpoint{t}");}

            foreach(bool reverse in new[]{false,true}){
                var m=Machine();Channel(m,0,reverse?128:0,0x80,0x408000,2);m.Bus.WramAddress=0x1234;m.Bus.Wram[0x1234]=0x44;
                var paths=new List<string>();m.Bus.Dma.Observed=r=>{string p=Path.Combine(directory,"m3-observer"+paths.Count);StateSerializer.Save(m,p);paths.Add(p);};m.Bus.Write(0x420b,1);m.RunUntil(80);
                foreach(string p in paths){var copy=StateSerializer.Load(m.Bus.Cartridge!,p);copy.RunUntil(80);Check(copy.Bus.WramAddress==m.Bus.WramAddress&&Snapshot(copy,directory,"m3-observer-copy")==Snapshot(m,directory,"m3-observer-ref"),"observer resume must not repeat effects");}
                Check(m.Bus.WramAddress==0x1236);
            }
        });
        test("M3 HDMA all modes direct indirect repeat and two pass ordering",()=>{
            foreach(bool indirect in new[]{false,true})foreach(bool reverse in new[]{false,true})for(int mode=0;mode<8;mode++){
                var m=Machine();Channel(m,0,mode|(indirect?64:0)|(reverse?128:0),0x40,0x7e1000,0,0x7e);int len=Targets[mode].Length;
                var table=new List<byte>{0x82};if(indirect){table.Add(0);table.Add(0x20);}else for(int i=0;i<len*2;i++)table.Add((byte)(0x30+i));table.Add(0);table.ToArray().CopyTo(m.Bus.Wram,0x1000);
                for(int i=0;i<len*2;i++)m.Bus.Wram[0x2000+i]=(byte)(0x30+i);
                var rows=Trace(m);m.Bus.Write(0x420c,1);m.RunUntil(2*1364+1300);
                var writes=rows.Where(r=>r.Phase==(reverse?"write-A":"write-B")).ToArray();Check(writes.Length==len*2,$"mode{mode} indirect{indirect} reverse{reverse}");
                for(int i=0;i<writes.Length;i++)Check(writes[i].Value==(reverse?0:0x30+i)&&writes[i].Address==(reverse?(indirect?0x7e2000:0x7e1001)+i:0x2140+Targets[mode][i%len]));
                Check(m.Bus.Dma.State.Completed[0],"terminating table descriptor");
            }
            var order=Machine();Channel(order,0,0,0x40,0x7e1000,0);Channel(order,1,0,0x41,0x7e1100,0);
            new byte[]{1,0x11,0}.CopyTo(order.Bus.Wram,0x1000);new byte[]{1,0x22,0}.CopyTo(order.Bus.Wram,0x1100);var log=Trace(order);order.Bus.Write(0x420c,3);order.RunUntil(1250);
            var line=log.Where(r=>r.Clock>=1104).ToArray();Check(line.Select(r=>r.Phase).SequenceEqual(new[]{"read-A","write-B","read-A","write-B","table-read","table-read"}),"all transfers precede all advances/reloads");
        });
        test("M3 HDMA no repeat skips lines table dummy and indirect terminal low byte",()=>{
            var wrap=Machine();Channel(wrap,0,0,0x40,0x7effff,0);wrap.Bus.Wram[0xffff]=1;wrap.Bus.Wram[0]=0x5a;wrap.Bus.Wram[1]=0;var wrapRows=Trace(wrap);wrap.Bus.Write(0x420c,1);wrap.RunUntil(1400);Check(wrapRows.Any(r=>r.Phase=="read-A"&&r.Address==0x7e0000&&r.Value==0x5a),"HDMA table/data pointer wraps within bank");
            var count128=Machine();Channel(count128,0,0,0x40,0x7e1000,0);new byte[]{0x80,0x5a,0}.CopyTo(count128.Bus.Wram,0x1000);var countRows=Trace(count128);count128.Bus.Write(0x420c,1);count128.RunUntil(128L*1364);Check(countRows.Count(r=>r.Phase=="write-B")==1&&count128.Bus.Dma.State.Completed[0],"descriptor80 runs128 lines with reference counter behavior");
            var m=Machine();Channel(m,0,0x40,0x40,0x7e1000,0,0x7e);new byte[]{2,0,0x20,0,0x55,0x66}.CopyTo(m.Bus.Wram,0x1000);m.Bus.Wram[0x2000]=0x42;var rows=Trace(m);m.Bus.Write(0x420c,1);m.RunUntil(3*1364);
            Check(rows.Count(r=>r.Phase=="write-B")==1,"descriptor2 transfers first line then skips second");
            Check(rows.Count(r=>r.Phase=="indirect-low")==2&&rows.Count(r=>r.Phase=="indirect-high")==1,"last completed channel fetches only pointer low");
            Check(rows.Count(r=>r.Phase=="table-read")==3,"reload dummy read still takes eight clocks while linecounter nonzero");
            Check(m.Bus.DmaRegisters[8]==5&&m.Bus.DmaRegisters[9]==0x10&&m.Bus.DmaRegisters[5]==0&&m.Bus.DmaRegisters[6]==0x55);
        });
        test("M3 HDMA preempts DMA at byte edge cancels shared channel",()=>{
            foreach(bool same in new[]{false,true}){
                var m=Machine();Channel(m,0,0,0x40,0x7e1000,0);new byte[]{1,0x5a,0}.CopyTo(m.Bus.Wram,0x1000);m.Bus.Write(0x420c,1);m.RunUntil(1080);
                int ch=same?0:1;Channel(m,ch,0,0x41,0x7e2000,30);var rows=Trace(m);m.Bus.Write(0x420b,(byte)(1<<ch));m.RunUntil(1600);
                var hdma=rows.Where(r=>r.Job==DmaJob.Line).ToArray();Check(hdma.Length>0&&hdma[0].Clock>=1104);
                int dmaWrites=rows.Count(r=>r.Job==DmaJob.Dma&&r.Phase=="write-B");Check(same?dmaWrites<30:dmaWrites==30,"HDMA steals its own DMA channel, other channels resume");
            }
        });
        test("M3 HDMA every clock state and observer resume during descriptors pointers transfers",()=>{
            var m=Machine();Channel(m,0,0x41,0x40,0x7e1000,0,0x7e);new byte[]{0x81,0,0x20,0,0x44}.CopyTo(m.Bus.Wram,0x1000);m.Bus.Write(0x420c,1);
            string initial=Path.Combine(directory,"m3-hdma-initial");StateSerializer.Save(m,initial);var baseline=StateSerializer.Load(m.Bus.Cartridge!,initial);baseline.RunUntil(1500);string final=Snapshot(baseline,directory,"m3-hdma-final");
            var saved=new List<string>();m.Bus.Dma.Observed=r=>{string p=Path.Combine(directory,"m3-hdma-observer"+saved.Count);StateSerializer.Save(m,p);saved.Add(p);};
            foreach(int t in Enumerable.Range(1,130).Concat(Enumerable.Range(1080,171))) {m.RunUntil(t);string p=Path.Combine(directory,"m3-hdma-phase");StateSerializer.Save(m,p);var copy=StateSerializer.Load(m.Bus.Cartridge!,p);copy.RunUntil(1500);Check(Snapshot(copy,directory,"m3-hdma-a")==final,$"HDMA phase{t}");}
            m.RunUntil(1500);foreach(string p in saved){var copy=StateSerializer.Load(m.Bus.Cartridge!,p);copy.RunUntil(1500);Check(Snapshot(copy,directory,"m3-hdma-observer-copy")==Snapshot(m,directory,"m3-hdma-observer-ref"),"committed descriptor/pointer observer phase");}
        });
        test("M3 ALU partial multiplication division zero and busy writes",()=>{
            var b=new SystemBus();b.Reset();b.Write(0x4202,13);b.Write(0x4203,17);Check(b.CpuIo.State.Product==0&&b.CpuIo.State.Quotient==0x110d);
            // Fixed arithmetic worksheet for 13*17. Each row is the observable
            // product/quotient, derived independently from binary long multiplication.
            int[] products=[17,17,85,221,221,221,221,221];
            int[] quotients=[0x886,0x443,0x221,0x110,0x88,0x44,0x22,0x11];
            for(int edge=1;edge<=8;edge++){b.CpuIo.AluEdge();Check(b.CpuIo.State.Product==products[edge-1]&&b.CpuIo.State.Quotient==quotients[edge-1]&&b.CpuIo.State.Multiply==8-edge);}
            var divide=new SystemBus();divide.Reset();divide.Write(0x4204,100);divide.Write(0x4205,0);divide.Write(0x4206,7);
            int[] divideQ=[0,0,0,0,0,0,0,0,0,0,0,0,1,3,7,14];
            int[] divideR=[100,100,100,100,100,100,100,100,100,100,100,100,44,16,2,2];
            for(int i=0;i<16;i++){divide.CpuIo.AluEdge();Check(divide.CpuIo.State.Quotient==divideQ[i]&&divide.CpuIo.State.Product==divideR[i],$"fixed long division edge{i+1}");}
            foreach(int divisor in new[]{0,1,7,255}){
                b.Write(0x4204,0x34);b.Write(0x4205,0x12);b.Write(0x4206,(byte)divisor);Check(b.CpuIo.State.Product==0x1234);
                for(int i=0;i<16;i++)b.CpuIo.AluEdge();Check(b.CpuIo.State.Quotient==(divisor==0?65535:0x1234/divisor)&&b.CpuIo.State.Product==(divisor==0?0x1234:0x1234%divisor));
            }
            b.Write(0x4202,3);b.Write(0x4203,9);b.CpuIo.AluEdge();int remaining=b.CpuIo.State.Multiply;b.Write(0x4203,5);Check(b.CpuIo.State.Multiply==remaining&&b.CpuIo.State.Product==0,"busy write clears product but does not restart counter");
        });
        test("M3 CPU ALU edges read after hold write before step refresh and save",()=>{
            var rmw=Machine(false);rmw.Cpu.PC=0;new byte[]{0xe6,0x10,0xdb}.CopyTo(rmw.Bus.Wram,0);rmw.Bus.Write(0x4202,7);rmw.Bus.Write(0x4203,11);rmw.RunUntil(25);Check(rmw.Bus.CpuIo.State.Multiply==5,"E-RMW invalid write pins are internal: ALU must wait for cycle completion30");rmw.RunUntil(30);Check(rmw.Bus.CpuIo.State.Multiply==3,"internal completion then valid-write before-step edge share clock30");rmw.RunUntil(38);Check(rmw.Bus.CpuIo.State.Multiply==3&&rmw.Bus.Wram[0x10]==1,"valid write endpoint has no extra ALU edge");
            foreach(bool refreshRmw in new[]{false,true}){
                var r=Machine(false);r.Cpu.PC=0;new byte[]{0xe6,0x10,0xdb}.CopyTo(r.Bus.Wram,0);r.Bus.Write(0x4202,7);r.Bus.Write(0x4203,11);if(refreshRmw)r.Clock.Schedule(26,HardwareEventKind.Refresh);
                string p=Path.Combine(directory,"m3-rmw-start");StateSerializer.Save(r,p);var baseline=StateSerializer.Load(r.Bus.Cartridge!,p);baseline.RunUntil(120);string expected=Snapshot(baseline,directory,"m3-rmw-end");
                for(int t=1;t<=100;t++){r.RunUntil(t);StateSerializer.Save(r,p);var copy=StateSerializer.Load(r.Bus.Cartridge!,p);copy.RunUntil(120);Check(Snapshot(copy,directory,"m3-rmw-copy")==expected,$"RMW internal/valid-write/refresh state at{t}");}
            }

            var m=Machine(false);m.Cpu.PC=0;m.Cpu.PBR=0;new byte[]{0xad,0x16,0x42,0x8d,0,0x10,0xdb}.CopyTo(m.Bus.Wram,0);m.Bus.Write(0x4202,13);m.Bus.Write(0x4203,17);
            m.RunUntil(24);Check(m.Bus.CpuIo.State.Multiply==5);m.RunUntil(26);Check(m.Bus.CpuIo.State.Multiply==5,"internal result read samples before own ALU edge");int sampled=m.CapturePhase().HeldRead!.Value!.Value;
            m.RunUntil(30);Check(m.Bus.CpuIo.State.Multiply==4&&m.Cpu.A==sampled);
            var refresh=Machine();refresh.Bus.Write(0x4202,7);refresh.Bus.Write(0x4203,11);refresh.RunUntil(538);Check(refresh.Bus.CpuIo.State.Multiply==8);refresh.RunUntil(577);Check(refresh.Bus.CpuIo.State.Multiply==4);refresh.RunUntil(578);Check(refresh.Bus.CpuIo.State.Multiply==3,"five ALU edges during DRAM refresh");
            foreach(int stop in Enumerable.Range(1,90)){
                var x=Machine(false);x.Cpu.PC=0;new byte[]{0x8d,0x40,0x21,0xdb}.CopyTo(x.Bus.Wram,0);x.Bus.Write(0x4202,7);x.Bus.Write(0x4203,11);x.RunUntil(stop);string path=Path.Combine(directory,"m3-alu");StateSerializer.Save(x,path);var copy=StateSerializer.Load(x.Bus.Cartridge!,path);x.RunUntil(100);copy.RunUntil(100);Check(Snapshot(x,directory,"m3-alu-a")==Snapshot(copy,directory,"m3-alu-b"),"write-before-step edge cannot repeat across budget");
            }
        });
        test("M3 H V HV IRQ holds readclear enable disable and line spill",()=>{
            foreach(var profile in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile})foreach(bool interlace in new[]{false,true})foreach(bool field in new[]{false,true})foreach(bool suppressed in new[]{false,true}){
                var boundary=new SnesSystem(profile);boundary.Insert(new Cartridge(DiagnosticRom.Create()));boundary.Cpu.Stopped=true;boundary.Bus.Ppu.Registers[0x33]=(byte)(interlace?1:0);boundary.RunUntil(1);
                long first=profile.FieldMasterClocks(false,interlace);long fieldStart=field?first:0;int lastLine=profile.FieldLines(field,interlace)-1;int length=profile.LineClocks(lastLine,field,interlace);
                if(field)boundary.RunUntil(first+1);
                int timer=(length-4)/4-1-(suppressed?0:1); // raw comparator at last/penultimate dot.
                boundary.Bus.Write(0x4207,(byte)timer);boundary.Bus.Write(0x4208,(byte)(timer>>8));boundary.Bus.Write(0x4209,(byte)lastLine);boundary.Bus.Write(0x420a,(byte)(lastLine>>8));boundary.Bus.Write(0x4200,0x30);
                long end=fieldStart+profile.FieldMasterClocks(field,interlace);boundary.RunUntil(end-1);Check(boundary.Clock.Capture().Events.Any(e=>e.Kind==HardwareEventKind.Device&&(e.Payload&255)==4&&e.Clock==end+(suppressed?6:2)),"delayed comparator event must actually be pending across field end");boundary.RunUntil(end+(suppressed?6:2));var tap=boundary.Clock.CounterBefore(6);
                Check(boundary.Bus.CpuIo.State.IrqFlag==!suppressed,$"historical counter6 last-dot IRQ {profile.Pal} interlace{interlace} field{field} length{length}");Check(suppressed?tap.H==0&&tap.V==0:tap.H!=0||tap.V!=0);
            }
            var shortLine=Machine();shortLine.RunUntil(HardwareProfile.Ntsc.FieldMasterClocks(false,false)+1);shortLine.Bus.Write(0x4207,0x52);shortLine.Bus.Write(0x4208,1);shortLine.Bus.Write(0x4209,240);shortLine.Bus.Write(0x420a,0);shortLine.Bus.Write(0x4200,0x30);long shortStart=HardwareProfile.Ntsc.FieldMasterClocks(false,false)+240L*1364;shortLine.RunUntil(shortStart+1366);Check(shortLine.Bus.CpuIo.State.IrqFlag&&shortLine.Clock.CounterBefore(6).V==241,"H6 after 1360-clock short line is not field H0/V0 and must allow IRQ");
            foreach(int early in new[]{0,1,5,9}){
                var e=Machine();e.RunUntil(early);e.Bus.Write(0x4209,0);e.Bus.Write(0x420a,0);e.Bus.Write(0x4200,0x20);e.RunUntil(10);Check(e.Bus.CpuIo.State.IrqFlag,$"V-only early enable H{early} preserves delayed H10 edge");
                var previous=Machine();previous.RunUntil(2*1364+early);previous.Bus.Write(0x4209,1);previous.Bus.Write(0x420a,0);previous.Bus.Write(0x4200,0x20);previous.RunUntil(2*1364+10);Check(previous.Bus.CpuIo.State.IrqFlag==(early<9),$"previous V compare at H{early}");
            }
            var token=Machine();token.Bus.CpuIo.State.IrqGeneration=0x7ffffe;token.Bus.Write(0x4207,20);token.Bus.Write(0x4208,0);token.Bus.Write(0x4200,0x10);token.RunUntil(94);Check(token.Bus.CpuIo.State.IrqFlag,"IRQ token rollover remains positive and decodable");
            foreach(int mode in new[]{0x10,0x20,0x30}){
                var m=Machine();m.Bus.Write(0x4207,20);m.Bus.Write(0x4208,0);m.Bus.Write(0x4209,2);m.Bus.Write(0x420a,0);m.Bus.Write(0x4200,(byte)mode);
                long edge=mode==0x10?94:2*1364+(mode==0x20?10:94);m.RunUntil(edge-1);Check(!m.Bus.CpuIo.State.IrqFlag);m.RunUntil(edge);Check(m.Bus.CpuIo.State.IrqFlag&&!m.Bus.CpuIo.IrqOutput);Check((m.Bus.Read(0x4211)&128)!=0&&m.Bus.CpuIo.State.IrqFlag,"readclear suppressed during four-clock hold");
                m.RunUntil(edge+4);Check(m.Bus.CpuIo.IrqOutput);Check((m.Bus.Read(0x4211)&128)!=0&&!m.Bus.CpuIo.State.IrqFlag);m.Bus.Write(0x4200,0);Check(!m.Bus.CpuIo.IrqOutput);
            }
            var midline=Machine();midline.RunUntil(2*1364+500);midline.Bus.Write(0x4209,2);midline.Bus.Write(0x420a,0);midline.Bus.Write(0x4200,0x20);midline.RunUntil(2*1364+501);Check(!midline.Bus.CpuIo.State.IrqFlag);midline.RunUntil(2*1364+502);Check(midline.Bus.CpuIo.State.IrqFlag,"V-only enable midline at next poll");midline.RunUntil(2*1364+506);midline.Bus.Read(0x4211);midline.Bus.Write(0x4209,2);midline.RunUntil(2*1364+510);Check(!midline.Bus.CpuIo.State.IrqFlag,"unchanged V level cannot continuously retrigger after readclear");midline.Bus.Write(0x4209,3);midline.RunUntil(2*1364+514);midline.Bus.Write(0x4209,2);midline.RunUntil(2*1364+518);Check(midline.Bus.CpuIo.State.IrqFlag,"VTIME change false then true creates a new edge");
            var spill=Machine();spill.Bus.Write(0x4207,0x52);spill.Bus.Write(0x4208,1);spill.Bus.Write(0x4200,0x10);spill.RunUntil(1365);Check(!spill.Bus.CpuIo.State.IrqFlag);spill.RunUntil(1366);Check(spill.Bus.CpuIo.State.IrqFlag,"HTIME338 delayed comparator survives line boundary");
        });
        test("M3 NMI delayed enable edge four clock read hold and field reset",()=>{
            long edge=225L*1364+2;var m=Machine();m.Bus.Write(0x4200,128);m.RunUntil(edge-1);Check(!m.Bus.NmiFlag);m.RunUntil(edge);Check(m.Bus.NmiFlag&&!m.Bus.CpuIo.State.NmiOutput);m.Bus.Read(0x4210);Check(m.Bus.NmiFlag);m.RunUntil(edge+4);Check(m.Bus.CpuIo.State.NmiOutput&&m.Cpu.CaptureInterrupts().NmiLatched);
            m.Bus.Read(0x4210);Check(!m.Bus.NmiFlag);m.RunUntil(m.Clock.Now+m.Clock.ClocksUntilFieldEnd);Check(!m.Bus.NmiFlag&&!m.Bus.CpuIo.State.NmiOutput);
            var enabled=Machine();enabled.RunUntil(edge+10);Check(enabled.Bus.NmiFlag&&!enabled.Bus.CpuIo.State.NmiOutput);enabled.Bus.Write(0x4200,128);enabled.RunUntil(edge+11);Check(enabled.Cpu.CaptureInterrupts().NmiLatched,"enable while RDNMI set causes edge");
        });
        test("M3 H V counter latches high bit toggles status reset and PIO falling",()=>{
            var m=Machine();m.RunUntil(1300);m.Bus.Read(0x2137);int dot=m.Clock.Dot;Check(m.Bus.Read(0x213c)==(dot&255));Check((m.Bus.Read(0x213c)&1)==dot>>8);Check(m.Bus.Read(0x213c)==(dot&255));
            Check((m.Bus.Read(0x213f)&64)!=0&&!m.Bus.CpuIo.State.ReadHHigh);Check((m.Bus.Read(0x213f)&64)==0);
            m.RunUntil(2*1364+100);m.Bus.Write(0x4201,0x7f);Check(m.Bus.CpuIo.State.LatchedH==25&&m.Bus.CpuIo.State.LatchedV==2);Check(m.Bus.Read(0x213d)==2);m.Bus.Read(0x213f);Check(!m.Bus.CpuIo.State.ReadVHigh);
        });
        test("M3 serial four data lines live strobe falling latch and signature",()=>{
            var b=new SystemBus();b.Reset();b.Controllers.AuxiliaryConnected[0]=b.Controllers.AuxiliaryConnected[1]=true;b.Controllers.Buttons[0]=0x8000;b.Write(0x4016,1);Check((b.Read(0x4016)&1)==1);b.Controllers.Buttons[0]=0x0100;b.Controllers.Buttons[1]=0x0200;b.Controllers.Buttons[2]=0x0040;b.Controllers.Buttons[3]=0x0020;b.Write(0x4016,0);
            for(int bit=0;bit<16;bit++)for(int port=0;port<2;port++){
                int value=b.Read(0x4016+port);Check((value&1)==((b.Controllers.Buttons[port]>>(15-bit))&1));Check(((value>>1)&1)==((b.Controllers.Buttons[port+2]>>(15-bit))&1));if(port==1)Check((value&28)==28);
            }
            Check((b.Read(0x4016)&3)==3&&(b.Read(0x4017)&3)==3);
            var standard=new ControllerPorts();standard.Buttons[0]=0xffff;standard.Write(1);standard.Write(0);for(int i=0;i<16;i++)Check(standard.Read(0)==(i<12?1:0),"standard gamepad signature and low data2 line");Check(standard.Read(0)==1);
        });
        test("M3 automatic JOY exact start busy shifts all four results and replay",()=>{
            var m=Machine();m.Bus.Controllers.AuxiliaryConnected[0]=m.Bus.Controllers.AuxiliaryConnected[1]=true;m.Bus.Controllers.Buttons[0]=0x8100;m.Bus.Controllers.Buttons[1]=0x4200;m.Bus.Controllers.Buttons[2]=0x2400;m.Bus.Controllers.Buttons[3]=0x1800;m.Bus.Write(0x4200,1);
            long start=(225L*1364+130+255)&~255L;m.RunUntil(start-1);Check(!m.Bus.CpuIo.AutoBusy&&m.Bus.CpuIo.State.AutoCounter==33);m.RunUntil(start);Check(m.Bus.CpuIo.AutoBusy&&m.Bus.CpuIo.State.AutoCounter==0);
            m.Bus.Controllers.Buttons[0]=0x5500; // input changes while auto strobe high: sampled at falling edge.
            m.RunUntil(start+128);Check(m.Bus.Controllers.Latched[0]==0x5500);m.RunUntil(start+33*128-1);Check(m.Bus.CpuIo.AutoBusy);m.RunUntil(start+33*128);Check(!m.Bus.CpuIo.AutoBusy);
            for(int i=0;i<4;i++)Check((m.Bus.Read(0x4218+i*2)|m.Bus.Read(0x4219+i*2)<<8)==m.Bus.Controllers.Buttons[i],$"JOY{i+1}");
            foreach(int offset in new[]{0,1,127,128,255,256,383,384,4095,4223,4224}){
                var a=Machine();a.Bus.Write(0x4200,1);a.Bus.Controllers.Buttons[0]=0xa500;a.RunUntil(start+offset);string p=Path.Combine(directory,"m3-joy");StateSerializer.Save(a,p);var copy=StateSerializer.Load(a.Bus.Cartridge!,p);a.RunUntil(start+4300);copy.RunUntil(start+4300);Check(Snapshot(a,directory,"m3-joy-a")==Snapshot(copy,directory,"m3-joy-b"));
            }
        });
        test("M3 IRQ NMI DMA event collisions state chunks and meaningful mutations",()=>{
            foreach(int offset in new[]{-4,-1,0,1,4}){
                var m=Machine();m.Bus.Write(0x4200,0xb1);m.Bus.Write(0x4207,20);m.Bus.Write(0x4208,0);m.Bus.Write(0x4209,225);m.Bus.Write(0x420a,0);long start=225L*1364+94+offset;m.RunUntil(start);Channel(m,0,0,0x40,0x7e1000,4);m.Bus.Write(0x420b,1);
                string p=Path.Combine(directory,"m3-collision");StateSerializer.Save(m,p);var tiny=StateSerializer.Load(m.Bus.Cartridge!,p);m.RunUntil(start+200);for(long t=start+1;t<=start+200;t++)tiny.RunUntil(t);Check(Snapshot(m,directory,"m3-collision-a")==Snapshot(tiny,directory,"m3-collision-b"));
            }
            var original=Machine();original.Bus.Wram[0x1000]=0x40;original.Bus.Wram[0x1001]=0x41;Channel(original,0,1,0x40,0x7e1000,2);original.Bus.Write(0x420b,1);var rows=Trace(original);original.RunUntil(50);
            Check(Protocol(rows,1,false,2,0x7e1000,0x2140,0x40)==null);
            var wrongClock=rows.ToArray();wrongClock[0]=wrongClock[0] with {Clock=wrongClock[0].Clock+1};Check(Protocol(wrongClock,1,false,2,0x7e1000,0x2140,0x40)=="byte0 clock","read latch mutation rejected by same protocol oracle");
            var wrongChannel=rows.ToArray();wrongChannel[0]=wrongChannel[0] with {Channel=1};Check(Protocol(wrongChannel,1,false,2,0x7e1000,0x2140,0x40)=="byte0 channel","priority mutation rejected by same protocol oracle");
            var wrongTarget=rows.ToArray();wrongTarget[3]=wrongTarget[3] with {Address=0x2140};Check(Protocol(wrongTarget,1,false,2,0x7e1000,0x2140,0x40)=="byte1 address","mode alternation mutation rejected by same protocol oracle");
        });
    }
}
