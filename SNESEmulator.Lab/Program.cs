using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Testing;

return Lab.Run(args);

static class Lab
{
    static JsonSerializerOptions Json = new() { WriteIndented=true };
    public static int Run(string[] args)
    {
        if(args.Length==0) {Console.WriteLine("Commands: cpu [report-prefix] [fixture-directory] [case-name], rom [options], mutations [prefix], supervise <seconds> <report-prefix> <command...>, probe <pass|fail|skipped|hang|crash>. See docs/test-laboratory.md.");return 2;}
        try { return args[0] switch { "cpu"=>Cpu(args.Skip(1).ToArray()),"rom"=>Rom(args.Skip(1).ToArray()),"mutations"=>Mutations(args.ElementAtOrDefault(1)??Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/mutations"),"supervise"=>Supervise(args.Skip(1).ToArray()),"unavailable"=>Unavailable(args[1]),"probe"=>Probe(args[1]),_=>throw new ArgumentException("Unknown command") }; }
        catch(Exception ex) {Console.Error.WriteLine(ex);var r=new TestReport("laboratory-command");r.Add(args[0],"crash",ex.ToString());r.Save("artifacts/command-crash");return 1;}
    }
    static int Cpu(string[] args)
    {
        string prefix=args.ElementAtOrDefault(0)??Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/conformance",directory=args.ElementAtOrDefault(1)??"testdata/65816", filter=args.ElementAtOrDefault(2)??"";
        var report=new TestReport("65816-sampled-conformance");
        report.Metadata["accuracy"]="Register/listed RAM semantics only; timing and cycle traces NOT accepted. Generic 65816, no SNES hardware proof.";
        report.Metadata["reproduce"]=$"dotnet run --project SNESEmulator.Lab -c Release -- cpu {prefix} {directory} [case-name]";
        string manifestPath=Path.Combine(directory,"../manifest.json");
        if(!File.Exists(manifestPath)) { report.Add("manifest","skipped","Missing pinned manifest: "+manifestPath);report.Save(prefix);return report.ExitCode; }
        byte[] manifestBytes=File.ReadAllBytes(manifestPath);using var manifest=JsonDocument.Parse(manifestBytes);
        report.Metadata["manifestSha256"]=Convert.ToHexString(SHA256.HashData(manifestBytes));
        report.Metadata["sourceCommit"]=manifest.RootElement.GetProperty("commit").GetString();
        report.Metadata["representedCases"]=manifest.RootElement.GetProperty("representedCases").GetInt32();report.Metadata["upstreamCases"]=manifest.RootElement.GetProperty("upstreamCases").GetInt32();
        var files=manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
        var bus=new TraceBus();var cpu=new Cpu65816(bus);int executed=0;var coverage=new List<string>();
        for(int op=0;op<256;op++) foreach(string mode in new[]{"e","n"})
        {
            string key=$"{op:x2}.{mode}";
            var matches=files.Where(f=>f.GetProperty("opcode").GetString()==$"{op:x2}" && f.GetProperty("mode").GetString()==mode).ToArray();
            if(matches.Length!=1){report.Add(key,"skipped","Missing or ambiguous manifest entry");continue;}
            var entry=matches[0];string path=Path.Combine(directory,key+".json");
            if(!File.Exists(path)){report.Add(key,"skipped","Pinned fixture missing: "+path);continue;}
            byte[] bytes=File.ReadAllBytes(path);
            if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase)){report.Add(key,"fail","Fixture SHA-256 mismatch: "+path);continue;}
            using var doc=JsonDocument.Parse(bytes);int count=doc.RootElement.GetArrayLength();
            if(count!=entry.GetProperty("cases").GetInt32()){report.Add(key,"fail","Manifest case count mismatch");continue;}
            coverage.Add(key);
            foreach(var vector in doc.RootElement.EnumerateArray())
            {
                string name=vector.GetProperty("name").GetString()!;
                if(filter!="" && filter!=name)continue;
                Array.Clear(bus.Memory);CpuVector.Load(cpu,bus,vector.GetProperty("initial"));var watch=Stopwatch.StartNew();
                try
                {
                    int approximateCycles=CpuVector.ExecuteInstruction(cpu,bus);executed++;
                    string? diff=CpuVector.FirstDifference(cpu,bus,vector.GetProperty("final"));
                    report.Add(name,diff==null?"pass":"fail",diff,watch.Elapsed.TotalSeconds,diff==null?null:new { opcode=$"{op:X2}",mode,initial=vector.GetProperty("initial").Clone(),expected=vector.GetProperty("final").Clone(),actual=CpuVector.State(cpu),logicalBus=bus.Trace.ToArray(),approximateCycles,reproduce=$"dotnet run --project SNESEmulator.Lab -c Release -- cpu {prefix} {directory} \"{name}\"" });
                }
                catch(Exception ex) {report.Add(name,"crash",ex.ToString(),watch.Elapsed.TotalSeconds,new {initial=vector.GetProperty("initial").Clone()});}
            }
            if(filter=="")report.Add(key+"/unsampled","skipped",$"{entry.GetProperty("upstreamCases").GetInt32()-count} upstream vectors not selected; sample count {count}. Full suite deferred to M1.");
        }
        if(executed==0)report.Add("selected-cases","skipped","No vectors executed, including any requested name filter");
        report.Metadata["executedCases"]=executed;report.Metadata["opcodeModeFixturesVerified"]=coverage.Count;report.Metadata["coverage"]=coverage;
        report.Save(prefix);return report.ExitCode;
    }
    static int Mutations(string prefix)
    {
        var r=new TestReport("report-mutation-checks");
        using var doc=JsonDocument.Parse("""{"pc":32768,"s":511,"p":52,"a":4660,"x":0,"y":0,"dbr":0,"d":0,"pbr":0,"e":1,"ram":[]}""");
        var bus=new TraceBus();var cpu=new Cpu65816(bus);CpuVector.Load(cpu,bus,doc.RootElement);cpu.A^=1;
        string? register=CpuVector.FirstDifference(cpu,bus,doc.RootElement);
        uint[] actual=[0xff000000,0xff000001,0xff000000,0xff000000],expected=[0xff000000,0xff000000,0xff000000,0xff000000];
        string? pixel=CpuVector.FirstPixelDifference(actual,expected,2);
        // Deliberately bad comparisons must produce failure reports; checks assert their diagnostics too.
        var rejected=new TestReport("deliberately-invalid-oracles");rejected.Add("incorrect-register",register==null?"pass":"fail",register,evidence:new {reproduce="dotnet run --project SNESEmulator.Lab -c Release -- mutations"});rejected.Add("incorrect-image",pixel==null?"pass":"fail",pixel);rejected.Save(prefix+"-rejected");
        r.Add("incorrect register detected",register=="register a: expected 1234, actual 1235"?"pass":"fail",register);
        r.Add("incorrect frame first pixel detected",pixel=="pixel (1,0): expected FF000000, actual FF000001"?"pass":"fail",pixel);
        using var saved=JsonDocument.Parse(File.ReadAllBytes(prefix+"-rejected.json"));
        r.Add("saved report retains failures",saved.RootElement.GetProperty("Tests").EnumerateArray().All(t=>t.GetProperty("Status").GetString()=="fail")?"pass":"fail");
        CpuVector.Load(cpu,bus,doc.RootElement);bus.Write(0x123456,0x42);
        string? extra=CpuVector.FirstDifference(cpu,bus,doc.RootElement);
        r.Add("unexpected memory write detected",extra=="unexpected memory change 123456: expected 00, actual 42"?"pass":"fail",extra);
        r.Save(prefix);return r.ExitCode;
    }
    static int Unavailable(string suite)
    {
        var report=new TestReport(suite);
        string reason=suite switch {
            "audio-conformance"=>"SPC700 and DSP not implemented; no independent native audio reference data acquired. No audio correctness result.",
            "hardware-evidence"=>"No measured original console/revision or trace acquisition is available. Proposed profiles remain unverified.",
            "graphics-conformance"=>"No independent hardware/reference-emulator frame corpus acquired. Synthetic comparator checks and diagnostic rendering are internal checks only.",
            _=>throw new ArgumentException("Unknown unavailable evidence suite") };
        report.Add(suite,"skipped",reason);report.Save(Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/"+suite);return report.ExitCode;
    }
    static int Probe(string kind)
    {
        if(kind=="silent-success")return 0;
        if(kind=="hang") {Thread.Sleep(Timeout.Infinite);return 0;}
        if(kind=="crash")Environment.Exit(23);
        var r=new TestReport("status-probe");r.Add("probe",kind=="green-nonzero"?"pass":kind);r.Save(Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/probe");return kind=="green-nonzero"?23:r.ExitCode;
    }
    static int Supervise(string[] args)
    {
        if(args.Length<3)throw new ArgumentException("supervise requires seconds, report prefix, executable and arguments");
        double seconds=double.Parse(args[0],System.Globalization.CultureInfo.InvariantCulture);if(seconds<=0||seconds>86400)throw new ArgumentException("Invalid watchdog seconds");
        string prefix=args[1];Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefix))!);
        // Delete stale reports before launch: a crashed process must never inherit an old green result.
        foreach(string extension in new[]{".json",".junit.xml"})if(File.Exists(prefix+extension))File.Delete(prefix+extension);
        string child=prefix+"-child";foreach(string extension in new[]{".json",".junit.xml"})if(File.Exists(child+extension))File.Delete(child+extension);
        var report=new TestReport("process-watchdog");var start=new ProcessStartInfo(args[2]) {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        foreach(string arg in args.Skip(3))start.ArgumentList.Add(arg);
        start.Environment["SNES_LAB_CHILD_REPORT"]=child;
        var watch=Stopwatch.StartNew();
        try
        {
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            bool done=process.WaitForExit((int)(seconds*1000));
            if(!done){process.Kill(true);process.WaitForExit();}
            string output=stdout.GetAwaiter().GetResult(),error=stderr.GetAwaiter().GetResult();
            File.WriteAllText(prefix+".stdout.txt",output);File.WriteAllText(prefix+".stderr.txt",error);Console.Write(output);Console.Error.Write(error);
            string status="crash";string detail=$"Exit code {process.ExitCode}; command "+string.Join(" ",args.Skip(2));
            if(!done)status="timeout";
            else if(File.Exists(child+".json"))
            {
                using var parsed=JsonDocument.Parse(File.ReadAllBytes(child+".json"));var results=parsed.RootElement.GetProperty("Tests").EnumerateArray().Select(t=>t.GetProperty("Status").GetString()).ToArray();
                status=results.Contains("crash")?"crash":results.Contains("timeout")?"timeout":results.Contains("fail")?"fail":results.Contains("skipped")?"skipped":results.Length>0&&process.ExitCode==0?"pass":"crash";
                detail+="; child report "+child+".json";
            }
            report.Add("child-process",status,detail,watch.Elapsed.TotalSeconds);
        }
        catch(Exception ex) {report.Add("child-process","crash",ex.ToString(),watch.Elapsed.TotalSeconds);}
        report.Save(prefix);return report.ExitCode;
    }
    sealed class LimitReached : Exception { }
    sealed record ReplayEvent(long Frame,int Port,ushort Buttons);
    static int Rom(string[] args)
    {
        var options=new Dictionary<string,string>();
        for(int i=0;i<args.Length;i+=2){if(i+1==args.Length || !args[i].StartsWith("--"))throw new ArgumentException("ROM options require --key value pairs");options.Add(args[i][2..],args[i+1]);}
        foreach(string key in options.Keys)if(key is not ("frames" or "masterclocks" or "output" or "rom" or "replay" or "trace-limit" or "expect-wram" or "expect-frame"))throw new ArgumentException("Unknown ROM option: "+key);
        string Opt(string k,string fallback)=>options.GetValueOrDefault(k,fallback);
        long frames=long.Parse(Opt("frames","8")),clockLimit=long.Parse(Opt("masterclocks","100000000"));
        if(frames<1||clockLimit<1)throw new ArgumentException("Positive frame and approximate masterclock limits required");
        string prefix=Opt("output",Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/rom");
        var report=new TestReport("headless-rom");var machine=new SnesSystem();
        byte[] rom=options.ContainsKey("rom")?File.ReadAllBytes(options["rom"]):DiagnosticRom.Create();machine.Insert(new Cartridge(rom));
        report.Metadata["romSha256"]=Convert.ToHexString(SHA256.HashData(rom));report.Metadata["profile"]=machine.Bus.Cartridge!.Pal?"approximate-pal-proposed":"approximate-ntsc-proposed";
        report.Metadata["accuracy"]="Approximate instruction Step()*6 + immediate DMA accounting; whole-scanline rendering. These are NOT hardware masterclocks. Limit overshoot <= one instruction plus immediate DMA debt. Trace clocks are instruction-start labels, not bus timestamps.";
        report.Metadata["framesLimit"]=frames;report.Metadata["approximateMasterclockLimit"]=clockLimit;
        ReplayEvent[] replay=[];
        if(options.TryGetValue("replay",out string? replayPath))
        {
            byte[] bytes=File.ReadAllBytes(replayPath);replay=JsonSerializer.Deserialize<ReplayEvent[]>(bytes)??throw new InvalidDataException("Replay array required");
            if(replay.Any(e=>e.Frame<0||e.Port<0||e.Port>1)||!replay.Select(e=>e.Frame).SequenceEqual(replay.Select(e=>e.Frame).Order()))throw new InvalidDataException("Replay must be ordered by frame with ports 0 or 1");
            report.Metadata["replaySha256"]=Convert.ToHexString(SHA256.HashData(bytes));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefix))!);
        using var cpuTrace=new StreamWriter(prefix+".cpu.jsonl");using var busTrace=new StreamWriter(prefix+".bus.jsonl");using var rasterTrace=new StreamWriter(prefix+".raster.jsonl");
        long clocks=0,busEvents=0,instructions=0;int previousRemainder=0,lastLine=-1,replayIndex=0,traceLimit=int.Parse(Opt("trace-limit","4096"));
        if(traceLimit<0)throw new ArgumentException("Trace limit must not be negative");
        machine.Bus.AccessObserved=(direction,address,value)=>{if(busEvents++<traceLimit)busTrace.WriteLine(JsonSerializer.Serialize(new {sequence=busEvents,approximateInstructionStart=clocks,direction,address,value}));};
        var watch=Stopwatch.StartNew();
        try
        {
            while(machine.Frames<frames && clocks<clockLimit)
            {
                while(replayIndex<replay.Length && replay[replayIndex].Frame==machine.Frames) {var e=replay[replayIndex++];machine.Bus.Controllers.Buttons[e.Port]=e.Buttons;}
                machine.RunFrame(m=>
                {
                    previousRemainder=m.MasterRemainder;
                    if(m.Bus.Ppu.Scanline!=lastLine) {lastLine=m.Bus.Ppu.Scanline;rasterTrace.WriteLine(JsonSerializer.Serialize(new {frame=m.Frames,line=lastLine,approximateClock=clocks}));}
                    if(instructions++<traceLimit)cpuTrace.WriteLine(JsonSerializer.Serialize(new {frame=m.Frames,line=lastLine,approximateClock=clocks,state=CpuVector.State(m.Cpu)}));
                },m=>{clocks+=m.MasterRemainder-previousRemainder;if(clocks>=clockLimit)throw new LimitReached();});
            }
        }
        catch(LimitReached) { }
        machine.Bus.AccessObserved=null;
        File.WriteAllBytes(prefix+".wram.bin",machine.Bus.Wram);
        using(var output=new BinaryWriter(File.Create(prefix+".argb32le.bin")))foreach(uint pixel in machine.Bus.Ppu.Pixels)output.Write(pixel);
        report.Metadata["cpuState"]=CpuVector.State(machine.Cpu);report.Metadata["completedFrames"]=machine.Frames;report.Metadata["approximateMasterclocks"]=clocks;report.Metadata["instructionsAttempted"]=instructions;
        report.Metadata["traceLimit"]=traceLimit;report.Metadata["busEvents"]=busEvents;report.Metadata["traceTruncated"]=busEvents>traceLimit||instructions>traceLimit;
        report.Metadata["stopReason"]=machine.Frames>=frames?"frame-limit":"approximate-masterclock-limit";
        report.Metadata["wramSha256"]=Convert.ToHexString(SHA256.HashData(machine.Bus.Wram));
        report.Metadata["frameSha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(prefix+".argb32le.bin")));
        report.Metadata["inputEventsApplied"]=replayIndex;report.Metadata["inputEventsNotReached"]=replay.Length-replayIndex;
        report.Add("bounded execution completed","pass","Execution smoke only; no independent ROM success oracle implied",watch.Elapsed.TotalSeconds);
        if(options.TryGetValue("expect-wram",out string? memoryPath))
        {
            using var expected=JsonDocument.Parse(File.ReadAllBytes(memoryPath));string? diff=null;
            foreach(var p in expected.RootElement.EnumerateArray()){int offset=p[0].GetInt32(),value=p[1].GetInt32();if(offset<0||offset>=machine.Bus.Wram.Length||value<0||value>255)throw new InvalidDataException("Invalid WRAM oracle");if(machine.Bus.Wram[offset]!=value){diff=$"WRAM {offset:X5}: expected {value:X2}, actual {machine.Bus.Wram[offset]:X2}";break;}}
            report.Metadata["memoryOracleSha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(memoryPath)));report.Add("WRAM oracle",diff==null?"pass":"fail",diff);
        }
        if(options.TryGetValue("expect-frame",out string? framePath))
        {
            byte[] bytes=File.ReadAllBytes(framePath);if(bytes.Length%4!=0)throw new InvalidDataException("ARGB32LE reference requires whole pixels");uint[] expected=new uint[bytes.Length/4];for(int i=0;i<expected.Length;i++)expected[i]=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i*4));
            string? diff=CpuVector.FirstPixelDifference(machine.Bus.Ppu.Pixels,expected,Ppu.Width);report.Metadata["frameOracleSha256"]=Convert.ToHexString(SHA256.HashData(bytes));report.Add("frame oracle",diff==null?"pass":"fail",diff);
        }
        report.Save(prefix);return report.ExitCode;
    }
}
