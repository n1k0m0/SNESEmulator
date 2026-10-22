using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Testing;
using SNESEmulator.SaveStates;

static class ApuTests
{
    static void Check(bool b,string reason="M4 assertion failed"){if(!b)throw new Exception(reason);}
    static string Json<T>(T o)=>JsonSerializer.Serialize(o);
    static Apu Program(params byte[] code){var a=new Apu();a.Write(0xf1,0);a.Cpu.PC=0x200;code.CopyTo(a.Ram,0x200);return a;}
    static SnesSystem Upload(){var m=new SnesSystem();m.Insert(new Cartridge(SpcUploadRom.Create()));return m;}
    public static void Run(Action<string,Action> test,string directory)
    {
        test("M4 complete opcode sample both direct and resumable state bus",()=>{
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../testdata/spc700-sample"));
            var b=new SpcFlatBus();var c=new Spc700(b);List<SpcCycle> rows=[];c.CycleObserved=rows.Add;
            foreach(var f in Directory.GetFiles(root,"*.json").Where(f=>Path.GetFileName(f)!="manifest.json")){
                using var d=JsonDocument.Parse(File.ReadAllBytes(f));foreach(var v in d.RootElement.EnumerateArray()){
                    SpcVector.Load(c,b,v.GetProperty("initial"));rows.Clear();int guard=0;while(c.AdvanceCycle()){Check(++guard<20,"continuation runaway");}
                    Check(SpcVector.StateDifference(c,b,v.GetProperty("final"))==null,v.GetProperty("name")+" state");Check(SpcVector.ApprovedCycleDifference(rows,v)==null,v.GetProperty("name")+" cycles");
                }
            }
        });
        test("M4 IPL underlay ports directional clears auxiliary RAM and writeonly",()=>{
            var a=new Apu();byte first=a.Read(0xffc0);a.Write(0xffc0,0x5e);Check(a.Read(0xffc0)==first&&a.Ram[0xffc0]==0x5e);a.Write(0xf1,0);Check(a.Read(0xffc0)==0x5e);
            for(int i=0;i<4;i++){a.CpuToApu[i]=(byte)(10+i);a.Write((ushort)(0xf4+i),(byte)(20+i));Check(a.Read((ushort)(0xf4+i))==10+i&&a.ApuToCpu[i]==20+i&&a.Ram[0xf4+i]==20+i);}
            a.Write(0xf1,0x10);Check(a.CpuToApu.SequenceEqual(new byte[]{0,0,12,13})&&a.ApuToCpu[0]==20);a.CpuToApu[0]=9;a.Write(0xf1,0x30);Check(a.CpuToApu.All(v=>v==0));
            a.Write(0xf0,8);a.Write(0xf8,0x41);a.Write(0xf9,0x42);Check(a.Read(0xf8)==0x41&&a.Read(0xf9)==0x42&&a.Ram[0xf8]==0);
            foreach(ushort r in new ushort[]{0xf0,0xf1,0xfa,0xfb,0xfc})Check(a.Read(r)==0,"write-only zero");
        });
        test("M4 DSP address read mirror write suppress output and ENDX interface",()=>{
            var a=new Apu();a.Write(0xf2,0x0c);a.Write(0xf3,0x55);a.Write(0xf2,0x8c);Check(a.Read(0xf3)==0x55);a.Write(0xf3,0xaa);Check(a.Read(0xf3)==0x55&&a.Read(0xf2)==0x8c);
            a.Write(0xf2,8);a.Write(0xf3,0xff);Check(a.Read(0xf3)==0);a.DspRegisters[0x7c]=0xff;a.Write(0xf2,0x7c);a.Write(0xf3,0x55);Check(a.Read(0xf3)==0);
        });
        test("M4 source adjudicated indirect Y fixed electrical worksheet",()=>{
            var b=new SpcFlatBus();b.Ram[0x8000]=0x17;b.Ram[0x8001]=0x10;b.Ram[0x10]=0;b.Ram[0x11]=2;b.Ram[0x203]=0x42;
            var c=new Spc700(b){PC=0x8000,A=1,Y=3};List<SpcCycle> rows=[];c.CycleObserved=rows.Add;c.Step();
            SpcCycle[] expected=[new(0x8000,0x17,"read"),new(0x8001,0x10,"read"),new(0x10,0,"read"),new(0x11,2,"read"),new(null,null,"wait"),new(0x203,0x42,"read")];
            Check(rows.SequenceEqual(expected)&&c.A==0x43,"published pointer low/high then internal then data ordering");
        });
        test("M4 permanent immutable SPC seed regressions",()=>{
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../testdata/m4-regressions.json"))));var bus=new SpcFlatBus();var cpu=new Spc700(bus);List<SpcCycle> rows=[];cpu.CycleObserved=rows.Add;
            foreach(var v in doc.RootElement.GetProperty("cases").EnumerateArray()){SpcVector.Load(cpu,bus,v.GetProperty("initial"));rows.Clear();cpu.Step();Check(SpcVector.StateDifference(cpu,bus,v.GetProperty("final"))==null&&SpcVector.ApprovedCycleDifference(rows,v)==null,v.GetProperty("name").ToString());}
        });
        test("M4 TEST independent internal external wait selectors literal timing table",()=>{
            int[] externalEnd=[24,48,120,240];int[] internalEnd=[72,96,168,288];
            for(int selector=0;selector<4;selector++){
                var external=Program(0x00);external.Write(0xf0,(byte)(0x0a|(selector<<4)));List<ApuPhase> e=[];external.PhaseObserved=e.Add;external.RunUntil(externalEnd[selector]);Check(e[0].Crystal==externalEnd[selector],"external divider");
                var internalCycle=Program(0x2f,0);internalCycle.Write(0xf0,(byte)(0x0a|(selector<<6)));List<ApuPhase> i=[];internalCycle.PhaseObserved=i.Add;internalCycle.RunUntil(internalEnd[selector]);Check(i[2].Crystal==internalEnd[selector],"internal divider");
            }
        });
        test("M4 timers fixed boundaries target0 readclear overflow all enable edges",()=>{
            for(int i=0;i<3;i++){
                int period=i==2?16:128;var a=Program(0x2f,0xfe);a.Write((ushort)(0xfa+i),1);a.Write(0xf1,(byte)(1<<i));
                a.RunUntil(period*24-1);Check(a.Read((ushort)(0xfd+i))==0,"before timer boundary");a.RunUntil(period*24);Check(a.Read((ushort)(0xfd+i))==1&&a.Read((ushort)(0xfd+i))==0,"exact timer boundary + read clear");
                a.RunUntil(period*24*3);a.Write(0xf1,(byte)(1<<i));Check(a.Read((ushort)(0xfd+i))==2,"same enabled CONTROL must retain output, including T2");
                a.RunUntil(period*24*19);Check(a.Read((ushort)(0xfd+i))==0,"4bit overflow");a.Write(0xf1,0);a.Write(0xf1,(byte)(1<<i));Check(a.Read((ushort)(0xfd+i))==0,"rising enable clear");
                var zero=Program(0x2f,0xfe);zero.Write(0xf1,(byte)(1<<i));zero.RunUntil(period*24*256);Check(zero.Read((ushort)(0xfd+i))==1,"target zero means256");
            }
        });
        test("M4 timer scaler free runs disabled target wrap TEST gating P restriction",()=>{
            var a=Program(0x2f,0xfe);a.RunUntil(12*24);a.Write(0xfc,1);a.Write(0xf1,4);a.RunUntil(16*24);Check(a.Read(0xff)==1,"enable must preserve scaler phase");
            a.Write(0xfc,3);a.RunUntil(48*24);a.Write(0xfc,1);a.RunUntil(64*24);Check(a.Read(0xff)==0,"target below counter waits for wrap");a.RunUntil(16*24*258);Check(a.Read(0xff)==1,"postincrement byte comparator wrap");
            var gate=Program(0x2f,0xfe);gate.Write(0xfc,1);gate.Write(0xf1,4);gate.RunUntil(8*24);gate.Write(0xf0,0x0b);Check(gate.Read(0xff)==1,"TEST gating falling edge pulses timer");gate.RunUntil(64*24);Check(gate.Read(0xff)==0,"TEST disable blocks pulses");
            gate.Cpu.PSW|=32;gate.Write(0xf0,0x0a);Check(gate.Test==0x0b,"P bit inhibits TEST register write");
        });
        test("M4 RAM read latch endpoint CPUport half latch hold and dummy MMIO",()=>{
            var a=Program(0xe4,0xf4,0xc4,0xf5,0x2f,0xfa);a.CpuToApu[0]=0x31;var rows=new List<ApuPhase>();a.PhaseObserved=rows.Add;
            a.RunUntil(59);Check(rows.Count==2);a.RunUntil(60);Check(rows[^1].Crystal==60&&rows[^1].Cycle.Address==0xf4&&rows[^1].Cycle.Value==0x31);a.CpuToApu[0]=0x99;a.RunUntil(72);Check(a.Cpu.A==0x31&&rows.Any(r=>r.Phase=="read-complete"&&r.Crystal==72));
            var ram=Program(0xe4,0x10);ram.Ram[0x10]=0x20;ram.RunUntil(71);ram.Ram[0x10]=0x42;ram.RunUntil(72);Check(ram.Cpu.A==0x42,"RAM samples full cycle endpoint");
            var dummy=new Apu();dummy.Write(0xfc,0);dummy.Cpu.PC=0xfc;Check(dummy.Read(0xff)==15);dummy.Cpu.PC=0xfc; // NOP at write-only target read0, dummy readsFD and clears T0OUT.
            dummy.RunUntil(48);Check(dummy.Read(0xfd)==0,"NOP dummy read must clear timer");
        });
        test("M4 every crystal continuation against untouched continuous reference",()=>{
            byte[] code=[0xe4,0xff,0xc4,0xf4,0xab,0x10,0x2f,0xf8];var continuous=Program(code);continuous.Write(0xfc,1);continuous.Write(0xf1,4);continuous.RunUntil(1800);string expected=Json(continuous.Capture());
            for(int t=0;t<450;t++){
                var m=Program(code);m.Write(0xfc,1);m.Write(0xf1,4);m.RunUntil(t);var loaded=new Apu();loaded.Restore(m.Capture());loaded.RunUntil(1800);Check(Json(loaded.Capture())==expected,$"SPC snapshot tick{t}");
            }
        });
        test("M4 committed phase observer snapshot never repeats readclear or port hold",()=>{
            var continuous=Program(0xe4,0xff,0xc4,0xf4,0xe4,0xf5,0xc4,0xf6,0x2f,0xf4);continuous.CpuToApu[1]=0x42;continuous.Write(0xfc,1);continuous.Write(0xf1,4);continuous.RunUntil(1200);string expected=Json(continuous.Capture());
            var m=Program(0xe4,0xff,0xc4,0xf4,0xe4,0xf5,0xc4,0xf6,0x2f,0xf4);m.CpuToApu[1]=0x42;m.Write(0xfc,1);m.Write(0xf1,4);List<ApuState> states=[];m.PhaseObserved=_=>states.Add(m.Capture());m.RunUntil(600);
            foreach(var s in states){var loaded=new Apu();loaded.Restore(s);loaded.RunUntil(1200);Check(Json(loaded.Capture())==expected,$"observer {s.Crystal}");}
        });
        test("M4 original SNES ROM real own IPL upload RAM program port pingpong",()=>{
            var m=Upload();m.RunUntil(300000);Check(m.Cpu.Stopped&&m.Bus.Wram[0x1ff0]==0xa5,"SNES upload completion");Check(m.Bus.Apu.Ram[0x300]==0x5a&&m.Bus.Apu.ApuToCpu[0]==0xa5&&m.Bus.Apu.ApuToCpu[1]==0xc3,"SPC executed uploaded program");Check(m.Bus.Apu.Ram.AsSpan(0x200,SpcUploadRom.Program.Length).SequenceEqual(SpcUploadRom.Program));
        });
        test("M4 own IPL CPU multiblock upload index and page wraps command zero avoidance",()=>{
            var m=new SnesSystem();m.Insert(new Cartridge(SpcUploadRom.Create(true)));m.RunUntil(2000000);
            Check(m.Cpu.Stopped&&m.Bus.Wram[0x1ff0]==0xa5&&m.Bus.Apu.Ram[0x300]==0x5a,"stress upload completes");
            Check(m.Bus.Apu.Ram.AsSpan(0x04f0,520).SequenceEqual(SpcUploadRom.StressData)&&m.Bus.Apu.Ram.AsSpan(0x08ff,257).SequenceEqual(SpcUploadRom.WrapData)&&m.Bus.Apu.Ram.AsSpan(0x0b80,255).SequenceEqual(SpcUploadRom.ZeroCommandData),"all1048 uploaded payload bytes retained");
        });
        test("M4 every master clock savestate active CPU SPC ports matches continuous",()=>{
            var reference=Upload();reference.RunUntil(4000);string referencePath=Path.Combine(directory,"m4-master-reference");StateSerializer.Save(reference,referencePath);byte[] expected=File.ReadAllBytes(referencePath);
            for(int t=0;t<250;t++){
                var m=Upload();m.RunUntil(t);string path=Path.Combine(directory,"m4-clock.sav");StateSerializer.Save(m,path);var loaded=StateSerializer.Load(m.Bus.Cartridge!,path);loaded.RunUntil(4000);StateSerializer.Save(loaded,path);Check(File.ReadAllBytes(path).SequenceEqual(expected),$"full system masterclock{t}");
            }
        });
        test("M4 APU observers exactly before on after typed refresh and line endpoints",()=>{
            var p=HardwareProfile.Ntsc with{ApuHzNumerator=HardwareProfile.Ntsc.MasterHzNumerator*24,ApuHzDenominator=HardwareProfile.Ntsc.MasterHzDenominator};
            SnesSystem Make(){var m=new SnesSystem(p);m.Insert(new Cartridge(SpcUploadRom.Create()));return m;}
            var reference=Make();reference.RunUntil(3000);string final=Path.Combine(directory,"m4-tie-reference");StateSerializer.Save(reference,final);byte[] expected=File.ReadAllBytes(final);
            var observed=Make();List<string> paths=[];HashSet<long> seen=[];observed.Bus.Apu.PhaseObserved=_=>{
                long t=observed.Clock.Now;if(t is 537 or 538 or 539 or 577 or 578 or 579 or 1363 or 1364 or 1365&&seen.Add(t)){string f=Path.Combine(directory,"m4-tie-"+t);StateSerializer.Save(observed,f);paths.Add(f);}
            };observed.RunUntil(1400);Check(paths.Count>=8,"must exercise actual event endpoints");
            foreach(var path in paths){var copy=StateSerializer.Load(observed.Bus.Cartridge!,path);copy.RunUntil(3000);StateSerializer.Save(copy,final);Check(File.ReadAllBytes(final).SequenceEqual(expected),path);}
        });
        test("M4 targeted APU observer snapshots across DMA IRQ Vblank NMI typed events",()=>{
            var p=HardwareProfile.Ntsc with{ApuHzNumerator=HardwareProfile.Ntsc.MasterHzNumerator*24,ApuHzDenominator=HardwareProfile.Ntsc.MasterHzDenominator};
            SnesSystem Make(){
                var m=new SnesSystem(p);m.Insert(new Cartridge(DiagnosticRom.Create()));m.Cpu.Stopped=true;var a=m.Bus.Apu;a.Write(0xf1,0);a.Cpu.PC=0x200;new byte[]{0x00,0x2f,0xfd}.CopyTo(a.Ram,0x200);
                m.Bus.Write(0x4207,100);m.Bus.Write(0x4208,0);m.Bus.Write(0x4200,0x90);
                foreach(var (r,v) in new[]{(0,0),(1,0x80),(2,0),(3,0x90),(4,0),(5,3),(6,0)})m.Bus.Write(0x4300+r,(byte)v);m.Bus.WramAddress=0x4000;m.Bus.Write(0x420b,1);return m;
            }
            const int end=307010;var reference=Make();reference.RunUntil(end);string referencePath=Path.Combine(directory,"m4-target-reference");StateSerializer.Save(reference,referencePath);byte[] expected=File.ReadAllBytes(referencePath);
            var times=new HashSet<long>{16,17,18,24,25,26,411,412,413,537,538,539,1363,1364,1365,306899,306900,306901,306902,306903};var observed=Make();List<string> paths=[];
            observed.Bus.Apu.PhaseObserved=_=>{if(times.Remove(observed.Clock.Now)){string path=Path.Combine(directory,"m4-target-"+observed.Clock.Now);StateSerializer.Save(observed,path);paths.Add(path);}};observed.RunUntil(306904);Check(times.Count==0,"all20 targeted boundaries must actually be snapshotted");
            foreach(var path in paths){var copy=StateSerializer.Load(observed.Bus.Cartridge!,path);copy.RunUntil(end);StateSerializer.Save(copy,referencePath);Check(File.ReadAllBytes(referencePath).SequenceEqual(expected),path);}
            Check(reference.Bus.CpuIo.State.NmiOutput&&reference.Bus.WramAddress==0x4003,"real NMI and DMA effects present");
        });
        test("M4 public APU observer system saves retain future due events CPU DMA raster",()=>{
            var reference=Upload();reference.RunUntil(3000);string referencePath=Path.Combine(directory,"m4-observer-reference");StateSerializer.Save(reference,referencePath);byte[] wanted=File.ReadAllBytes(referencePath);
            var observed=Upload();List<string> paths=[];observed.Bus.Apu.PhaseObserved=p=>{
                if(paths.Count<50&&observed.Clock.Now<1450){string path=Path.Combine(directory,"m4-public-"+paths.Count);StateSerializer.Save(observed,path);paths.Add(path);}
            };observed.RunUntil(1600);
            foreach(string path in paths){var loaded=StateSerializer.Load(observed.Bus.Cartridge!,path);loaded.RunUntil(3000);string result=Path.Combine(directory,"m4-public-tail");StateSerializer.Save(loaded,result);Check(File.ReadAllBytes(result).SequenceEqual(wanted),"public observer "+path);}
        });
        test("M4 reset sleep stop firmware identity and ratios NTSC PAL chunks",()=>{
            var stop=Program(0xff);stop.RunUntil(10000);Check(stop.Cpu.Stopped&&stop.Cycles>7);var sleep=Program(0xef);sleep.Write(0xfc,1);sleep.Write(0xf1,4);sleep.RunUntil(1600);Check(sleep.Cpu.Sleeping&&sleep.Read(0xff)>0,"sleep keeps timer cycling");
            var a=new Apu();a.Ram[0x1234]=0x66;a.Write(0xfa,7);a.Reset(false);Check(a.Ram[0x1234]==0x66&&a.Capture().Timers[0].Target==7&&a.Read(0xfd)==0);a.Reset(true);Check(a.Ram[0x1234]==0&&a.Read(0xfd)==15);
            byte[] other=OwnIpl.Bytes;other[0]^=1;try{new Apu(other).Restore(a.Capture());throw new Exception("wrong IPL accepted");}catch(InvalidDataException){}
            foreach(var p in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile}){var whole=new SnesSystem(p);whole.Insert(new Cartridge(SpcUploadRom.Create()));whole.RunUntil(20000);var chunk=new SnesSystem(p);chunk.Insert(new Cartridge(SpcUploadRom.Create()));for(int t=1;t<=20000;t++)chunk.RunUntil(t);Check(Json(whole.Bus.Apu.Capture())==Json(chunk.Bus.Apu.Capture()),p.Id+" chunking");Check(whole.Clock.ApuTicks==whole.Bus.Apu.Crystal,"crystal ratio");}
        });
        test("M4 own IPL reentry restores stack clears dirty page and advertises through code",()=>{
            var a=new Apu();Array.Fill(a.Ram,(byte)0xcc,0,240);a.Cpu.Restore(new(0xffc0,0x77,0x11,0x55,0x12,0x20,false,false,0));a.RunUntil(100000);
            Check(a.Cpu.SP==0xef&&(a.Cpu.PSW&32)==0&&a.Ram.AsSpan(1,239).ToArray().All(v=>v==0)&&a.ApuToCpu[0]==0xaa&&a.ApuToCpu[1]==0xbb,"own IPL reentry is real SPC code");
        });
        test("M4 savestate v5 rejects formats1 through4 and foreign IPL identity",()=>{
            var m=Upload();m.RunUntil(101);string valid=Path.Combine(directory,"m4-version-valid");StateSerializer.Save(m,valid);
            byte[] data;using(var f=File.OpenRead(valid))using(var zip=new System.IO.Compression.GZipStream(f,System.IO.Compression.CompressionMode.Decompress))using(var bytes=new MemoryStream()){zip.CopyTo(bytes);data=bytes.ToArray();}
            for(int version=1;version<=4;version++){byte[] modified=data.ToArray();BitConverter.GetBytes(version).CopyTo(modified,8);string old=Path.Combine(directory,"m4-old"+version);using(var f=File.Create(old))using(var zip=new System.IO.Compression.GZipStream(f,System.IO.Compression.CompressionLevel.Fastest))zip.Write(modified);bool rejected=false;try{StateSerializer.Load(m.Bus.Cartridge!,old);}catch(InvalidDataException){rejected=true;}Check(rejected,"old format"+version);}
            string firmware=Path.Combine(directory,"m4-foreign-ipl");byte[] altered=OwnIpl.Bytes;altered[0]^=1;File.WriteAllBytes(firmware,altered);string? previous=Environment.GetEnvironmentVariable("SNESEMULATOR_IPL");
            try{Environment.SetEnvironmentVariable("SNESEMULATOR_IPL",firmware);bool rejected=false;try{StateSerializer.Load(m.Bus.Cartridge!,valid);}catch(InvalidDataException e){rejected=e.Message.Contains("firmware identity");}Check(rejected,"own IPL state with different configured firmware");}finally{Environment.SetEnvironmentVariable("SNESEMULATOR_IPL",previous);}
        });
        test("M4 negative comparator mutations and hardware adjudication cannot hide errors",()=>{
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../testdata/spc700-sample"));using var d=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"17.json")));var v=d.RootElement[0];var b=new SpcFlatBus();var c=new Spc700(b);List<SpcCycle> rows=[];c.CycleObserved=rows.Add;SpcVector.Load(c,b,v.GetProperty("initial"));c.Step();Check(SpcVector.CycleDifference(rows,v.GetProperty("cycles"))!=null&&SpcVector.ApprovedCycleDifference(rows,v)==null,"raw mismatch explicitly adjudicated");
            foreach(string mutation in new[]{"address","value","direction","wait","missing","extra"}){var bad=rows.ToList();switch(mutation){case "address":bad[2]=bad[2] with{Address=(ushort)(bad[2].Address!^1)};break;case "value":bad[2]=bad[2] with{Value=(byte)(bad[2].Value!^1)};break;case "direction":bad[2]=bad[2] with{Direction="write"};break;case "wait":(bad[2],bad[4])=(bad[4],bad[2]);break;case "missing":bad.RemoveAt(5);break;case "extra":bad.Add(rows[0]);break;}Check(SpcVector.ApprovedCycleDifference(bad,v)!=null,mutation);}
            c.A^=1;Check(SpcVector.StateDifference(c,b,v.GetProperty("final"))?.StartsWith("a:")==true,"register mutation");c.A^=1;b.Ram[0x2000]^=1;b.BeforeWrite[0x2000]=(byte)(b.Ram[0x2000]^1);Check(SpcVector.StateDifference(c,b,v.GetProperty("final"))=="unexpected RAM write 2000","RAM mutation");
        });
    }
}
