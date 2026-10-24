using System.Text.Json;
using System.IO.Compression;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;
using SNESEmulator.Testing;

static class SuperFxProof
{
    static void Check(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
    static string State(Gsu g)=>JsonSerializer.Serialize(g.Capture());
    static Gsu Program(byte[] bytes,out byte[] ram,int pc=0,int mode=24,int clock=0,int config=0)
    {
        ram=new byte[131072];var g=new Gsu(new byte[32768],ram);g.WriteIo(0x303a,(byte)mode);g.WriteIo(0x3039,(byte)clock);g.WriteIo(0x3037,(byte)config);
        for(int i=0;i<512;i++)g.WriteIo(0x3100+i,i<bytes.Length?bytes[i]:(byte)1);
        g.WriteIo(0x301e,(byte)pc);g.WriteIo(0x301f,(byte)(pc>>8));return g;
    }
    static void Finish(Gsu g,int bound=10000){g.AdvanceTo(g.Tick+bound);Check(!g.Running,"GSU did not STOP within bounded execution.");}
    static Gsu Single(int opcode,int alt,ushort[] registers,int status=0)
    {
        var g=Program([1,0x80,0x23,1],out _);var s=g.Capture();registers=registers.ToArray();registers[15]=0;
        g.Restore(s with{Registers=registers,Pipeline=(byte)opcode,PcWritten=false,Status=(ushort)(32|(alt<<8)|status)});
        for(int t=0;t<2000&&g.Instructions==0;t++)g.AdvanceTo(g.Tick+1);
        Check(g.Instructions==1,"Single instruction did not retire");return g;
    }
    static SnesSystem Machine(HardwareProfile p)
    {var m=new SnesSystem(p);m.Insert(new Cartridge(SuperFxDiagnosticRom.Create(p.Pal),hardware:new SuperFxConfiguration(SuperFxRevision.Gsu2)));return m;}
    static byte[] Unzip(string file){using var f=File.OpenRead(file);using var z=new GZipStream(f,CompressionMode.Decompress);using var output=new MemoryStream();z.CopyTo(output);return output.ToArray();}
    static void VerifyFixtures()
    {
        using var manifest=JsonDocument.Parse(File.ReadAllText("testdata/superfx/manifest.json"));
        foreach(var entry in manifest.RootElement.GetProperty("files").EnumerateObject())Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes("testdata/superfx/"+entry.Name))).Equals(entry.Value.GetString(),StringComparison.OrdinalIgnoreCase),"Changed SuperFX fixture: "+entry.Name);
    }
    public static int SourceAudit(string[] args)
    {
        VerifyFixtures();var report=new TestReport("SuperFX-raw-software-source-audit");
        using var input=new BinaryReader(new MemoryStream(Unzip("testdata/superfx/inputs.bin.gz")));using var expected=new BinaryReader(new MemoryStream(Unzip("testdata/superfx/expected.bin.gz")));
        int count=input.ReadInt32();Check(count==8192&&expected.ReadInt32()==count,"Nonvacuous vector count");int differences=0,stopOnly=0,romBusyOnly=0,scopedDifferences=0;var details=new List<object>();
        for(int i=0;i<count;i++){
            int op=input.ReadByte(),alt=input.ReadByte(),status=input.ReadUInt16();ushort[] regs=new ushort[16],want=new ushort[16];for(int n=0;n<16;n++)regs[n]=input.ReadUInt16();for(int n=0;n<16;n++)want[n]=expected.ReadUInt16();ushort flags=expected.ReadUInt16();
            var g=Single(op,alt,regs,status);bool same=g.Registers.SequenceEqual(want)&&g.Status==flags;
            bool scopedSame=g.Registers.SequenceEqual(want)&&(g.Status&~64)==(flags&~64);if(!scopedSame)scopedDifferences++;
            if(!same){differences++;if(g.Registers.SequenceEqual(want)&&(g.Status^flags)==64)romBusyOnly++;bool stop=op==0&&g.Registers.Take(15).SequenceEqual(want.Take(15))&&g.Registers[15]==0&&want[15]==1&&g.Status==flags;if(stop)stopOnly++;if(details.Count<100)details.Add(new{index=i,opcode=op,alt,status,actualRegisters=g.Registers,expectedRegisters=want,actualStatus=g.Status,expectedStatus=flags,primaryStopOnly=stop});}
        }
        report.Metadata["cases"]=count;report.Metadata["mismatches"]=differences;report.Metadata["primaryStopOnly"]=stopOnly;report.Metadata["outerRomBusyHookOnly"]=romBusyOnly;report.Metadata["additionalProcessorScopeMismatches"]=scopedDifferences;report.Metadata["firstDifferences"]=details;
        report.Add("Unmodified pinned software processor outputs",differences==0?"pass":"fail","Reference output bytes are preserved. OrdinarySTOP now follows the explicit nocash research account and agrees with pinned software;112 ROMbusy-flag differences arise because this processor-only adapter excludes the reference outer SuperFX R14 update hook. Full raw status remains compared here. Other primary/software timing, FMULT-R4, transparency and ownership conflicts are separately inventoried; these semantic vectors do not cover them.");
        report.Add("Raw accuracy inventory guard",differences==112&&stopOnly==0&&romBusyOnly==112&&scopedDifferences==0?"pass":"fail","Inventory guard, not raw all-pass or physical certification.");report.Save(args.FirstOrDefault()??"artifacts/m10b-superfx/raw-source-audit");return report.ExitCode;
    }
    static bool TerminalLoop(SnesSystem m)
    {
        // The reviewed author sources terminate in BRA Loop (80 FE), often
        // relocated into WRAM. No side-effecting bus read is used for inspection.
        byte Peek(int a)=>a<0x2000?m.Bus.Wram[a]:m.Bus.Cartridge!.Rom[m.Bus.Cartridge.RomOffset((m.Cpu.PBR<<16)|(a&65535))];
        for(int delta=-2;delta<=0;delta++){int a=(m.Cpu.PC+delta)&65535;if(Peek(a)==0x80&&Peek((a+1)&65535)==0xfe)return true;}
        return false;
    }
    public static int PrimaryAudit(string[] args)
    {
        var report=new TestReport("SuperFX-primary-source-contradictions");
        var g=Program([0,1],out _);Finish(g);int pc=g.Registers[15];
        report.Add("BookII2-5-3 ordinary STOP address+1",pc==1?"pass":"fail","Original manufacturer claim conflicts with explicit nocash STOPaddress+2 research account; current digital model follows the latter.",evidence:new{actualR15=pc,bookExpected=1,nocashExpected=2});
        // COLR10: the odd pixel selects nonzero high nibble. BookII says the
        // pre-dither low nibble makes both pixels transparent; nocash describes
        // post-dither semitransparency. Keep both predictions in the report.
        g=Program([0xf0,0x10,0,0x4e,0xf0,2,0,0x3d,0x4e,0x4c,0x4c,0x3d,0x4c,0,1],out var ram);ram[0]=ram[1]=0xc0;Finish(g);
        report.Add("BookII2-8-11 pre-dither transparency",ram[1]==0xc0?"pass":"fail","Current model follows nocash post-dither drawing-color mask. Manufacturer and shared bsnes pre-dither choice remain contradictory, no new physical capture.",evidence:new{actualPlane1=ram[1],bookAndSharedReferenceExpected=0xc0,nocashExpected=0x80});
        report.Add("FMULT destinationR4 physical result","fail","Manufacturer/developer cautions and nocash 'reportedly unchanged' do not resolve original PeterLemon numeric-result expectation. Deterministic retained value remains a model choice, not certified silicon behavior.");
        report.Save(args.FirstOrDefault()??"artifacts/m10b-superfx/primary-source-audit");return report.ExitCode;
    }
    public static int PublishedRoms(string[] args)
    {
        var report=new TestReport("SuperFX-published-source-author-ROMs");string prefix=args.FirstOrDefault()??"artifacts/m10b-superfx/published-roms";
        if(args.Length<2){report.Add("PeterLemon local originals","skipped","No explicit local source-author checkout. No license grant inferred, no ROM redistributed.");report.Save(prefix);return report.ExitCode;}
        string root=args[1];var results=new List<object>();
        foreach(string file in Directory.GetFiles(Path.Combine(root,"CHIP/GSU/GSUTest"),"*.sfc",SearchOption.AllDirectories).OrderBy(x=>x))
        {
            if(args.Length>2&&!Path.GetFileName(file).Contains(args[2],StringComparison.OrdinalIgnoreCase))continue;
            string asm=Path.ChangeExtension(file,".asm");if(!File.Exists(asm))continue;
            string source=File.ReadAllText(asm);string finalPage=source[source.LastIndexOf("ClearVRAM(",StringComparison.Ordinal)..];var slots=System.Text.RegularExpressions.Regex.Matches(finalPage,@"PrintText\(Pass,\s*\$([0-9A-Fa-f]+),\s*4\)").Select(m=>Convert.ToInt32(m.Groups[1].Value,16)).Distinct().ToArray();
            if(slots.Length==0){report.Add(Path.GetFileName(file),"skipped","No reviewed PASS terminal text contract");continue;}
            var m=new SnesSystem();var bytes=File.ReadAllBytes(file);m.Insert(new Cartridge(bytes,file,hardware:new SuperFxConfiguration(SuperFxRevision.Gsu2,65536)));int fields=0,stable=0;long last=-1;bool all=false;string? reason=null;var multiplyTrace=new List<GsuInstructionTrace>();
            if(Path.GetFileName(file)=="GSUFMULT.sfc")((SuperFxCartridgeHardware)m.Bus.Cartridge!.Hardware!).Processor.InstructionObserved=entry=>{if(entry.Opcode==0x9f)multiplyTrace.Add(entry);};
            try{
                for(;fields<300;fields++){
                    m.RunFrame();var chip=((SuperFxCartridgeHardware)m.Bus.Cartridge!.Hardware!).Processor;
                    all=slots.All(a=>Enumerable.Range(0,4).All(n=>m.Bus.Ppu.Vram[(a+n*2)&65535]=="PASS"[n]));
                    bool fail=false;for(int a=0xf800;a<0xffff-8;a+=2)if(m.Bus.Ppu.Vram[a]=='F'&&m.Bus.Ppu.Vram[a+2]=='A'&&m.Bus.Ppu.Vram[a+4]=='I'&&m.Bus.Ppu.Vram[a+6]=='L')fail=true;
                    if(fail){reason="Source-author ROM printed FAIL";break;}
                    if(all&&!chip.Running&&chip.Instructions==last)stable++;else stable=0;last=chip.Instructions;
                    if(stable>=3&&TerminalLoop(m))break;
                }
            }catch(Exception e){reason=e.Message;}
            bool pass=all&&stable>=3&&last>0&&reason==null&&TerminalLoop(m);string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            results.Add(new{file=Path.GetFileName(file),sha256=hash,pass,fields,passSlots=slots.Length,stableFields=stable,instructions=last,reason,multiplyTrace,cpuResultWord=m.Bus.Wram[0]|m.Bus.Wram[1]<<8,cpuFlags=m.Bus.Wram[2]|m.Bus.Wram[3]<<8,pc=m.Cpu.PC,terminalLoop=TerminalLoop(m),passAddresses=slots.Select(a=>new{address=a,actual=new string(Enumerable.Range(0,4).Select(n=>(char)m.Bus.Ppu.Vram[(a+n*2)&65535]).ToArray())}),processor=((SuperFxCartridgeHardware)m.Bus.Cartridge!.Hardware!).Processor.Capture(),sourceSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(asm)))});report.Add(Path.GetFileName(file),pass?"pass":"fail",reason??(!pass?"Nonvacuous stable all-PASS terminal not reached":null));
        }
        report.Metadata["results"]=results;report.Metadata["source"]="PeterLemon/SNES commit350b394e86ec5d62f600b5cbf64cdce3721bb6ef. Author says tests realSNES+SD2SNES; our local software execution is not an original capture. No redistribution license found.";report.Save(prefix);return report.ExitCode;
    }
    public static int Run(string[] args)
    {
        VerifyFixtures();var report=new TestReport("M10b-SuperFX");string prefix=args.FirstOrDefault()??"artifacts/m10b-superfx/proof";
        void Test(string name,Action body){var w=System.Diagnostics.Stopwatch.StartNew();try{body();report.Add(name,"pass",seconds:w.Elapsed.TotalSeconds);}catch(Exception e){report.Add(name,"fail",e.ToString(),w.Elapsed.TotalSeconds);Console.WriteLine("FAIL "+name+": "+e.Message);}}
        report.Metadata["scope"]="Manufacturer literal semantics, authored CPU/GSU integration, separately executed pinned software vectors. No commercial game or physical capture certification. GSU1/2 digital common model, independent explicit nominal oscillator; revision quirks and fast multiply silicon errors unverified.";
        report.Metadata["externalHardwareRoms"]=new{Executed=false,Reason="Run superfx-published-roms separately with an explicit local source-author checkout. No original ROM is bundled or silently executed by this functional proof."};
        Test("All256 opcodes x4 ALT modes retire within a bound",()=>{
            for(int op=0;op<256;op++)for(int alt=0;alt<4;alt++){var r=Enumerable.Range(0,16).Select(i=>(ushort)(i*0x111+0x1234)).ToArray();Single(op,alt,r);}
        });
        Test("Unsigned addition/subtraction independent integer flags 32768 samples",()=>{
            uint seed=0x19870915;for(int i=0;i<8192;i++){
                seed=seed*1664525+1013904223;int a=(int)(seed>>16);seed=seed*1664525+1013904223;int b=(int)(seed>>16);
                foreach(bool subtract in new[]{false,true})foreach(int carry in new[]{0,1}){
                    ushort[] r=new ushort[16];r[0]=(ushort)a;r[3]=(ushort)b;int want=subtract?a-b-(1-carry):a+b+carry;
                    var g=Single(subtract?0x63:0x53,1,r,carry*4);Check(g.Registers[0]==(ushort)want,"ALU result");
                    Check(((g.Status&4)!=0)==(subtract?want>=0:want>65535),"ALU carry");Check(((g.Status&2)!=0)==((ushort)want==0),"ALU zero");
                    bool overflow=subtract?(((a^b)&(a^want)&0x8000)!=0):(((~(a^b))&(a^want)&0x8000)!=0);Check(((g.Status&16)!=0)==overflow,"ALU overflow");
                }
            }
        });
        Test("Manufacturer2-6-2 branch delay and branch-in-delay operand",()=>{
            // BRA +3, INC R1 delay, skipped INC R2, padding, target STOP.
            var g=Program([5,3,0xd1,0xd2,1,0,1],out _);Finish(g);Check(g.Registers[1]==1&&g.Registers[2]==0&&g.Registers[15]==7,"Delay slot and STOP PC");
            // Book example2: branch-delay BRA consumes TO R1 ($11) at target
            // as its offset. It lands target+1+$11, not the lexical operand.
            byte[] code=Enumerable.Repeat((byte)1,64).ToArray();code[0]=5;code[1]=6;code[2]=5;code[8]=0x11;code[26]=0;
            g=Program(code,out _);Finish(g);Check(g.Registers[15]==28,"Branch in delay-slot operand must come from destination");
        });
        Test("Manufacturer2-5-3 transfer and jump STOP retained R15",()=>{
            var g=Program([0xff,0x40,0,0,1],out _);Finish(g);Check(g.Registers[15]==0x41,"IWT R15 followed by pipeline STOP");
            g=Program([0xf8,0x40,0,0x98,0,1],out _);Finish(g);Check(g.Registers[15]==0x41,"JMP followed by pipeline STOP");
        });
        Test("Manufacturer2-6-12 STOP next-line prefetch WAIT",()=>{
            var g=Program(Enumerable.Repeat((byte)1,16).ToArray(),out _,15);g.WriteIo(0x310f,0);
            var s=g.Capture();var v=new bool[32];v[0]=true;g.Restore(s with{Valid=v});g.WriteIo(0x303a,0);g.AdvanceTo(100);
            Check(g.Running,"STOP incorrectly skipped inaccessible next-line prefetch");g.WriteIo(0x303a,16);Finish(g);Check(!g.Running,"ROM ownership did not release WAIT");
        });
        Test("Manufacturer cache line validation, forced stop versus STOP retention",()=>{
            var g=Program([0,1],out _);Finish(g);Check(g.Capture().Valid.All(v=>v),"STOP cleared valid cache");
            g.WriteIo(0x301f,0);g.WriteIo(0x3030,0);Check(g.Capture().Valid.All(v=>!v)&&g.Capture().CacheBase==0,"Forced GO0 did not invalidate");
        });
        Test("Manufacturer dummy vector all16 bytes and IRQ high-byte acknowledge",()=>{
            byte[] want=[0,1,0,1,4,1,0,1,0,1,8,1,0,1,12,1];for(int i=0;i<16;i++)Check(SuperFxCartridgeHardware.DummyVector(0xffe0+i)==want[i],"Dummy vector");
            var g=Program([0,1],out _);bool irq=false;g.IrqChanged=b=>irq=b;Finish(g);Check(irq&&g.Irq,"STOP IRQ");g.ReadIo(0x3030);Check(irq,"Low SFR read acknowledged");Check((g.ReadIo(0x3031)&128)!=0&&!irq&&!g.Irq,"High SFR ack");
            g=Program([0,1],out _,config:128);Finish(g);Check(g.Irq&&!g.IrqOutput,"CFGR disables output while retaining STOP status");
        });
        Test("Buffered store byte order, odd word address xor and ROM fetch",()=>{
            var g=Program([0xf0,0x34,0x12,0xf1,0x21,0,0x31,0x3d,0x41,0,1],out var ram);Finish(g);Check(ram[0x21]==0x34&&ram[0x20]==0x12&&g.Registers[0]==0x34,"Odd word RAM address/byte load");
            byte[] rom=new byte[32768];rom[0x1234]=0xab;g=new Gsu(rom,new byte[131072]);for(int i=0;i<16;i++)g.WriteIo(0x3100+i,new byte[]{0xfe,0x34,0x12,0xef,0,1}.ElementAtOrDefault(i));g.WriteIo(0x303a,24);g.WriteIo(0x301f,0);Finish(g);Check(g.Registers[0]==0xab,"R14 buffered ROM fetch");
        });
        foreach(int depth in new[]{0,1,2,3})foreach(int height in new[]{0,1,2,3})
        Test($"Planar graphics depth{depth} height{height} literal address and RPIX",()=>{
            // Draw x=9,y=10 with all planes set, then RPIX flushes both caches.
            var g=Program([0xf0,0xff,0,0x4e,0xf1,9,0,0xf2,10,0,0x4c,0xe1,0x3d,0x4c,0,1],out var ram,mode:24|depth|((height&1)<<2)|((height&2)<<4));
            Finish(g);int bpp=depth==0?2:depth==3?8:4;
            int tile=height==3?17:17+height*4;int address=tile*bpp*8+4;
            for(int p=0;p<bpp;p++)Check(ram[address+(p/2)*16+p%2]==0x40,"Literal planar bit address");Check(g.Registers[0]==(1<<bpp)-1,"RPIX color");
        });
        Test("Manufacturer cached LDW slow7cycles, LDB slow6cycles, LM fast14cycles",()=>{
            foreach(var (code,fast,want) in new[]{(new byte[]{0x41},false,14),(new byte[]{0x3d,0x41},false,12),(new byte[]{0x3d,0xf0,0x20,0},true,14)})
            {
                var g=Program(code.Concat(new byte[]{0,1}).ToArray(),out var ram,clock:fast?1:0);ram[0x20]=0x34;ram[0x21]=0x12;g.Registers[1]=0x20;
                // Skip the reset NOP bootstrap; count the exact documented code
                // bytes, including ALT1. Cache lines were populated by S-CPU.
                var st=g.Capture();g.Restore(st with{Pipeline=code[0],Registers=st.Registers.Select((v,i)=>i==15?(ushort)1:v).ToArray()});
                long initial=g.Tick;int count=code[0]==0x3d?2:1;while(g.Instructions<count&&g.Tick<100)g.AdvanceTo(g.Tick+1);
                Check(g.Tick-initial==want,"Primary cached latency got"+(g.Tick-initial)+" expected"+want);
            }
        });
        Test("Manufacturer FMULT R4 suppression and2bpp effective transparency",()=>{
            var g=Program([0x14,0x9f,0,1],out _);g.Registers[0]=0x4000;g.Registers[6]=0x4000;g.Registers[4]=0x9876;Finish(g);Check(g.Registers[4]==0x9876,"FMULT must not overwrite R4");
            foreach(int c in new[]{4,8,12}){g=Program([0xf0,(byte)c,0,0x4e,0x4c,0xe1,0x3d,0x4c,0,1],out var ram);ram[0]=0x80;ram[1]=0x80;Finish(g);Check(ram[0]==0x80&&ram[1]==0x80,"2bpp transparent effective zero erased pixel");}
        });
        Test("Nocash post-dither transparency effective masks,8bpp ignores dither",()=>{
            foreach(int mode in new[]{0,1,3})foreach(bool freeze in new[]{false,true}){
                byte[] code=[0xf0,0x10,0,0x4e,0xf0,(byte)(freeze?10:2),0,0x3d,0x4e,0x4c,0x4c,0x3d,0x4c,0,1];
                var g=Program(code,out var ram,mode:24|mode);int bpp=mode==0?2:mode==3?8:4;for(int plane=0;plane<bpp;plane++)ram[(plane/2)*16+plane%2]=0xc0;Finish(g);
                for(int plane=0;plane<bpp;plane++){
                    byte want=mode==3?(byte)(freeze?0xc0:plane==4?0xc0:0):(byte)(plane==0?0xc0:0x80);
                    Check(ram[(plane/2)*16+plane%2]==want,"Dither/transparency mode"+mode+" freeze"+freeze+" plane"+plane);
                }
            }
        });
        Test("Every tick continuation includes RAM, cache fill, all timed microkinds and both buffers",()=>{
            byte[] code=[0xf0,0xff,0,0x4e,0xf1,9,0,0xf2,10,0,0x4c,0xe1,0x3d,0x4c,0xf1,0x21,0,0x31,0x3d,0x41,0xfe,0x34,0x12,0xef,0x9f,0,1];
            int snapshots=0;var phases=new HashSet<int>();var kinds=new HashSet<GsuMicroKind>();bool cacheFill=false,romBuffer=false,ramBuffer=false,pixelBuffer=false;
            foreach(bool cached in new[]{true,false})for(int t=0;t<=700;t++){
                byte[] rom=new byte[32768];code.CopyTo(rom,0);byte[] ram;Gsu g;
                if(cached)g=Program(code,out ram,mode:27);else{ram=new byte[131072];g=new Gsu(rom,ram);g.WriteIo(0x303a,27);g.WriteIo(0x301f,0);}
                g.AdvanceTo(t);var state=g.Capture();phases.Add(state.Phase);
                if(state.Phase==4&&state.OperationIndex<state.Operations.Length)kinds.Add(state.Operations[state.OperationIndex].Kind);
                cacheFill|=state.Phase is 1 or 3&&state.FetchOffset>=0&&!state.Valid[state.FetchOffset>>4]&&state.FillIndex>0;
                romBuffer|=state.RomDue>=0;ramBuffer|=state.RamDue>=0;pixelBuffer|=state.Pixels.Any(p=>p.Mask!=0);
                var cloneRam=ram.ToArray();var clone=new Gsu(cached?new byte[32768]:rom,cloneRam);clone.Restore(state);g.AdvanceTo(2000);clone.AdvanceTo(2000);
                Check(State(g)==State(clone)&&ram.SequenceEqual(cloneRam),"Continuation state/RAM at tick"+t+" cached"+cached);snapshots++;
            }
            Check(snapshots==1402&&phases.Contains(1)&&phases.Contains(3)&&phases.Contains(4)&&cacheFill&&romBuffer&&ramBuffer&&pixelBuffer,"Vacuous phase/buffer coverage");
            foreach(var kind in new[]{GsuMicroKind.Delay,GsuMicroKind.ReadRam,GsuMicroKind.ReadGraphic,GsuMicroKind.WriteGraphic})Check(kinds.Contains(kind),"Unvisited timed microkind "+kind);
            report.Metadata["tickContinuations"]=new{snapshots,phases=phases.Order().ToArray(),timedMicroKinds=kinds.Select(k=>k.ToString()).ToArray(),cacheFill,romBuffer,ramBuffer,pixelBuffer,ramCompared=true};
        });
        Test("Malformed GSU microphase mutations reject without live RAM effects",()=>{
            var g=Program([0x41,0,1],out var ram);g.Registers[1]=0x20;g.AdvanceTo(5);var state=g.Capture();Check(state.Phase==4&&state.Operations.Any(o=>o.Kind==GsuMicroKind.ReadRam),"Missing active read mutation witness");
            var mutations=new[]{state with{OperationDue=state.Tick+1000},state with{FetchDue=state.Tick+1},state with{GraphicByte=256},state with{Status=(ushort)(state.Status|1)},state with{Operations=[new(GsuMicroKind.BufferWrite,0x20,1),new(GsuMicroKind.Execute),new(GsuMicroKind.Finish)],Opcode=1},state with{Operations=[new(GsuMicroKind.Execute)]},state with{RamSecondPending=true,RamDue=-1}};
            foreach(var bad in mutations){bool rejected=false;try{g.Restore(bad);}catch(InvalidDataException){rejected=true;}Check(rejected&&State(g)==JsonSerializer.Serialize(state)&&ram.All(v=>v==0),"Malformed continuation mutated live GSU");}
        });
        Test("Ownership WAIT freezes transfer latency and warm reset retains shared RAM",()=>{
            var g=Program([0x41,0,1],out var ram);g.Registers[1]=0x20;ram[0x20]=0x34;g.AdvanceTo(5);var before=g.Capture();Check(before.OperationDue>g.Tick,"No pending RAM read");g.WriteIo(0x303a,16);g.AdvanceTo(105);var held=g.Capture();Check(held.OperationDue-before.OperationDue==100,"Denied RAM bus consumed latency");g.WriteIo(0x303a,24);Finish(g);Check(g.Registers[0]==0x34,"WAIT resume");
            var m=Machine(HardwareProfile.Ntsc);m.RunUntil(20000);m.Bus.Cartridge!.Sram[7]=0x65;m.Reset();Check(m.Clock.Now==0&&m.Bus.Cartridge.Sram[7]==0x65,"Warm reset epoch/RAM");m.RunUntil(100);Check(((SuperFxCartridgeHardware)m.Bus.Cartridge.Hardware!).Processor.Tick>=0,"Reset cartridge timeline");
        });
        Test("Explicit board sidecar clone, mapper literals and rejected unsupported configurations",()=>{
            string dir=Path.Combine(Path.GetTempPath(),"snes-superfx-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            try{string path=Path.Combine(dir,"own.sfc");var config=new SuperFxConfiguration(SuperFxRevision.Gsu1,32768,21440000,1);File.WriteAllText(path+".hardware.json",JsonSerializer.Serialize(config,new JsonSerializerOptions{Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}}));
                var cart=new Cartridge(SuperFxDiagnosticRom.Create(),path);Check(Equals(cart.HardwareConfiguration,config),"Actual sidecar configuration");var clone=new Cartridge(cart.Rom,path,cart.Options,cart.HardwareConfiguration);Check(Equals(clone.HardwareConfiguration,config)&&!ReferenceEquals(clone.Sram,cart.Sram),"Immutable config / private RAM clone");
                var bus=new SystemBus{Cartridge=cart};bus.Write(0x700001,0x5a);Check(bus.Read(0x006001)==0x5a&&bus.Read(0x806001)==0x5a&&bus.Read(0x708001)==0x5a,"Literal32KiB RAM mirrors");
                bus.Write(0x303a,24);bus.Write(0x301f,0);Check(bus.Read(0x00ffe4)==4&&bus.Read(0x00ffe5)==1,"Literal blocked ROM vector");bus.OpenBus=0x77;Check(bus.Read(0x700001)==0x77,"Owned RAM open bus");bus.Write(0x700001,0x33);Check(cart.Sram[1]==0x5a,"Owned RAM write");
                foreach(var bad in new[]{config with{BackupRamBytes=128},config with{CpuOnlyRomBytes=65536},config with{CrystalHzNumerator=9000000000000000000,CrystalHzDenominator=900000000000},config with{RamBytes=0}}){bool rejected=false;try{new Cartridge(cart.Rom,hardware:bad);}catch(Exception e)when(e is InvalidDataException or NotSupportedException){rejected=true;}Check(rejected,"Unsupported board accepted");}
            }finally{Directory.Delete(dir,true);}
        });
        foreach(var p in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile})
        Test("65816 authored ROM cache/shared RAM/IRQ and2MC snapshot replay "+p.Id,()=>{
            var m=Machine(p);var dma=new List<SNESEmulator.Core.BusAccess>();m.Bus.AccessOriginObserved=a=>{if(a.Origin is AccessOrigin.DmaA or AccessOrigin.DmaB)dma.Add(a);};m.RunUntil(60000);Check(m.Cpu.Stopped&&m.Bus.Wram[0]==0x34&&m.Bus.Wram[1]==0x12&&(m.Bus.Wram[2]&128)!=0,"CPU/GSU mailbox terminal");Check(m.Bus.Cartridge!.Sram[0x22]==0x35,"Second GSU mailbox");
            Check(m.Bus.Wram[3]==3&&m.Bus.Cartridge.Sram[0x114]==0x40&&m.Bus.Cartridge.Sram[0x115]==0x40,"GSU PLOT/RPIX literal planar mailbox");
            Check(m.Bus.Ppu.Vram[0x100]==0x40&&m.Bus.Ppu.Vram[0x101]==0x40,"GSU planar row did not reach PPU VRAM");
            Check(dma.Count(a=>a.Origin==AccessOrigin.DmaA&&a.Direction=="read"&&a.Address is 0x700114 or 0x700115&&a.Value==0x40)==2&&dma.Count(a=>a.Origin==AccessOrigin.DmaB&&a.Direction=="write"&&a.Address is 0x2118 or 0x2119&&a.Value==0x40)==2,"Literal DMA A/B origin and nonzero transfer bytes");
            m.RunFrame();m.RunFrame();var image=m.Bus.Ppu.CompletedFrame!.Native.ToArray();m.RunFrame();Check(image.AsSpan().Contains((ushort)0x7fff)&&image.AsSpan().SequenceEqual(m.Bus.Ppu.CompletedFrame!.Native.Span),"GSU/DMA/PPU nonzero stable rendered field");
            report.Metadata[p.Pal?"palGsuWhitePixels":"ntscGsuWhitePixels"]=image.Count(pixel=>pixel==0x7fff);
            var final=StateSerializer.Capture(m);var source=Machine(p);
            long start=0;while(((SuperFxCartridgeHardware)source.Bus.Cartridge!.Hardware!).Processor.Instructions==0&&start<50000){start+=20;source.RunUntil(start);}Check(start<50000,"GSU never activated");var seen=new HashSet<int>();
            for(int t=0;t<=600;t+=2){source.RunUntil(start+t);seen.Add(((SuperFxCartridgeHardware)source.Bus.Cartridge!.Hardware!).Processor.Capture().Phase);var raw=StateSerializer.Capture(source);using var mem=new MemoryStream();using(var z=new GZipStream(mem,CompressionLevel.Fastest,true))z.Write(raw);mem.Position=0;var loaded=StateSerializer.Load(source.Bus.Cartridge!,mem);source.RunUntil(start+t+200);loaded.RunUntil(start+t+200);Check(StateSerializer.Capture(source).SequenceEqual(StateSerializer.Capture(loaded)),"2MC continuation"+t);source=Machine(p);}
            report.Metadata[p.Pal?"palSystemSnapshots":"ntscSystemSnapshots"]=301;Check(seen.Count>=2,"No active GSU phase coverage");
        });
        Test("Late malformed payload rejection retains distinct nonempty live SRAM",()=>{
            var m=Machine(HardwareProfile.Ntsc);Array.Fill(m.Bus.Cartridge!.Sram,(byte)0x5a);m.RunUntil(4000);var raw=StateSerializer.Capture(m);string text=System.Text.Encoding.UTF8.GetString(raw);Check(text.Contains("\"Version\":1"),"Missing chip payload marker");
            // Mutate the final envelope version, retaining every earlier state byte.
            byte[] needle=System.Text.Encoding.UTF8.GetBytes("{\"Version\":1,\"Kind\":\"SUPERFX\"");int at=raw.AsSpan().LastIndexOf(needle);Check(at>=0,"Missing exact chip envelope");raw[at+11]=(byte)'2';Array.Fill(m.Bus.Cartridge.Sram,(byte)0xa5);
            using var stream=new MemoryStream();using(var zip=new GZipStream(stream,CompressionLevel.Fastest,true))zip.Write(raw);stream.Position=0;bool rejected=false;try{StateSerializer.Load(m.Bus.Cartridge,stream);}catch(InvalidDataException){rejected=true;}
            Check(rejected&&m.Bus.Cartridge.Sram.All(b=>b==0xa5),"Rejected temporary machine modified live SRAM");
        });
        report.Save(prefix);return report.ExitCode;
    }
}
