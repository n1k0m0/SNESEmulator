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
// Runs Manager storage, session and IPC checks with isolated user data.
// Windows-only interface checks are explicitly separated from portable coverage.
//

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SNESEmulator.Contracts;
using SNESEmulator.Core;
using SNESEmulator.Management;
using SNESEmulator.SaveStates;
using SNESEmulator.Testing;

// The holder intentionally uses the documented path-derived serialization name.
// Its stdin lifetime lets the parent distinguish a true two-second contention
// failure from a crashed child or a failed disk transaction.
if (args.FirstOrDefault() == "--settings-hold")
{
    string target=Path.GetFullPath(args[1]);
    string identity=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(target.ToUpperInvariant())));
    using var mutex=new Mutex(false,"SNESEmulator.Settings."+identity);
    if(!mutex.WaitOne(TimeSpan.FromSeconds(2))) return 3;
    try { Console.WriteLine("LOCKED");Console.Out.Flush();Console.ReadLine(); }
    finally { mutex.ReleaseMutex(); }
    return 0;
}
if (args.FirstOrDefault() == "--settings-write-once")
{
    try
    {
        SNESEmulator.Runtime.SettingsPersistence.WriteOrdered(new EmulatorSettings{AudioVolume=int.Parse(args[2])}.Serialize(),args[1]);
        Console.WriteLine("SAVED");return 0;
    }
    catch(IOException e) { Console.WriteLine("BLOCKED "+e.Message);return 2; }
}

if (args.FirstOrDefault() == "--settings-write")
{
    Console.WriteLine("READY");
    Console.Out.Flush();
    Console.ReadLine();
    for (int i = 0; i < 30; i++)
        SNESEmulator.Runtime.SettingsPersistence.WriteOrdered(new EmulatorSettings { AudioVolume = int.Parse(args[2]) }.Serialize(), args[1]);
    return 0;
}

if (args.FirstOrDefault() == "--pipe-host")
{
    Environment.SetEnvironmentVariable("SNESEMULATOR_IPC_DIR", args[1]);
    await using var server = new IpcServer(async (r, t) =>
    {
        if (r.Command == "Delay")
        {
            File.WriteAllText(Path.Combine(args[1], "delay-entered"), "ready");
            try
            {
                await Task.Delay(60000, t);
            }
            finally
            {
                File.WriteAllText(Path.Combine(args[1], "delay-exited"), "joined");
            }

            return Response.Ok(r);
        }

        return r.Command == "Echo" ? Response.Ok(r, r.Payload) : Response.Fail(r, "UNSUPPORTED", "Unknown command");
    });
    Console.WriteLine(server.PipeName);
    Console.Out.Flush();
    await Console.In.ReadLineAsync();
    return 0;
}

