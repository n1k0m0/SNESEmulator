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
        if(args.Length==0) {Console.WriteLine("Commands: cpu [report-prefix] [fixture-directory] [case-name], cpu-full [report-prefix] [fixture-directory] [opcode.mode], rom [options], mutations [prefix], supervise <seconds> <report-prefix> <command...>, probe <pass|fail|skipped|hang|crash>. See docs/test-laboratory.md.");return 2;}
        try { return args[0] switch { "cpu"=>Cpu(args.Skip(1).ToArray()),"cpu-full"=>Cpu(args.Skip(1).ToArray(),true),"cpu-bus"=>Cpu(args.Skip(1).ToArray(),false,true),"cpu-bus-full"=>Cpu(args.Skip(1).ToArray(),true,true),"benchmark"=>Benchmark(),"rom"=>Rom(args.Skip(1).ToArray()),"mutations"=>Mutations(args.ElementAtOrDefault(1)??Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/mutations"),"supervise"=>Supervise(args.Skip(1).ToArray()),"unavailable"=>Unavailable(args[1]),"probe"=>Probe(args[1]),_=>throw new ArgumentException("Unknown command") }; }
        catch(Exception ex) {Console.Error.WriteLine(ex);var r=new TestReport("laboratory-command");r.Add(args[0],"crash",ex.ToString());r.Save("artifacts/command-crash");return 1;}
    }
    static int Benchmark()
    {
        var report=new TestReport("M2-local-performance-observation");report.Metadata["host"]=new{machine=Environment.MachineName,os=Environment.OSVersion.ToString(),runtime=Environment.Version.ToString(),processors=Environment.ProcessorCount};
        var machine=new SnesSystem();machine.Insert(new Cartridge(DiagnosticRom.Create()));machine.RunFrame();var watch=Stopwatch.StartNew();
        for(int n=0;n<120;n++)machine.RunFrame();watch.Stop();report.Metadata["framesPerSecond"]=120/watch.Elapsed.TotalSeconds;report.Metadata["masterclocksPerSecond"]=(machine.Clock.Now-357368)/watch.Elapsed.TotalSeconds;
        var bus=new TraceBus{TraceEnabled=false};Array.Fill(bus.Memory,(byte)0xea);var cpu=new Cpu65816(bus){PC=0x8000,P=0x34};
        watch.Restart();for(int n=0;n<100000;n++)cpu.Step();watch.Stop();report.Metadata["stepNopInstructionsPerSecond"]=100000/watch.Elapsed.TotalSeconds;
        watch.Restart();for(int n=0;n<100000;n++)while(cpu.AdvanceCycle()){}watch.Stop();report.Metadata["singleCycleNopInstructionsPerSecond"]=100000/watch.Elapsed.TotalSeconds;
        report.Add("benchmark executed","pass","Performance observation only; no minimum performance or hardware-accuracy assertion");report.Save("artifacts/m2-benchmark");Console.WriteLine(JsonSerializer.Serialize(report.Metadata,Json));return 0;
    }
    static int Cpu(string[] args,bool full=false,bool timed=false)
    {
        string prefix=args.ElementAtOrDefault(0)??Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/conformance",directory=args.ElementAtOrDefault(1)??(full?"testdata/65816-full":"testdata/65816"), filter=args.ElementAtOrDefault(2)??"";
        string command=(timed?"cpu-bus":"cpu")+(full?"-full":"");
        var report=new TestReport("65816-"+(full?"full":"sampled")+(timed?"-bus":"-semantics"));
        report.Metadata["accuracy"]=timed?"Generic 65816 bus cycles, separate from SNES hardware access timing; no original hardware proof.":"Register/listed RAM semantics only; timing and cycle traces NOT accepted. Generic 65816, no SNES hardware proof.";
        report.Metadata["reproduce"]=$"dotnet run --project SNESEmulator.Lab -c Release -- {command} {prefix} {directory} {(full ? "[opcode.mode]" : "[case-name]")}";
        string manifestPath=Path.Combine(directory,full?"manifest.json":"../manifest.json");
        if(!File.Exists(manifestPath)) { report.Add("manifest","skipped","Missing pinned manifest: "+manifestPath);report.Save(prefix);return report.ExitCode; }
        byte[] manifestBytes=File.ReadAllBytes(manifestPath);using var manifest=JsonDocument.Parse(manifestBytes);
        if(full && (!File.Exists("testdata/65816-full-manifest.json") || !SHA256.HashData(manifestBytes).SequenceEqual(SHA256.HashData(File.ReadAllBytes("testdata/65816-full-manifest.json"))))) {
            report.Add("full-manifest-integrity","fail","Full manifest differs from reviewed immutable source hashes");report.Save(prefix);return report.ExitCode;
        }
        report.Metadata["manifestSha256"]=Convert.ToHexString(SHA256.HashData(manifestBytes));
        report.Metadata["sourceCommit"]=manifest.RootElement.GetProperty("commit").GetString();
        report.Metadata["representedCases"]=manifest.RootElement.GetProperty("representedCases").GetInt32();report.Metadata["upstreamCases"]=manifest.RootElement.GetProperty("upstreamCases").GetInt32();
        var files=manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
        var bus=new TraceBus { TraceEnabled=!full };var cpu=new Cpu65816(bus);var cycleTrace=new List<CpuCycle>();if(timed)cpu.CycleObserved=cycleTrace.Add;int executed=0,totalPassed=0,totalFailed=0;long comparedCycles=0;var coverage=new List<string>();
        for(int op=0;op<256;op++) foreach(string mode in new[]{"e","n"})
        {
            string key=$"{op:x2}.{mode}";
            if(full && filter!="" && filter!=key) continue;
            var matches=files.Where(f=>f.GetProperty("opcode").GetString()==$"{op:x2}" && f.GetProperty("mode").GetString()==mode).ToArray();
            if(matches.Length!=1){report.Add(key,"skipped","Missing or ambiguous manifest entry");continue;}
            var entry=matches[0];string path=Path.Combine(directory,key+".json");
            if(!File.Exists(path)){report.Add(key,"skipped","Pinned fixture missing: "+path);continue;}
            using var hashStream=File.OpenRead(path);
            if(!Convert.ToHexString(SHA256.HashData(hashStream)).Equals(entry.GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase)){report.Add(key,"fail","Fixture SHA-256 mismatch: "+path);continue;}
            var groupWatch=Stopwatch.StartNew();
            int count=0,passed=0,failed=0,crashed=0;object? firstFailure=null;
            string? firstDiff=null;
            using var vectorStream=File.OpenRead(path);
            var vectors=JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(vectorStream).GetAsyncEnumerator();
            IEnumerable<JsonElement> ReadVectors() { try { while(vectors.MoveNextAsync().AsTask().GetAwaiter().GetResult()) yield return vectors.Current; } finally { vectors.DisposeAsync().AsTask().GetAwaiter().GetResult(); } }
            coverage.Add(key);
            foreach(var vector in ReadVectors())
            {
                if(groupWatch.Elapsed.TotalSeconds>30) { failed++; report.Add(key+"/watchdog","timeout","Opcode/mode group exceeded 30 seconds");break; }
                count++;
                string name=vector.GetProperty("name").GetString()!;
                if(!full && filter!="" && filter!=name)continue;
                cycleTrace.Clear();bus.ClearMemory();CpuVector.Load(cpu,bus,vector.GetProperty("initial"));var effectiveInitial=CpuVector.EffectiveInitial(cpu);var watch=Stopwatch.StartNew();
                try
                {
                    int approximateCycles=CpuVector.ExecuteInstruction(cpu,bus);executed++;if(timed)comparedCycles+=cycleTrace.Count;
                    string? diff=CpuVector.FirstDifference(cpu,bus,vector.GetProperty("final"));if(timed)diff??=CpuVector.FirstCycleDifference(cycleTrace,vector.GetProperty("cycles"));
                    if(diff==null) { passed++;totalPassed++; } else { failed++;totalFailed++; firstDiff??=name+": "+diff; firstFailure??=new {name,opcode=$"{op:X2}",mode,emulation=effectiveInitial["e"]!=0,memory8=effectiveInitial["e"]!=0||(effectiveInitial["p"]&32)!=0,index8=effectiveInitial["e"]!=0||(effectiveInitial["p"]&16)!=0,effectiveInitial,initial=vector.GetProperty("initial").Clone(),expected=vector.GetProperty("final").Clone(),actual=CpuVector.State(cpu),expectedCycles=timed?vector.GetProperty("cycles").Clone():default,actualCycles=timed?cycleTrace.ToArray():null}; }
                    if(!full) report.Add(name,diff==null?"pass":"fail",diff,watch.Elapsed.TotalSeconds,diff==null?null:new { opcode=$"{op:X2}",mode,effectiveInitial,initial=vector.GetProperty("initial").Clone(),expected=vector.GetProperty("final").Clone(),actual=CpuVector.State(cpu),logicalBus=bus.Trace.ToArray(),approximateCycles,reproduce=$"dotnet run --project SNESEmulator.Lab -c Release -- {command} {prefix} {directory} \"{name}\"" });
                }
                catch(Exception ex) {failed++;crashed++;totalFailed++;firstDiff??=name+": "+ex.Message;firstFailure??=new {name,effectiveInitial,initial=vector.GetProperty("initial").Clone(),error=ex.ToString()};if(!full)report.Add(name,"crash",ex.ToString(),watch.Elapsed.TotalSeconds,new {initial=vector.GetProperty("initial").Clone()});}
            }
            if(count!=entry.GetProperty("cases").GetInt32()) { failed++; report.Add(key+"/count","fail",$"Expected {entry.GetProperty("cases").GetInt32()} cases, read {count}"); }
            if(full) { report.Add(key,crashed>0?"crash":failed==0?"pass":"fail",firstDiff,groupWatch.Elapsed.TotalSeconds,evidence:new {passed,failed,crashed,firstFailure,reproduce=$"dotnet run --project SNESEmulator.Lab -c Release -- {command} {prefix} {directory} {key}"});Console.WriteLine($"{key}: {passed} pass / {failed} fail"); }
            if(!full && filter=="")report.Add(key+"/unsampled","skipped",$"{entry.GetProperty("upstreamCases").GetInt32()-count} upstream vectors not selected; sample count {count}. Full suite available with cpu-full.");
        }
        if(executed==0)report.Add("selected-cases","skipped","No vectors executed, including any requested name filter");
        report.Metadata["busCyclesCompared"]=comparedCycles;report.Metadata["executedCases"]=executed;report.Metadata["passedCases"]=totalPassed;report.Metadata["failedCases"]=totalFailed;report.Metadata["coverageKind"]=full?(filter==""?"full":"full-file-filter"):"sample";report.Metadata["stackConvention"]="Raw upstream initial S retained in evidence; before execution E=1 maps S to 0x100|(S&255), the only valid emulation stack page. P/X/Y unchanged.";report.Metadata["opcodeModeFixturesVerified"]=coverage.Count;report.Metadata["coverage"]=coverage;
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
        using var semantics=JsonDocument.Parse("""{"pc":32770,"s":511,"p":52,"a":66,"x":0,"y":0,"dbr":0,"d":0,"pbr":0,"e":1,"ram":[]}""");
        foreach(bool mutate in new[]{false,true}) {
            bus.ClearMemory();CpuVector.Load(cpu,bus,doc.RootElement);cpu.A=0;
            bus.Initialize(0x8000,(byte)(mutate?0xA2:0xA9));bus.Initialize(0x8001,0x42);CpuVector.ExecuteInstruction(cpu,bus);
            string? difference=CpuVector.FirstDifference(cpu,bus,semantics.RootElement);
            r.Add(mutate?"changed instruction rejected by independent oracle":"independent immediate-load oracle",(mutate?difference=="register a: expected 42, actual 0":difference==null)?"pass":"fail",difference);
        }
        using var busOracle=JsonDocument.Parse("[[32768,234,\"dp-remx-\"],[32769,null,\"---remx-\"]]");
        CpuCycle[] correct=[new(32768,234,"dp-remx-"),new(32769,null,"---remx-")];
        foreach(var (label,mutation) in new (string,CpuCycle[])[]{("missing cycle",[correct[0]]),("wrong address",[correct[0] with{Address=32770},correct[1]]),("wrong pins",[correct[0] with{Pins="-p-remx-"},correct[1]]),("wrong direction",[correct[0] with{Pins="dp-wemx-"},correct[1]]),("wrong value",[correct[0] with{Value=233},correct[1]])})
        {string? diff=CpuVector.FirstCycleDifference(mutation,busOracle.RootElement);r.Add(label+" rejected",diff!=null&&diff.StartsWith(label=="missing cycle"?"cycle 1:":"cycle 0:")?"pass":"fail",diff);}
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
        if(frames<1||clockLimit<1)throw new ArgumentException("Positive frame and masterclock limits required");
        string prefix=Opt("output",Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/rom");
        var report=new TestReport("headless-rom");var machine=new SnesSystem();
        byte[] rom=options.ContainsKey("rom")?File.ReadAllBytes(options["rom"]):DiagnosticRom.Create();machine.Insert(new Cartridge(rom));
        report.Metadata["romSha256"]=Convert.ToHexString(SHA256.HashData(rom));report.Metadata["profile"]=machine.Profile;
        report.Metadata["accuracy"]="Exact master-clock budget with individually timed CPU cycles. Generic CPU bus conformance and analytical profile timing are separate evidence. Whole-line PPU rendering and legacy immediate DMA remain incomplete M3/M5 devices; oscillator/revision target is unverified hardware.";
        report.Metadata["busTraceContract"]="Explicit read-latch, write-latch and cycle-complete phases. Valid read latch after accessClocks-4 active clocks plus4 active trailing hold; writes at completion. Each record includes phase timestamp, cycle start, latch timestamp and captured value. MDR exceptions remain M3.";
        report.Metadata["framesLimit"]=frames;report.Metadata["masterclockLimit"]=clockLimit;
        ReplayEvent[] replay=[];
        if(options.TryGetValue("replay",out string? replayPath))
        {
            byte[] bytes=File.ReadAllBytes(replayPath);replay=JsonSerializer.Deserialize<ReplayEvent[]>(bytes)??throw new InvalidDataException("Replay array required");
            if(replay.Any(e=>e.Frame<0||e.Port<0||e.Port>1)||!replay.Select(e=>e.Frame).SequenceEqual(replay.Select(e=>e.Frame).Order()))throw new InvalidDataException("Replay must be ordered by frame with ports 0 or 1");
            report.Metadata["replaySha256"]=Convert.ToHexString(SHA256.HashData(bytes));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefix))!);
        using var cpuTrace=new StreamWriter(prefix+".cpu.jsonl");using var busTrace=new StreamWriter(prefix+".bus.jsonl");using var rasterTrace=new StreamWriter(prefix+".raster.jsonl");
        long clocks=0,busEvents=0,instructions=0;int replayIndex=0,traceLimit=int.Parse(Opt("trace-limit","4096"));
        if(traceLimit<0)throw new ArgumentException("Trace limit must not be negative");
        machine.CyclePhaseObserved=phase=>{if(busEvents++<traceLimit)busTrace.WriteLine(JsonSerializer.Serialize(new {sequence=busEvents,origin="CPU",phase}));};
        machine.Clock.EventObserved=e=>rasterTrace.WriteLine(JsonSerializer.Serialize(new {masterClock=machine.Clock.Now,h=machine.Clock.H,v=machine.Clock.V,field=machine.Clock.Field,kind=e.Kind.ToString()}));
        var watch=Stopwatch.StartNew();
        while(machine.Frames<frames && machine.Clock.Now<clockLimit)
        {
            while(replayIndex<replay.Length && replay[replayIndex].Frame==machine.Frames) {var e=replay[replayIndex++];machine.Bus.Controllers.Buttons[e.Port]=e.Buttons;}
            machine.RunUntil(Math.Min(clockLimit,machine.Clock.NextEventClock),m=>
            {
                if(instructions++<traceLimit)cpuTrace.WriteLine(JsonSerializer.Serialize(new {frame=m.Frames,line=m.Clock.V,masterClock=m.Clock.Now,state=CpuVector.State(m.Cpu)}));
            });
        }
        clocks=machine.Clock.Now;machine.CyclePhaseObserved=null;machine.Clock.EventObserved=null;
        File.WriteAllBytes(prefix+".wram.bin",machine.Bus.Wram);
        using(var output=new BinaryWriter(File.Create(prefix+".argb32le.bin")))foreach(uint pixel in machine.Bus.Ppu.Pixels)output.Write(pixel);
        report.Metadata["cpuState"]=CpuVector.State(machine.Cpu);report.Metadata["completedFrames"]=machine.Frames;report.Metadata["masterclocks"]=clocks;report.Metadata["instructionsAttempted"]=instructions;
        report.Metadata["traceLimit"]=traceLimit;report.Metadata["busEvents"]=busEvents;report.Metadata["traceTruncated"]=busEvents>traceLimit||instructions>traceLimit;
        report.Metadata["stopReason"]=machine.Frames>=frames?"frame-limit":"masterclock-limit";
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
