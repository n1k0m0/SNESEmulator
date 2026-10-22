using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Testing;

static class DspTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static string Json<T>(T value)=>JsonSerializer.Serialize(value);
    static Apu Stopped(){var a=new Apu();a.Write(0xf1,0);a.Cpu.PC=0x200;a.Cpu.Stopped=true;return a;}
    static Apu Tone(){var a=Stopped();byte[] code=AudioDiagnosticRom.SpcProgram();code.CopyTo(a.Ram,0x200);a.Cpu.Stopped=false;return a;}
    static byte[] Inflate(string path){using var f=File.OpenRead(path);using var g=new GZipStream(f,CompressionMode.Decompress);using var m=new MemoryStream();g.CopyTo(m);return m.ToArray();}
    public static Dictionary<string,object> Evidence {get;}=new();
    public static void Run(Action<string,Action> test,string directory,bool longOnly=false)
    {
        var register=test;test=(name,action)=>{if(longOnly==name.Contains("bounded long run"))register(name,action);};
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../testdata/dsp-reference"));
        test("M6 immutable independent LGPL DSP raw stereo RAM and register corpus",()=>{
            using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"manifest.json")));
            foreach(var e in manifest.RootElement.GetProperty("files").EnumerateArray())Check(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,e.GetProperty("file").GetString()!))))==e.GetProperty("sha256").GetString(),"reference fixture hash");
            long samples=0,clocks=0;int active=0;
            for(int caseId=0;caseId<manifest.RootElement.GetProperty("cases").GetInt32();caseId++){
                byte[] input=Inflate(Path.Combine(root,$"{caseId:00}.input.gz")),expected=Inflate(Path.Combine(root,$"{caseId:00}.expected.gz"));
                using var r=new BinaryReader(new MemoryStream(input));int count=r.ReadInt32(),events=r.ReadInt32();var a=new Apu();r.ReadBytes(65536).CopyTo(a.Ram,0);r.ReadBytes(128).CopyTo(a.DspRegisters,0);
                a.Dsp.Restore(new(){Directory=a.DspRegisters[0x5d],EchoPage=a.DspRegisters[0x6d],NewKeys=a.DspRegisters[0x4c]},0);
                a.Write(0xf1,0);a.Cpu.PC=0x200;a.Cpu.Stopped=true;
                // Disable CONTROL through the public port above only changes RAM F1;
                // restore its independently supplied underlay before comparing.
                a.Ram[0xf1]=input[8+0xf1];
                List<StereoSample> all=[];List<byte> observations=[];
                if(caseId==68)a.DspPhaseObserved=row=>{
                    observations.AddRange(a.DspRegisters);ulong hash=14695981039346656037;foreach(byte datum in a.Ram){hash^=datum;hash=unchecked(hash*1099511628211);}
                    observations.AddRange(BitConverter.GetBytes(hash));
                };
                void RunTo(long target){while(a.Crystal<target){a.RunUntil(Math.Min(target,a.Crystal+24576));all.AddRange(a.Dsp.DrainSamples());}}
                for(int e=0;e<events;e++){
                    int at=r.ReadInt32(),address=r.ReadInt32(),kind=r.ReadInt32(),value=r.ReadInt32();RunTo(at*24L);
                    if(kind==0)a.Dsp.WriteRegister(address,(byte)value);else if(kind==1)a.Ram[address]=(byte)value;
                    else {all.Clear();a.Dsp.Reset(false);var reset=a.Dsp.Capture();reset.NextCrystal=at*24L+24;a.Dsp.Restore(reset,a.Crystal);}
                }
                RunTo(count*24L);var actual=all.ToArray();
                using var er=new BinaryReader(new MemoryStream(expected));int scalarCount=er.ReadInt32();Check(actual.Length*2==scalarCount,$"case{caseId}: sample count");
                byte[] pcm=er.ReadBytes(scalarCount*2);string? difference=StereoOracle.Difference(actual,pcm,672+(caseId==69?8192*24L:0));
                Check(difference==null,$"case{caseId}: {difference}; reproduce --dsp-only; voice envelopes: {string.Join(',',a.Dsp.Capture().Voices.Select(v=>v.Envelope))}");
                active+=actual.Count(s=>s.Left!=0||s.Right!=0);
                byte[] ram=er.ReadBytes(65536),regs=er.ReadBytes(128);int different=Enumerable.Range(0,65536).FirstOrDefault(i=>ram[i]!=a.Ram[i],-1);
                Check(different<0,$"case{caseId} RAM first{different:X4}");different=Enumerable.Range(0,128).FirstOrDefault(i=>regs[i]!=a.DspRegisters[i],-1);Check(different<0,$"case{caseId} register first{different:X2}");
                if(caseId==68){byte[] trace=er.ReadBytes((int)(er.BaseStream.Length-er.BaseStream.Position));Check(trace.Length==count*136&&observations.Count==trace.Length,"exact per-clock observation count");int phaseDiff=Enumerable.Range(0,trace.Length).FirstOrDefault(i=>trace[i]!=observations[i],-1);Check(phaseDiff<0,$"first phase difference clock{phaseDiff/136} item{phaseDiff%136}; expected register/RAM observations");Evidence["perClockRegisterObservations"]=count*128;Evidence["perClockRamDigests"]=count;}
                samples+=actual.Length;clocks+=count;
            }
            Check(active>1000,"corpus must produce actual sound");Evidence["referenceCommit"]=manifest.RootElement.GetProperty("commit").GetString()!;Evidence["samplesCompared"]=samples;Evidence["nativePhasesExecuted"]=clocks;Evidence["nonzeroSamples"]=active;
        });
        test("M6 BRR independent literal signed rounding invalid shifts filters saturation and wrap",()=>{
            (int n,int h,int p,int q,int expected)[] rows=[(7,0,0,0,6),(-1,0,0,0,-2),(7,0xc0,0,0,28672),(-8,0xc0,0,0,-32768),(7,0xd0,0,0,0),(-1,0xf0,0,0,-4096),(0,4,16384,0,15360),(0,4,-16384,0,-15360),(0,8,16384,8192,23552),(0,12,16384,8192,22784),(0,8,32766,-32768,-2),(0,8,-32768,32766,0)];
            foreach(var row in rows)Check(SnesDsp.DecodeNibble(row.n,row.h,row.p,row.q)==row.expected,$"BRR literal{row}");
        });
        test("M6 Gaussian independent coefficient worksheet three-term wrap and even truncation",()=>{
            Check(SnesDsp.Interpolate([32766,0,0,0],0)==5918,"coefficient370 arithmetic");
            Check(SnesDsp.Interpolate([-32768,-32768,-32768,-32768],0)==32752,"sum2049 gives negative overflow before fourth term");
            Check(SnesDsp.Interpolate([0,0,0,0],255)==0,"silence");
            Check(SnesDsp.Interpolate([32766,0,0,0],0)!=5919,"low-bit mutation");
        });
        test("M6 source literal ADSR GAIN release and unsigned hidden bent gain",()=>{
            (int mode,int env,int hidden,int adsr,int data,int gain,int expected,int nextMode)[] rows=[
                (1,0,0,0x8f,0,0,1024,1),(1,1024,1024,0x8f,0,0,2047,2),
                (2,2047,0,0xf0,0,0,2039,2),(3,256,0,0x80,31,0,255,3),
                (3,0,0,0,0,0x7f,2032,3),(3,16,0,0,0,0x9f,0,3),
                (3,256,0,0,0,0xbf,255,3),(3,1504,1504,0,0,0xdf,1536,3),
                (3,1536,1536,0,0,0xff,1544,3),(3,0,-32,0,0,0xff,8,3),
                (3,100,0,0,0,0xc0,100,3),(0,7,0,0,0,127,0,0)];
            foreach(var row in rows){
                var a=Stopped();a.DspRegisters[0x6c]=0x20;a.DspRegisters[6]=(byte)row.data;a.DspRegisters[7]=(byte)row.gain;
                var s=new DspState{Phase=30,Adsr=row.adsr,Counter=1,KeyWindow=false};var voice=s.Voices[0];voice.EnvelopeMode=row.mode;voice.Envelope=row.env;voice.HiddenEnvelope=row.hidden;
                a.Dsp.Restore(s,0);a.RunUntil(24);var actual=a.Dsp.Capture().Voices[0];Check(actual.Envelope==row.expected&&actual.EnvelopeMode==row.nextMode,$"envelope literal{row}: {actual.Envelope}/{actual.EnvelopeMode}");
            }
        });
        test("M6 independent full rate periods offsets and noise literal sequence",()=>{
            int[] periods=[30721,2048,1536,1280,1024,768,640,512,384,320,256,192,160,128,96,80,64,48,40,32,24,20,16,12,10,8,6,5,4,3,2,1];
            for(int rate=0;rate<32;rate++){
                Check(SnesDsp.RatePeriod(rate)==periods[rate],"period table");int fires=Enumerable.Range(0,30720).Count(n=>SnesDsp.RateFires(n,rate));Check(fires==(rate==0?0:30720/periods[rate]),$"whole counter period rate{rate}");
            }
            Check(SnesDsp.RateFires(496,2)&&!SnesDsp.RateFires(495,2)&&SnesDsp.RateFires(744,3),"source offsets1040/536");
            var a=Stopped();a.DspRegisters[0x6c]=0x3f;
            int[] expected=[0x2000,0x1000,0x800,0x400,0x200,0x100,0x80,0x40,0x20,0x10,8,4,2,0x4001,0x6000];
            foreach(int noise in expected){long target=a.Dsp.NextBoundary+(30-a.Dsp.Phase+32)%32*24;a.RunUntil(target);Check(a.Dsp.Capture().Noise==noise,$"noise at{target}");}
        });
        test("M6 split left right echo write gating and overlapping shared ARAM",()=>{
            foreach(bool initiallyDisabled in new[]{false,true}){
                var a=Stopped();a.DspRegisters[0x6c]=(byte)(initiallyDisabled?32:0);a.Ram[0xfffc]=0xaa;a.Ram[0xfffe]=0xbb;
                var s=new DspState{Phase=28,EchoAddress=0xfffc,EchoSend=[0x1234,-0x1234]};a.Dsp.Restore(s,0);
                a.RunUntil(24);a.DspRegisters[0x6c]=(byte)(initiallyDisabled?0:32);a.RunUntil(48);
                Check(a.Ram[0xfffc]==(initiallyDisabled?0xaa:0x34),"left flag sampled at28");a.RunUntil(72);
                Check(a.Ram[0xfffe]==(initiallyDisabled?0xcc:0xbb),"right flag independently sampled at29");
            }
        });
        test("M6 literal FIR wrap before eighth tap feedback and signed mix intermediates",()=>{
            var a=Stopped();a.DspRegisters[0x6c]=0x20;a.DspRegisters[0x6f]=a.DspRegisters[0x7f]=127;
            var s=new DspState{Phase=25,Filter=[30000,-30000]};s.EchoHistory[14]=s.EchoHistory[0]=16383;s.EchoHistory[15]=s.EchoHistory[1]=-16384;a.Dsp.Restore(s,0);a.RunUntil(24);
            Check(a.Dsp.Capture().Filter.SequenceEqual(new[]{29484,-29488}),"16-bit wrap of seven FIR taps before clamped eighth tap");
            a=Stopped();a.DspRegisters[0x6c]=0x20;a.DspRegisters[0x0c]=a.DspRegisters[0x0d]=128;s=new(){Phase=26,Main=[-32768,0],Filter=[-32768,0]};a.Dsp.Restore(s,0);a.RunUntil(24);
            Check(a.Dsp.Capture().Main[0]==-32768&&a.Dsp.Capture().EchoSend[0]==-32768,"signed volume -128 intermediate wraps before final add");
            a=Stopped();a.DspRegisters[0]=128;s=new(){Phase=31,Output=-32768,EchoEnable=1};a.Dsp.Restore(s,0);a.RunUntil(24);Check(a.Dsp.Capture().Main[0]==32767&&a.Dsp.Capture().EchoSend[0]==32767,"per voice saturation before master mix");
        });
        test("M6 published phase RAM worksheet voice operations and negative trace mutations",()=>{
            var a=Stopped();List<DspPhase> rows=[];a.DspPhaseObserved=rows.Add;a.RunUntil(768);
            int[][] reads=[[2,3],[1,0]];for(int i=0;i<2;i++)Check(rows[i].Memory.Select(m=>m.Address).SequenceEqual(reads[i])&&rows[i].Memory.All(m=>!m.Write&&m.Value==0),$"phase{i} voice{(i==0?1:1)} directory/BRR byte order");
            Check(rows[22].Memory.Select(m=>m.Address).SequenceEqual(new[]{0,1})&&rows[23].Memory.Select(m=>m.Address).SequenceEqual(new[]{2,3}),"separate stereo echo RAM read phases");
            Check(rows[0].VoiceOperations.SequenceEqual(new[]{5,102})&&rows[1].VoiceOperations.SequenceEqual(new[]{6,103}),"voice and stage diagnostics");
            var correct=rows[0].Memory;bool Matches(DspAccess[] actual)=>actual.SequenceEqual(new[]{new DspAccess(2,0,false),new DspAccess(3,0,false)});
            Check(Matches(correct),"literal trace comparator positive");foreach(var mutation in new[]{new[]{correct[1],correct[0]},new[]{correct[0] with{Address=4},correct[1]},new[]{correct[0] with{Write=true},correct[1]},new[]{correct[0] with{Value=1},correct[1]}})Check(!Matches(mutation),"negative DSP phase RAM trace mutation");
            Evidence["negativePhaseTraceMutations"]=4;
        });
        test("M6 first sample native timestamp fixed crystal despite TEST sleep and stop",()=>{
            for(int mode=0;mode<4;mode++){
                var a=Stopped();a.Write(0xf0,(byte)(0x0a|mode<<4|mode<<6));a.Cpu.Sleeping=mode%2==0;a.RunUntil(671);Check(a.Dsp.SampleCount==0,"pre first DAC pair");a.RunUntil(672);Check(a.Dsp.SampleCount==1,"first pair");a.RunUntil(768*100L);
                Check(a.Dsp.SampleCount==100&&a.Dsp.DrainSamples()[^1].Crystal==672+99*768L,"native oscillator independent of wait states");
            }
        });
        test("M6 actual original CPU IPL SPC BRR stereo diagnostic and native output",()=>{
            var m=new SnesSystem();m.Insert(new Cartridge(AudioDiagnosticRom.Create()));m.RunUntil(1500000);var samples=m.Bus.Apu.Dsp.DrainSamples();
            Check(m.Cpu.Stopped&&m.Bus.Wram[0x1ff0]==0xa5&&m.Bus.Apu.Ram[0x300]==0x5a,"real CPU/SPC upload and executed reply");
            Check(samples.Count(s=>s.Left!=0)>100&&samples.Count(s=>s.Right!=0)>100&&samples.Count(s=>s.Left!=s.Right)>100,"two differently pitched BRR voices actually mixed as stereo");
            Check(Convert.ToHexStringLower(SHA256.HashData(SpcUploadRom.Create()))=="60384889500eb3037ed4912cacbc7cdb370ce89431ea37021bac35283b76723f"&&Convert.ToHexStringLower(SHA256.HashData(SpcUploadRom.Create(true)))=="93ca50b7e9805db099a77cd9136deb9b40e4f8a983636c2165d7d6835b1c9e05","both existing M4 upload ROMs remain byte-identical to independently compiled2dc8cf generator");
            Evidence["audioDiagnosticRomSha256"]=Convert.ToHexStringLower(SHA256.HashData(AudioDiagnosticRom.Create()));
        });
        test("M6 self-authored standard SPC snapshot matches independent native stereo",()=>{
            byte[] source=Inflate(Path.Combine(root,"70.input.gz")),expected=Inflate(Path.Combine(root,"70.expected.gz")),spc=new byte[0x10200];
            "SNES-SPC700 Sound File Data v0.30"u8.CopyTo(spc);spc[0x21]=spc[0x22]=26;spc[0x23]=26;spc[0x24]=30;spc[0x26]=2;spc[0x2b]=0xef;
            source.AsSpan(8,65536).CopyTo(spc.AsSpan(0x100));source.AsSpan(8+65536,128).CopyTo(spc.AsSpan(0x10100));
            var a=new Apu();a.LoadLaboratorySpcSnapshot(spc);a.RunUntil(BitConverter.ToInt32(source)*24L);var actual=a.Dsp.DrainSamples();
            int count=BitConverter.ToInt32(expected);Check(a.Cpu.Stopped&&StereoOracle.Difference(actual,expected.AsSpan(4,count*2))==null,"executed STOP snapshot and independent raw PCM");
            Check(a.Ram.SequenceEqual(expected.AsSpan(4+count*2,65536).ToArray()),"snapshot final shared ARAM");
            Check(a.CpuToApu.SequenceEqual(spc.AsSpan(0x1f4,4).ToArray())&&a.ApuToCpu.SequenceEqual(spc.AsSpan(0x1f4,4).ToArray())&&a.Test==0x0a,"explicit laboratory port/TEST initialization");
            byte[] ambiguous=spc.ToArray();ambiguous[0x1f1]=0x80;bool unsupported=false;try{a.LoadLaboratorySpcSnapshot(ambiguous);}catch(InvalidDataException){unsupported=true;}Check(unsupported,"ambiguous IPL-overlay SPC unsupported");
            Evidence["selfAuthoredSpcSnapshotSha256"]=Convert.ToHexStringLower(SHA256.HashData(spc));
            bool rejected=false;spc[0]=0;try{a.LoadLaboratorySpcSnapshot(spc);}catch(InvalidDataException){rejected=true;}Check(rejected,"bad SPC rejected before reset");
        });
        test("M6 every crystal DSP SPC register and echo collision continuation against uninterrupted",()=>{
            var setup=Tone();setup.RunUntil(50000);var start=setup.Capture();
            // Executed SPC writes F2/F3 then overlapping echo RAM; all endpoints are real cycles.
            byte[] program=[0x8f,0x6c,0xf2,0x8f,0x00,0xf3,0x8f,0x4c,0xf2,0x8f,3,0xf3,0x8f,0x7f,0xf2,0xe4,0xf3,0xc5,0x00,0x80,0x2f,0xe9];
            program.CopyTo(start.Ram,0x400);start=start with{Cpu=start.Cpu with{PC=0x400,Stopped=false},Continuation=null,CycleActive=false,Holding=false,Hold=0};
            var reference=new Apu();reference.Restore(start);reference.RunUntil(53000);string expected=Json(reference.Capture());var native=reference.Dsp.DrainSamples();
            int checkpoints=0;
            for(long tick=50000;tick<50768;tick++){
                var a=new Apu();a.Restore(start);a.RunUntil(tick);var loaded=new Apu();loaded.Restore(a.Capture());loaded.RunUntil(53000);Check(Json(loaded.Capture())==expected,$"DSP/SPC crystal{tick}");
                var future=native.Where(s=>s.Crystal>tick).ToArray();Check(loaded.Dsp.DrainSamples().SequenceEqual(future),$"native continuation{tick}");checkpoints++;
            }
            Evidence["everyCrystalContinuations"]=checkpoints;
        });
        test("M6 committed DSP observer snapshots include same-clock SPC port effects",()=>{
            var setup=Tone();setup.RunUntil(50000);var start=setup.Capture();var reference=new Apu();reference.Restore(start);reference.RunUntil(53000);string expected=Json(reference.Capture());
            List<ApuState> states=[];var a=new Apu();a.Restore(start);a.DspPhaseObserved=_=>states.Add(a.Capture());a.RunUntil(50768);
            foreach(var snapshot in states){var copy=new Apu();copy.Restore(snapshot);copy.RunUntil(53000);Check(Json(copy.Capture())==expected,$"committed DSP observer{snapshot.Crystal}");}
            Check(states.Count==32,"one sample has32 committed DSP phases");
        });
        test("M6 full console every master clock audio savestate against uninterrupted reference",()=>{
            var setup=new SnesSystem();setup.Insert(new Cartridge(AudioDiagnosticRom.Create()));setup.RunUntil(600000);string start=Path.Combine(directory,"m6-start");SNESEmulator.SaveStates.StateSerializer.Save(setup,start);
            var reference=setup;reference.Bus.Apu.Dsp.ClearPresentation();reference.RunUntil(602000);string output=Path.Combine(directory,"m6-ref");SNESEmulator.SaveStates.StateSerializer.Save(reference,output);byte[] expected=File.ReadAllBytes(output);var native=reference.Bus.Apu.Dsp.DrainSamples();
            int count=0;for(long tick=600000;tick<600096;tick++){
                var m=SNESEmulator.SaveStates.StateSerializer.Load(setup.Bus.Cartridge!,start);m.RunUntil(tick);string current=Path.Combine(directory,"m6-current");SNESEmulator.SaveStates.StateSerializer.Save(m,current);
                var copy=SNESEmulator.SaveStates.StateSerializer.Load(m.Bus.Cartridge!,current);copy.RunUntil(602000);SNESEmulator.SaveStates.StateSerializer.Save(copy,current);Check(File.ReadAllBytes(current).SequenceEqual(expected),$"full console DSP clock{tick}");Check(copy.Bus.Apu.Dsp.DrainSamples().SequenceEqual(native.Where(s=>s.Crystal>m.Bus.Apu.Crystal)),$"full console native audio{tick}");count++;
            }
            Evidence["everyMasterClockAudioStates"]=count;
        });
        test("M6 public DSP observer full console continuation DMA IRQ PPU typed event ties",()=>{
            int snapshots=0;
            foreach(var profile in new[]{HardwareProfile.Ntsc,HardwareProfile.Ntsc with{Id="SYNTHETIC-24-crystal-per-master-DSP-observer",ApuHzNumerator=HardwareProfile.Ntsc.MasterHzNumerator*24,ApuHzDenominator=HardwareProfile.Ntsc.MasterHzDenominator}}){
                SnesSystem Setup(){
                    var m=new SnesSystem(profile);m.Insert(new Cartridge(AudioDiagnosticRom.Create()));m.RunUntil(600000);
                    // Real SPC MMIO and echo overlap, while the outer console services DMA/IRQ.
                    byte[] code=[0x8f,0x6c,0xf2,0x8f,0x00,0xf3,0x8f,0x4c,0xf2,0x8f,3,0xf3,0xc5,0,0x80,0x2f,0xef];
                    var apu=m.Bus.Apu.Capture();code.CopyTo(apu.Ram,0x400);apu=apu with{Cpu=apu.Cpu with{PC=0x400,Stopped=false},Continuation=null,CycleActive=false,Holding=false,Hold=0};m.Bus.Apu.Restore(apu);
                    for(int i=0;i<256;i++)m.Bus.Wram[i]=(byte)i;
                    m.Bus.Write(0x4300,0);m.Bus.Write(0x4301,0x22);m.Bus.Write(0x4302,0);m.Bus.Write(0x4303,0);m.Bus.Write(0x4304,0x7e);m.Bus.Write(0x4305,0);m.Bus.Write(0x4306,1);m.Bus.Write(0x420b,1);
                    m.Bus.Write(0x4207,100);m.Bus.Write(0x4208,0);m.Bus.Write(0x4200,0x10);m.Clock.Schedule(600200,HardwareEventKind.CpuStall,8,-3);m.Bus.Apu.Dsp.ClearPresentation();return m;
                }
                var reference=Setup();reference.RunUntil(602000);string expectedPath=Path.Combine(directory,"m6-observer-ref");SNESEmulator.SaveStates.StateSerializer.Save(reference,expectedPath);byte[] expected=File.ReadAllBytes(expectedPath);var samples=reference.Bus.Apu.Dsp.DrainSamples();
                var observed=Setup();List<(string Path,long Crystal)> paths=[];
                observed.Bus.Apu.DspPhaseObserved=row=>{
                    // Include each pipeline phase plus line/refresh/IRQ/DMA vicinity without a quadratic huge suite.
                    if(paths.Count<128&&(paths.Count<32||row.Phase==27||observed.Clock.Now is >=600159 and <=600161 or >=600199 and <=600201 or >=600559 and <=600561 or >=600695 and <=600697)){
                        string path=Path.Combine(directory,"m6-observer-"+paths.Count);SNESEmulator.SaveStates.StateSerializer.Save(observed,path);paths.Add((path,observed.Bus.Apu.Crystal));
                    }
                };observed.RunUntil(601000);Check(paths.Count>8,"captured actual observer event ties");
                foreach(var item in paths){var loaded=SNESEmulator.SaveStates.StateSerializer.Load(observed.Bus.Cartridge!,item.Path);loaded.RunUntil(602000);string result=Path.Combine(directory,"m6-observer-tail");SNESEmulator.SaveStates.StateSerializer.Save(loaded,result);Check(File.ReadAllBytes(result).SequenceEqual(expected),$"{profile.Id} committed DSP observer full state{item.Crystal}");Check(loaded.Bus.Apu.Dsp.DrainSamples().SequenceEqual(samples.Where(s=>s.Crystal>item.Crystal)),"DSP observer future stereo");snapshots++;}
            }
            Evidence["fullConsoleDspObserverSnapshots"]=snapshots;
        });
        test("M6 native rational NTSC PAL continuous audio video pause reset and bounded long run",()=>{
            List<object> coverage=[];
            foreach(var profile in new[]{HardwareProfile.Ntsc,HardwareProfile.PalProfile}){
                var m=new SnesSystem(profile);m.Insert(new Cartridge(AudioDiagnosticRom.Create()));
                long target=profile.MasterHzNumerator*10/profile.MasterHzDenominator;m.RunUntil(target);
                Int128 crystal=(Int128)target*profile.ApuHzNumerator*profile.MasterHzDenominator/(profile.MasterHzNumerator*profile.ApuHzDenominator);
                long expected=(long)((crystal+96)/768);Check(m.Bus.Apu.Crystal==(long)crystal&&m.Bus.Apu.Dsp.SampleCount==expected,"exact10second integer clock/sample relationship");
                string paused=Json(m.Bus.Apu.Capture());m.RunUntil(target);Check(Json(m.Bus.Apu.Capture())==paused,"due-now pause advances no emulated time");
                Check(m.Bus.Apu.Dsp.DrainSamples().Length<=SnesDsp.PresentationCapacity,"long-run queue bounded");coverage.Add(new{profile.Id,masterclocks=target,apuCrystal=(long)crystal,samples=expected,fields=m.Frames});
                m.Reset();Check(m.Clock.Now==0&&m.Bus.Apu.Dsp.SampleCount==0&&m.Bus.Apu.Dsp.DrainSamples().Length==0,"warm reset starts defined clock epoch and presentation");m.RunUntil(1000);Check(m.Bus.Apu.Dsp.SampleCount==1,"first reset sample due-now edge");
            }
            Evidence["tenSecondAvRuns"]=coverage;
        });
        test("M6 bounded presentation queue separated from savestate and hardware",()=>{
            var a=Stopped();a.RunUntil(10000L*768);Check(a.Dsp.SampleCount==10000&&a.Dsp.DroppedPresentationSamples==10000-SnesDsp.PresentationCapacity,"bounded drop oldest");
            string before=Json(a.Capture());Check(a.Dsp.DrainSamples().Length==SnesDsp.PresentationCapacity,"presentation capacity");Check(Json(a.Capture())==before,"draining never changes hardware");
            var copy=new Apu();copy.Restore(a.Capture());Check(copy.Dsp.DrainSamples().Length==0&&copy.Dsp.SampleCount==10000,"no pending presentation serialized");
        });
        test("M6 native stereo comparator rejects channel value timing length mutations",()=>{
            StereoSample[] samples=[new(0,672,1,-2),new(1,1440,32767,-32768)];byte[] expected=[1,0,254,255,255,127,0,128];Check(StereoOracle.Difference(samples,expected)==null,"independent literal positive");
            foreach(var mutated in new[]{new[]{samples[0] with{Left=-2,Right=1},samples[1]},new[]{samples[0],samples[1] with{Right=-32767}},new[]{samples[0] with{Crystal=673},samples[1]},new[]{samples[0]},new[]{samples[0],samples[1],samples[1]}})Check(StereoOracle.Difference(mutated,expected)!=null,"negative digital comparator mutation");
            Check(StereoOracle.Difference(samples,expected.AsSpan(0,7))!=null,"truncated oracle");Evidence["negativeDigitalMutations"]=6;
        });
    }
}