string root = Path.Combine(Path.GetTempPath(), "SNESEmulator-manager-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("SNESEMULATOR_DATA_DIR", root);
Environment.SetEnvironmentVariable("SNESEMULATOR_IPC_DIR", root);
var report = new TestReport(args.Contains("--windows") ? "manager-windows" : "manager-portable");
report.Metadata["temporaryRoot"] = root;
int failures = 0;
void Check(bool value, string message = "Assertion failed")
{
    if (!value)
        throw new Exception(message);
}

async Task Test(string name, Func<Task> body)
{
    var watch = Stopwatch.StartNew();
    try
    {
        await body();
        report.Add(name, "pass", seconds: watch.Elapsed.TotalSeconds);
        Console.WriteLine("PASS " + name);
    }
    catch (Exception e)
    {
        failures++;
        report.Add(name, "fail", e.ToString(), watch.Elapsed.TotalSeconds);
        Console.WriteLine("FAIL " + name + ": " + e);
    }
}

Process Child(params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (string a in arguments)
        start.ArgumentList.Add(a);
    return Process.Start(start)!;
}

async Task Throws<T>(Func<Task> action)
    where T : Exception
{
    try
    {
        await action();
        throw new Exception("Expected " + typeof(T).Name);
    }
    catch (T)
    {
    }
}

if (!args.Contains("--windows"))
{
    await Test("M9e framing strict UTF8 null duplicates depth and budget", async () =>
    {
        foreach (string json in new[]
        {
            "{}",
            "{\"Payload\":{}}",
            "{\"Version\":1,\"RequestId\":\"00000000000000000000000000000000\",\"Command\":\"Echo\"}",
            "null",
            "{\"Version\":1,\"RequestId\":null,\"Command\":null,\"Payload\":{}}",
            "{\"Version\":1,\"Version\":1}"
        }

        )
        {
            using var memory = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            byte[] prefix = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
            memory.Write(prefix);
            memory.Write(bytes);
            memory.Position = 0;
            try
            {
                var r = await Framing.ReadAsync<Request>(memory, default);
                Framing.Validate(r);
                throw new Exception("Accepted malformed request");
            }
            catch (Exception e)when (e is InvalidDataException or JsonException)
            {
            }
        }

        foreach (string json in new[]
        {
            new string ('[', 17) + "0" + new string (']', 17),
            "{\"Long\":\"" + new string ('a', 65537) + "\"}",
            "[" + string.Join(',', Enumerable.Repeat("0", 122369)) + "]",
            "{" + string.Join(',', Enumerable.Range(0, 65).Select(i => "\"k" + i + "\":0")) + "}"
        }

        )
        {
            using var memory = new MemoryStream();
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            byte[] prefix = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
            memory.Write(prefix);
            memory.Write(bytes);
            memory.Position = 0;
            try
            {
                _ = await Framing.ReadAsync<JsonElement>(memory, default);
                throw new Exception("Accepted budget violation");
            }
            catch (Exception e)when (e is InvalidDataException or JsonException)
            {
            }
        }

        using var invalid = new MemoryStream(new byte[] { 2, 0, 0, 0, 0xc0, 0xff });
        await Throws<DecoderFallbackException>(async () => _ = await Framing.ReadAsync<Request>(invalid, default));
        using var oversized = new MemoryStream(new byte[] { 255, 255, 255, 127 });
        await Throws<InvalidDataException>(async () => _ = await Framing.ReadAsync<Request>(oversized, default));
    });
    await Test("M9g historical IPC1 peers cannot apply structured settings",async()=>
    {
        int calls=0;await using var server=new IpcServer((request,token)=>{Interlocked.Increment(ref calls);return Task.FromResult(Response.Ok(request));});
        foreach(string command in new[]{"Hello","ApplySettings"})
        {
            using var pipe=new NamedPipeClientStream(".",server.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            using var deadline=new CancellationTokenSource(5000);await pipe.ConnectAsync(deadline.Token);
            var old=Request.Create(command) with{Version=1};await Framing.WriteAsync(pipe,old,deadline.Token);var response=await Framing.ReadAsync<Response>(pipe,deadline.Token);
            Check(!response.Success&&response.Code=="PROTOCOL_MISMATCH"&&response.Version==IpcProtocol.Version,"old peer rejected explicitly");
        }
        Check(calls==0,"historical peer mutated settings");
        using var current=Process.GetCurrentProcess();var oldInfo=new InstanceInfo(Environment.ProcessId,Discovery.StartIdentity(current),server.PipeName,1,Environment.ProcessPath??"",Discovery.StartIdentityKind);
        Check(!Discovery.IsAlive(oldInfo),"old discovery accepted");await Throws<IOException>(()=>new IpcClient(oldInfo).SendAsync(Request.Create("Settings")));
    });
    await HelloTests.Run(Test, Check);
    await Test("M9e canonical copierheader library notes favorites missing and relink", async () =>
    {
        string folder = Path.Combine(root, "roms");
        Directory.CreateDirectory(folder);
        byte[] rom = DiagnosticRom.Create();
        File.WriteAllBytes(Path.Combine(folder, "a.sfc"), rom);
        File.WriteAllBytes(Path.Combine(folder, "b.smc"), new byte[512].Concat(rom).ToArray());
        using var db = new LibraryStore(Path.Combine(root, "library.db"));
        await db.InitializeAsync();
        Check(await db.ScanAsync(folder) == 2);
        var list = await db.ListAsync();
        Check(list.Count == 2 && list[0].Hash == list[1].Hash);
        await db.UpdateAsync(list[0].Hash, true, "local note", true);
        var noted = await db.ListAsync("local note");
        Check(noted.Count == 2 && noted.All(x => x.Hash == list[0].Hash && x.Notes == "local note" && x.Favorite && x.LastPlayed != null));
        File.Move(list[0].Path, list[0].Path + ".moved");
        Check((await db.ListAsync()).Any(x => !x.Exists));
        await db.RelinkAsync(list[0], list[0].Path + ".moved");
        var relinked = await db.ListAsync();
        Check(relinked.Count == 2 && relinked.All(x => x.Exists));
    });
    await Test("M9e scan cancellation transaction rollback and oversized ROM", async () =>
    {
        string folder = Path.Combine(root, "rollback");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "a.sfc"), DiagnosticRom.Create());
        File.WriteAllBytes(Path.Combine(folder, "b.sfc"), AudioDiagnosticRom.Create());
        using var db = new LibraryStore(Path.Combine(root, "rollback.db"));
        await db.InitializeAsync();
        int inserted = 0;
        db.AfterScanInsert = count =>
        {
            inserted = count;
            throw new InvalidOperationException("Injected after real SQLite insert");
        };
        await Throws<InvalidOperationException>(async () => _ = await db.ScanAsync(folder));
        Check(inserted == 1 && (await db.ListAsync()).Count == 0);
        using var cancel = new CancellationTokenSource();
        db.AfterScanInsert = count =>
        {
            inserted = count;
            cancel.Cancel();
        };
        await Throws<OperationCanceledException>(async () => _ = await db.ScanAsync(folder, cancel.Token));
        Check(inserted == 1 && (await db.ListAsync()).Count == 0);
        db.AfterScanInsert = null;
        string emptyTree=Path.Combine(root,"empty-directories");Directory.CreateDirectory(emptyTree);for(int i=0;i<1030;i++)Directory.CreateDirectory(Path.Combine(emptyTree,i.ToString()));await Throws<InvalidDataException>(async()=>_=await db.ScanAsync(emptyTree));Check((await db.ListAsync()).Count==0);
        string oversized = Path.Combine(root, "oversized.sfc");
        using (var f = File.Create(oversized))
            f.SetLength(100000000);
        await Throws<InvalidDataException>(() => db.RelinkAsync(new("h", "t", "", false, "", null, false), oversized));
    });
    await Test("M9e corrupt database explicit retained recovery and metadata isolation", async () =>
    {
        string path = Path.Combine(root, "broken.db");
        byte[] bad = Encoding.UTF8.GetBytes("corrupt DB");
        File.WriteAllBytes(path, bad);
        using var db = new LibraryStore(path);
        await db.InitializeAsync();
        Check(db.Notice != null);
        await Throws<InvalidDataException>(async () => _ = await db.ListAsync());
        Check(File.ReadAllBytes(path).SequenceEqual(bad));
        await db.RecoverAsync();
        Check(db.Notice == null && Directory.GetFiles(root, "broken.db.corrupt-*").Length == 1);
        await db.SetStateMetadataAsync("sample.snessav", "New name", "hash");
        var metadata = await db.StateMetadataAsync();
        Check(metadata.Count == 1 && metadata.Values.Single().Name == "New name" && metadata.Values.Single().Hash == "hash" && (await db.ListAsync()).Count == 0);
    });
    await Test("M10c State10 SA1 preview import export and board mismatch",async()=>{
        var config=new Sa1Configuration();var cart=new Cartridge(Sa1DiagnosticRom.Create(),hardware:config);var m=new SnesSystem();m.Insert(cart);m.RunUntil(18000);
        string file=Path.Combine(root,"sa1.snessav"),export=Path.Combine(root,"sa1-export.snessav");StateSerializer.Save(m,file);Check(StateCatalog.ReadIdentity(file)==cart.Hash);var preview=StateCatalog.Preview(file,cart);Check(preview.Format==10&&preview.Error==null&&preview.Pixels!=null);StateCatalog.Import(file,export,cart);Check(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(export)));
        var other=new Cartridge(cart.Rom,hardware:config with{RamBytes=65536});Check(StateCatalog.Preview(file,other).Error!=null);await Throws<InvalidDataException>(async()=>{StateCatalog.Import(file,export,other);await Task.CompletedTask;});Check(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(export)));
    });
    await Test("M10b State10 SuperFX preview import export and board mismatch",async()=>{
        var config=new SuperFxConfiguration(SuperFxRevision.Gsu2);var cart=new Cartridge(SuperFxDiagnosticRom.Create(),hardware:config);var m=new SnesSystem();m.Insert(cart);m.RunUntil(18000);
        string file=Path.Combine(root,"superfx.snessav"),export=Path.Combine(root,"superfx-export.snessav");StateSerializer.Save(m,file);Check(StateCatalog.ReadIdentity(file)==cart.Hash);var preview=StateCatalog.Preview(file,cart);Check(preview.Format==10&&preview.Error==null&&preview.Pixels!=null);StateCatalog.Import(file,export,cart);Check(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(export)));
        var other=new Cartridge(cart.Rom,hardware:config with{Revision=SuperFxRevision.Gsu1});Check(StateCatalog.Preview(file,other).Error!=null);await Throws<InvalidDataException>(async()=>{StateCatalog.Import(file,export,other);await Task.CompletedTask;});Check(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(export)));
    });
    await Test("M10a State10 DSP1 catalog preview import export and firmware mismatch", async () =>
    {
        var config=Dsp1Configuration.SyntheticProgram(Dsp1DiagnosticRom.Firmware(),Dsp1Board.LoRom1MiB);
        var cart=new Cartridge(Dsp1DiagnosticRom.Create(Dsp1Board.LoRom1MiB),hardware:config);var machine=new SnesSystem();machine.Insert(cart);machine.RunUntil(201);
        string file=Path.Combine(root,"dsp1.snessav"),export=Path.Combine(root,"dsp1-export.snessav");StateSerializer.Save(machine,file);
        Check(StateCatalog.ReadIdentity(file)==cart.Hash);var preview=StateCatalog.Preview(file,cart);Check(preview.Error==null&&preview.Format==10&&preview.Pixels!=null);
        StateCatalog.Import(file,export,cart);Check(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(export)));
        byte[] other=Dsp1DiagnosticRom.Firmware();other[1]^=1;var different=new Cartridge(cart.Rom,hardware:Dsp1Configuration.SyntheticProgram(other,Dsp1Board.LoRom1MiB));
        Check(StateCatalog.Preview(file,different).Error!=null);await Throws<InvalidDataException>(async()=>{StateCatalog.Import(file,export,different);await Task.CompletedTask;});Check(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(export)));
    });
    await Test("M9e State9 strict preview import export recovery delete metadata byte identity", async () =>
    {
        var machine = new SnesSystem();
        var cart = new Cartridge(DiagnosticRom.Create());
        machine.Insert(cart);
        machine.RunFrame();
        string file = Path.Combine(root, "state.snessav");
        StateSerializer.Save(machine, file);
        byte[] original = File.ReadAllBytes(file);
        Check(StateCatalog.ReadIdentity(file) == cart.Hash);
        Check(StateCatalog.Preview(file, cart).Pixels?.Length == 256 * 224);
        string export = Path.Combine(root, "export.snessav");
        StateCatalog.Import(file, export, cart);
        Check(original.SequenceEqual(File.ReadAllBytes(export)));
        StateSerializer.Save(machine, file);
        File.WriteAllText(file, "damaged");
        Check(StateCatalog.Preview(file, cart).Error != null && StateCatalog.Preview(file, cart, true).Error == null);
        StateCatalog.Recover(file, cart);
        Check(StateCatalog.Preview(file, cart).Error == null && Directory.GetFiles(root, "state.snessav.corrupt-*").Length == 1);
        AtomicFile.DeleteLogical(file);
        Check(!File.Exists(file) && !File.Exists(file + ".bak"));
        await Task.CompletedTask;
    });
    await Test("Audit Manager reviews and explicitly recovers a backup when the primary is missing", async () =>
    {
        var cart=new Cartridge(DiagnosticRom.Create());var machine=new SnesSystem();machine.Insert(cart);
        string folder=Path.Combine(root,"backup-only-review");Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,"missing-primary.snessav");
        StateSerializer.Save(machine,file);StateSerializer.Save(machine,file);
        byte[] backup=File.ReadAllBytes(file+".bak");File.Delete(file);
        Check(StateCatalog.List(folder).Contains(file),"backup-only logical save is listed");
        await Throws<FileNotFoundException>(()=>{StateCatalog.ReadIdentity(file);return Task.CompletedTask;});
        Check(StateCatalog.ReadIdentityForReview(file)==cart.Hash,"Manager identifies the backup cartridge for review");
        var preview=StateCatalog.Preview(file,cart);
        Check(preview.Error!=null&&preview.RecoveryAvailable,"normal preview requires explicit recovery");
        Check(StateCatalog.Preview(file,cart,true).Error==null,"complete backup validation succeeds");
        Check(!File.Exists(file)&&File.ReadAllBytes(file+".bak").SequenceEqual(backup),"review does not restore or mutate files");
        StateCatalog.Recover(file,cart);
        Check(File.ReadAllBytes(file).SequenceEqual(backup)&&StateCatalog.ReadIdentity(file)==cart.Hash,"explicit recovery restores exact original bytes");
        File.Delete(file);File.WriteAllText(file+".bak","damaged backup");
        await Throws<InvalidDataException>(()=>{StateCatalog.ReadIdentityForReview(file);return Task.CompletedTask;});
        Check(StateCatalog.Preview(file,cart,true).Error!=null&&!File.Exists(file),"invalid backup is rejected without restoration");
    });
    await Test("M9e separate process discovery handshake unsupported parallel and PID reuse", async () =>
    {
        using var child = Child("--pipe-host", root);
        try
        {
            string? name = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(name != null);
            var info = Discovery.List().Single(x => x.ProcessId == child.Id);
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => new IpcClient(info).SendAsync(Request.Create("Echo", new { Index = i }))));
            Check(results.Length == 12 && results.Select(x => x.Value<JsonElement>().GetProperty("Index").GetInt32()).Order().SequenceEqual(Enumerable.Range(0, 12)) && results.All(x => x.Success));
            Check((await new IpcClient(info).SendAsync(Request.Create("Unknown"))).Code == "UNSUPPORTED");
            Check(!Discovery.IsAlive(info with { StartTicks = info.StartTicks + 1 }));
            Check(!Discovery.IsAlive(info with { StartIdentityKind = "foreign-identity-kind" }));
            await child.StandardInput.WriteLineAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(child.ExitCode == 0 && !Discovery.IsAlive(info));
            Check(!Discovery.List().Any(x => x.ProcessId == child.Id));
        }
        finally
        {
            if (!child.HasExited)
            {
                await child.StandardInput.WriteLineAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
        }
    });
    await Test("M9e real pipe malformed clients cannot consume loops and version hello errors", async () =>
    {
        using var child = Child("--pipe-host", root);
        try
        {
            string name = (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!;
            var info = Discovery.List().Single(x => x.ProcessId == child.Id);
            var malformed = new[]
            {
                Encoding.UTF8.GetBytes("{}"),
                new byte[]
                {
                    0xc0,
                    0xff
                },
                Encoding.UTF8.GetBytes(new string ('[', 17) + "0" + new string (']', 17)),
                Encoding.UTF8.GetBytes("{\"Version\":1,\"RequestId\":null,\"Command\":null,\"Payload\":{}}")
            };
            for (int i = 0; i < 8; i++)
            {
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var deadline = new CancellationTokenSource(5000);
                await pipe.ConnectAsync(deadline.Token);
                byte[] bytes = malformed[i % malformed.Length];
                byte[] prefix = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(prefix, i==7?Framing.MaximumBytes+1:bytes.Length);
                await pipe.WriteAsync(prefix, deadline.Token);
                if(i!=7)await pipe.WriteAsync(bytes, deadline.Token);
                byte[] one = new byte[1];
                Check(await pipe.ReadAsync(one, deadline.Token) == 0);
            }

            Check((await new IpcClient(info).SendAsync(Request.Create("Echo"))).Success);
            foreach (var r in new[]
            {
                Request.Create("Hello")with
                {
                    Version = 99
                },
                Request.Create("Echo")
            }

            )
            {
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var deadline = new CancellationTokenSource(5000);
                await pipe.ConnectAsync(deadline.Token);
                await Framing.WriteAsync(pipe, r, deadline.Token);
                var response = await Framing.ReadAsync<Response>(pipe, deadline.Token);
                Check(!response.Success && response.Code == (r.Version == 99 ? "PROTOCOL_MISMATCH" : "HELLO_REQUIRED"));
            }
        }
        finally
        {
            await child.StandardInput.WriteLineAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }
    });
    await Test("M9e pipe cancellation and joined shutdown during pending request", async () =>
    {
        using var child = Child("--pipe-host", root);
        try
        {
            _ = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var info = Discovery.List().Single(x => x.ProcessId == child.Id);
            using var token = new CancellationTokenSource();
            var delay = new IpcClient(info).SendAsync(Request.Create("Delay"), token.Token);
            var watch = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, "delay-entered")))
            {
                if (watch.Elapsed.TotalSeconds > 10)
                    throw new TimeoutException("Handler entry barrier");
                await Task.Delay(10);
            }

            token.Cancel();
            await Throws<OperationCanceledException>(() => delay);
            watch.Restart();
            while (!File.Exists(Path.Combine(root, "delay-exited")))
            {
                if (watch.Elapsed.TotalSeconds > 3)
                    throw new TimeoutException("Disconnect did not promptly cancel and join handler");
                await Task.Delay(10);
            }

            await child.StandardInput.WriteLineAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(child.ExitCode == 0);
        }
        finally
        {
            if (!child.HasExited)
            {
                await child.StandardInput.WriteLineAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
        }
    });
    await Test("M9e actual partial-packet timeout preserves fixed server loops", async () =>
    {
        using var child = Child("--pipe-host", root);
        try
        {
            string name = (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!;
            var info = Discovery.List().Single(i => i.ProcessId == child.Id);
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var deadline = new CancellationTokenSource(20000);
            await pipe.ConnectAsync(deadline.Token);
            await pipe.WriteAsync(new byte[] { 100, 0, 0, 0, 123 }, deadline.Token);
            byte[] one = new byte[1];
            var watch = Stopwatch.StartNew();
            Check(await pipe.ReadAsync(one, deadline.Token) == 0 && watch.Elapsed.TotalSeconds >= 14, "Partial packet must actually reach15second timeout");
            Check((await new IpcClient(info).SendAsync(Request.Create("Echo"))).Success);
        }
        finally
        {
            await child.StandardInput.WriteLineAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }
    });
    await Test("R1 settings contention budget preserves bytes and permits a real-process retry",async()=>
    {
        string path=Path.Combine(root,"settings-budget.json");
        SNESEmulator.Runtime.SettingsPersistence.WriteOrdered(new EmulatorSettings{AudioVolume=20}.Serialize(),path);
        byte[] original=File.ReadAllBytes(path);
        using var holder=Child("--settings-hold",path);
        try
        {
            Check(await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))=="LOCKED","serialization holder did not start");
            // Include child startup: parent scheduling delays must never shorten
            // our observation of the child's full two-second mutex wait.
            var elapsed=Stopwatch.StartNew();
            using var contender=Child("--settings-write-once",path,"70");
            var error=contender.StandardError.ReadToEndAsync();
            var output=contender.StandardOutput.ReadToEndAsync();
            await contender.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            string text=await output;
            Check(contender.ExitCode==2&&text.StartsWith("BLOCKED Another settings write exceeded"),"contention was not the explicit bounded failure: "+text+await error);
            Check(elapsed.Elapsed>=TimeSpan.FromSeconds(1.8),"serialization budget was skipped");
            Check(File.ReadAllBytes(path).SequenceEqual(original)&&!File.Exists(path+".bak"),"failed contender changed a generation");
            report.Metadata["settingsBudget"]=new{HolderPid=holder.Id,ContenderPid=contender.Id,Seconds=elapsed.Elapsed.TotalSeconds,ExitCode=contender.ExitCode,Output=text};
        }
        finally
        {
            await holder.StandardInput.WriteLineAsync();
            await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        }
        Check(holder.ExitCode==0,"serialization holder failed");
        using var retry=Child("--settings-write-once",path,"70");
        await retry.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Check(retry.ExitCode==0&&EmulatorSettings.Load(path).AudioVolume==70&&File.ReadAllBytes(path+".bak").SequenceEqual(original),"retry lost current or previous settings generation");
    });
    await Test("M9e two actual processes serialize settings generations", async () =>
    {
        string path = Path.Combine(root, "concurrent-settings.json");
        using var first = Child("--settings-write", path, "20");
        using var second = Child("--settings-write", path, "70");
        var children = new[] { first, second };
        var tokens = children.Select(Discovery.StartIdentity).ToArray();
        var errors = children.Select(p => p.StandardError.ReadToEndAsync()).ToArray();
        var elapsed = Stopwatch.StartNew();
        var observations = new List<Dictionary<string, object?>>();
        void Observe(string phase)
        {
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                var item = new Dictionary<string, object?> { ["phase"] = phase, ["pid"] = child.Id, ["identityKind"] = Discovery.StartIdentityKind, ["startToken"] = tokens[i], ["wallSeconds"] = elapsed.Elapsed.TotalSeconds };
                try
                {
                    item["exited"] = child.HasExited;
                    item["exitCode"] = child.HasExited ? child.ExitCode : null;
                    item["cpuSeconds"] = child.TotalProcessorTime.TotalSeconds;
                }
                catch (Exception e) { item["processObservationError"] = e.Message; }
                if (OperatingSystem.IsLinux())
                    foreach (string name in new[] { "stat", "io", "wchan" })
                        try { item[name] = File.ReadAllText($"/proc/{child.Id}/{name}"); }
                        catch (IOException e) { item[name] = e.Message; }
                observations.Add(item);
            }
            File.WriteAllText(Path.Combine(root, "settings-children.json"), JsonSerializer.Serialize(observations));
        }
        Exception? failure = null;
        try
        {
            Observe("started");
            Check(await first.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "READY" && await second.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "READY");
            await first.StandardInput.WriteLineAsync();
            await second.StandardInput.WriteLineAsync();
            // This remains the original success deadline. A later normal exit never
            // turns a timeout green; the extra wait only preserves its actual cause.
            await Task.WhenAll(children.Select(p => p.WaitForExitAsync())).WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (Exception e)
        {
            failure = e;
            Observe("failed-deadline");
            foreach (var child in children)
                try { child.StandardInput.Close(); }
                catch (IOException closeError) { observations.Add(new() { ["phase"] = "input-close-error", ["pid"] = child.Id, ["error"] = closeError.Message }); }
            try { await Task.WhenAll(children.Select(p => p.WaitForExitAsync())).WaitAsync(TimeSpan.FromSeconds(60)); }
            catch (TimeoutException) { Observe("drain-deadline"); }
        }
        finally
        {
            for (int i = 0; i < children.Length; i++)
                try
                {
                    if (!children[i].HasExited && Discovery.StartIdentity(children[i]) == tokens[i]) children[i].Kill(entireProcessTree: true);
                }
                catch (Exception e) when ((e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) && children[i].HasExited) { }
            await Task.WhenAll(children.Select(p => p.WaitForExitAsync())).WaitAsync(TimeSpan.FromSeconds(10));
            Observe("joined");
            for (int i = 0; i < errors.Length; i++) File.WriteAllText(Path.Combine(root, $"settings-child-{children[i].Id}.stderr.log"), await errors[i].WaitAsync(TimeSpan.FromSeconds(5)));
            report.Metadata["settingsChildren"] = observations;
        }
        string firstError = await errors[0], secondError = await errors[1];
        if (failure != null) throw new Exception($"Settings child success deadline failed; actual final exits {first.ExitCode}/{second.ExitCode}; first={firstError}; second={secondError}; diagnostics {Path.Combine(root, "settings-children.json")}", failure);
        Check(first.ExitCode == 0 && second.ExitCode == 0 && new[] { 20, 70 }.Contains(EmulatorSettings.Load(path).AudioVolume) && File.Exists(path + ".bak"), $"Settings child exits {first.ExitCode}/{second.ExitCode}; first={firstError}; second={secondError}");
    });
}
else
{
    await WindowsTests.Run(Test, Check, root);
}

report.Metadata["limits"] = "Synthetic apphooks and real local processes/pipes; no OS focus, physical input, console or clean VM evidence";
report.Save(Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT") ?? "artifacts/m9e-" + (args.Contains("--windows") ? "windows" : "portable"));
return failures == 0 ? 0 : 1;
