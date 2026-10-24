using System.Text.Json;
using System.Security.Cryptography;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;
namespace SNESEmulator.Testing;

public sealed class SpcFlatBus : ISpcBus
{
    public byte[] Ram {get;}=new byte[65536];
    public Dictionary<ushort,byte> BeforeWrite {get;}=[];
    public byte Read(ushort a)=>Ram[a];
    public void Write(ushort a,byte v){BeforeWrite.TryAdd(a,Ram[a]);Ram[a]=v;}
}
public static class SpcVector
{
    public static int Boot(string[] args)
    {
        string prefix=args.ElementAtOrDefault(0)??Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/spc-original-boot";
        string path=args.ElementAtOrDefault(1)??Environment.GetEnvironmentVariable("SNESEMULATOR_IPL")??"testdata/local-firmware/ipl.bin";
        var report=new TestReport("original-SPC-IPL-local-input");
        if(!File.Exists(path)){report.Add("original IPL execution","skipped","Supply legally obtained64-byte IPL via SNESEMULATOR_IPL; firmware is never downloaded by the gate or distributed");report.Save(prefix);return report.ExitCode;}
        byte[] firmware=File.ReadAllBytes(path);string hash=Convert.ToHexStringLower(SHA256.HashData(firmware));
        report.Metadata["firmwareSha256"]=hash;report.Metadata["source"]="Locally supplied image; known IPL bytes published in Anomie SPC700 doc revision1126,2007-04-21";
        if(hash!="c95f88b299030d5afa55b1031e2b5ef2dff650c4b4e6bb6f8b1359436521278f"){report.Add("known original IPL identity","fail","Local input differs from published IPL identity");report.Save(prefix);return report.ExitCode;}
        string? previous=Environment.GetEnvironmentVariable("SNESEMULATOR_IPL");
        try{
            Environment.SetEnvironmentVariable("SNESEMULATOR_IPL",Path.GetFullPath(path));var m=new SnesSystem();m.Insert(new Cartridge(SpcUploadRom.Create()));long reads=0,writes=0;
            m.Bus.Apu.PhaseObserved=p=>{if(p.Phase=="read-latch"&&p.Cycle.Address>=0xffc0)reads++;if(p.Phase=="write-latch"&&p.Cycle.Address is >=0x200 and <0x210)writes++;};
            m.RunUntil(300000);
            bool success=m.Cpu.Stopped&&m.Bus.Wram[0x1ff0]==0xa5&&m.Bus.Apu.Ram[0x300]==0x5a&&m.Bus.Apu.ApuToCpu[0]==0xa5&&m.Bus.Apu.ApuToCpu[1]==0xc3&&m.Bus.Apu.Ram.AsSpan(0x200,SpcUploadRom.Program.Length).SequenceEqual(SpcUploadRom.Program)&&reads>0&&writes==SpcUploadRom.Program.Length;
            report.Add("original IPL executed65816 upload and SPC RAM/ports response",success?"pass":"fail",evidence:new {iplReads=reads,uploadWrites=writes,masterClock=m.Clock.Now,spc=m.Bus.Apu.Cpu.Capture(),ports=m.Bus.Apu.ApuToCpu,signature=m.Bus.Wram[0x1ff0],ram0300=m.Bus.Apu.Ram[0x300]});
            string state=Path.GetTempFileName();try{
                StateSerializer.Save(m,state);var copy=StateSerializer.Load(m.Bus.Cartridge!,state);m.RunUntil(310000);copy.RunUntil(310000);
                report.Add("original IPL state resumes with same firmware",JsonSerializer.Serialize(m.Bus.Apu.Capture())==JsonSerializer.Serialize(copy.Bus.Apu.Capture())?"pass":"fail");
                Environment.SetEnvironmentVariable("SNESEMULATOR_IPL",null);bool rejected=false;try{StateSerializer.Load(m.Bus.Cartridge!,state);}catch(InvalidDataException e){rejected=e.Message.Contains("firmware identity");}
                report.Add("original IPL state rejects own replacement firmware",rejected?"pass":"fail");
            }finally{File.Delete(state);}
            Environment.SetEnvironmentVariable("SNESEMULATOR_IPL",Path.GetFullPath(path));var stress=new SnesSystem();stress.Insert(new Cartridge(SpcUploadRom.Create(true)));stress.RunUntil(2000000);
            bool stressPass=stress.Cpu.Stopped&&stress.Bus.Wram[0x1ff0]==0xa5&&stress.Bus.Apu.Ram[0x300]==0x5a&&stress.Bus.Apu.Ram.AsSpan(0x04f0,520).SequenceEqual(SpcUploadRom.StressData)&&stress.Bus.Apu.Ram.AsSpan(0x08ff,257).SequenceEqual(SpcUploadRom.WrapData)&&stress.Bus.Apu.Ram.AsSpan(0x0b80,255).SequenceEqual(SpcUploadRom.ZeroCommandData);
            report.Add("original IPL actual CPU1048byte multiblock index page command wraps",stressPass?"pass":"fail");
        }finally{Environment.SetEnvironmentVariable("SNESEMULATOR_IPL",previous);}
        report.Save(prefix);return report.ExitCode;
    }
    public static void Load(Spc700 c,SpcFlatBus b,JsonElement s)
    {
        int V(string k)=>s.GetProperty(k).GetInt32();
        c.RestoreContinuation(null);c.Restore(new((ushort)V("pc"),(byte)V("a"),(byte)V("x"),(byte)V("y"),(byte)V("sp"),(byte)V("psw"),false,false,0));
        Array.Clear(b.Ram);b.BeforeWrite.Clear();foreach(var p in s.GetProperty("ram").EnumerateArray())b.Ram[p[0].GetInt32()]=(byte)p[1].GetInt32();
    }
    public static string? StateDifference(Spc700 c,SpcFlatBus b,JsonElement s)
    {
        Dictionary<string,int> r=new(){["pc"]=c.PC,["a"]=c.A,["x"]=c.X,["y"]=c.Y,["sp"]=c.SP,["psw"]=c.PSW};
        foreach(var (k,v) in r)if(s.GetProperty(k).GetInt32()!=v)return $"{k}: expected {s.GetProperty(k)}, actual {v}";
        var ram=s.GetProperty("ram").EnumerateArray().ToDictionary(p=>(ushort)p[0].GetInt32(),p=>(byte)p[1].GetInt32());
        foreach(var (a,v) in ram)if(b.Ram[a]!=v)return $"RAM {a:X4}: expected {v:X2}, actual {b.Ram[a]:X2}";
        foreach(var (a,v) in b.BeforeWrite)if(!ram.ContainsKey(a)&&b.Ram[a]!=v)return $"unexpected RAM write {a:X4}";
        return null;
    }
    public static string? CycleDifference(IReadOnlyList<SpcCycle> actual,JsonElement expected)
    {
        int i=0;foreach(var row in expected.EnumerateArray()){
            var c=new SpcCycle(row[0].ValueKind==JsonValueKind.Null?null:(ushort)row[0].GetInt32(),row[1].ValueKind==JsonValueKind.Null?null:(byte)row[1].GetInt32(),row[2].GetString()!);
            if(i>=actual.Count)return $"missing cycle {i}: {c}";
            if(actual[i].Address!=c.Address||actual[i].Direction!=c.Direction||c.Value!=null&&actual[i].Value!=c.Value)return $"cycle {i}: expected {c}, actual {actual[i]}";i++;
        }return i==actual.Count?null:$"extra cycle {i}: {actual[i]}";
    }
    // Published Overload/AWJ/Near logic-analyzer adjudication (nesdev thread16140, 2017).
    // Immutable raw JSMoo rows stay unchanged. ONLY the seven [dp]+Y reads move wait2 to4.
    public static string? ApprovedCycleDifference(IReadOnlyList<SpcCycle> actual,JsonElement vector)
    {
        int op=vector.GetProperty("initial").GetProperty("ram").EnumerateArray().First(p=>p[0].GetInt32()==vector.GetProperty("initial").GetProperty("pc").GetInt32())[1].GetInt32();
        var expected=vector.GetProperty("cycles");
        if(op is not (0x17 or 0x37 or 0x57 or 0x77 or 0x97 or 0xb7 or 0xf7))return CycleDifference(actual,expected);
        int[] order=[0,1,3,4,2,5];if(actual.Count!=6||expected.GetArrayLength()!=6)return "adjudicated indirect-Y cycle count";
        for(int i=0;i<6;i++){var row=expected[order[i]];ushort? a=row[0].ValueKind==JsonValueKind.Null?null:(ushort)row[0].GetInt32();byte? v=row[1].ValueKind==JsonValueKind.Null?null:(byte)row[1].GetInt32();
            if(actual[i].Address!=a||actual[i].Direction!=row[2].GetString()||v!=null&&actual[i].Value!=v)return $"adjudicated indirect-Y cycle {i}: expected raw row {order[i]}, actual {actual[i]}";}
        return null;
    }
    public static int Run(string[] args,bool full,bool continuation=false)
    {
        string prefix=args.ElementAtOrDefault(0)??Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/spc-conformance";
        string directory=args.ElementAtOrDefault(1)??(full?"testdata/spc700-full":"testdata/spc700-sample");string? filter=args.ElementAtOrDefault(2);
        var report=new TestReport("SPC700-independent-state-and-electrical-cycles");long executed=0,cycles=0,failed=0,rawMismatches=0,semanticFailures=0,busFailures=0,adjudicated=0;int files=0;
        string manifest=full?"testdata/spc700-full-manifest.json":Path.Combine(directory,"manifest.json");
        if(!File.Exists(manifest)){report.Add("fixtures","skipped","Pinned SPC manifest missing");report.Save(prefix);return report.ExitCode;}
        using var metadata=JsonDocument.Parse(File.ReadAllBytes(manifest));
        var bus=new SpcFlatBus();var cpu=new Spc700(bus);List<SpcCycle> trace=[];cpu.CycleObserved=trace.Add;
        long hotCases=0,legacyCases=0;
        var hotCounter=typeof(Spc700).GetProperty("HotInstructionsCompleted",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var legacyCounter=typeof(Spc700).GetProperty("ReplayRunsCompleted",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        long ReadCounter(System.Reflection.PropertyInfo property)=>(long)property.GetValue(cpu)!;
        foreach(var item in metadata.RootElement.GetProperty("files").EnumerateArray()){
            string file=item.GetProperty("file").GetString()!;if(filter!=null&&file[..2]!=filter)continue;
            string path=Path.Combine(directory,file);if(!File.Exists(path)){report.Add(file,"skipped","Missing pinned file");continue;}
            byte[] data=File.ReadAllBytes(path);string hash=Convert.ToHexStringLower(SHA256.HashData(data));
            if(hash!=item.GetProperty("sha256").GetString()){report.Add(file,"fail","SHA-256 mismatch");continue;}
            files++;using var doc=JsonDocument.Parse(data);int count=0,bad=0,rawBad=0;string? first=null;object? evidence=null,rawEvidence=null;
            foreach(var v in doc.RootElement.EnumerateArray()){
                count++;executed++;trace.Clear();Load(cpu,bus,v.GetProperty("initial"));
                try{
                    if(continuation){long beforeHot=ReadCounter(hotCounter),beforeLegacy=ReadCounter(legacyCounter);while(!cpu.RunInstruction()){}hotCases+=ReadCounter(hotCounter)-beforeHot;legacyCases+=ReadCounter(legacyCounter)-beforeLegacy;}
                    else cpu.Step();
                    cycles+=trace.Count;string? raw=CycleDifference(trace,v.GetProperty("cycles"));if(raw!=null){rawMismatches++;rawBad++;rawEvidence??=new {difference=raw,vector=v.Clone(),actualCycles=trace.ToArray()};}string? stateDiff=StateDifference(cpu,bus,v.GetProperty("final")),busDiff=ApprovedCycleDifference(trace,v);if(stateDiff!=null)semanticFailures++;if(busDiff!=null)busFailures++;if(raw!=null&&busDiff==null)adjudicated++;string? diff=stateDiff??busDiff;
                    if(diff!=null){bad++;first??=v.GetProperty("name").GetString()+": "+diff;evidence??=new {vector=v.Clone(),actual=cpu.Capture(),actualCycles=trace.ToArray()};}}
                catch(Exception e){bad++;first??=v.GetProperty("name").GetString()+": "+e.Message;}
            }
            if(count!=item.GetProperty("cases").GetInt32()){bad++;first??="case count mismatch";}failed+=bad;
            report.Add(file,bad==0?"pass":"fail",first,evidence:new {approvedFailures=evidence,rawBusMismatches=rawBad,firstRawMismatch=rawEvidence});Console.WriteLine($"SPC {file}: {count-bad} pass / {bad} fail");
            if(!full)report.Add(file+"/unsampled","skipped","Only four sample vectors; full corpus required for acceptance");
        }
        report.Metadata["executionPath"]=continuation?"RunInstruction:23 hot forms, remaining forms explicitly replay decoder":"original Step decoder";
        report.Metadata["hotCases"]=hotCases;report.Metadata["legacyCases"]=legacyCases;
        report.Metadata["executedCases"]=executed;report.Metadata["failedCases"]=failed;report.Metadata["busCyclesCompared"]=cycles;report.Metadata["fixturesVerified"]=files;report.Metadata["revision"]=metadata.RootElement.GetProperty("revision").GetString();
        report.Metadata["rawBusPassed"]=executed-rawMismatches;report.Metadata["rawBusMismatches"]=rawMismatches;report.Metadata["sourceAdjudicatedCases"]=adjudicated;report.Metadata["semanticPassed"]=executed-semanticFailures;report.Metadata["semanticFailures"]=semanticFailures;report.Metadata["approvedBusPassed"]=executed-busFailures;report.Metadata["approvedBusFailures"]=busFailures;report.Metadata["adjudicationRule"]="spc-indirect-y-2017-v1: only17/37/57/77/97/B7/F7 order[0,1,3,4,2,5]; hardware researcher discussion https://forums.nesdev.org/viewtopic.php?t=16140";
        report.Save(prefix);return report.ExitCode;
    }
}
