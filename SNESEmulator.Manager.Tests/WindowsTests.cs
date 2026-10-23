using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using SNESEmulator.Contracts;
using SNESEmulator.Core;
using SNESEmulator.Management;

internal static class WindowsTests
{
    internal static async Task Run(Func<string, Func<Task>, Task> test, Action<bool, string> check, string root)
    {
        byte[] BatteryRom()
        {
            byte[] rom = DiagnosticRom.Create();
            rom[0x7fd8] = 1;
            byte[] code = [0x78, 0xaf, 0, 0, 0x70, 0x1a, 0x8f, 0, 0, 0x70, 0x80, 0xf5];
            code.CopyTo(rom, 0x2000);
            rom[0x7ffc] = 0;
            rom[0x7ffd] = 0xa0;
            return rom;
        }

        byte[] Hardware(string path)
        {
            using var stream = File.OpenRead(path);
            return SNESEmulator.SaveStates.StateSerializer.Decode(stream);
        }

        string repo = Directory.GetCurrentDirectory();
        string? package = Environment.GetEnvironmentVariable("SNESEMULATOR_TEST_PACKAGE");
        string exe = package == null ? Path.Combine(repo, "SNESEmulator/bin/Release/net10.0-windows/SNESEmulator.exe") : Path.Combine(package, "SNESEmulator.exe");
        string manager = package == null ? Path.Combine(repo, "SNESEmulatorManager/bin/Release/net10.0-windows/SNESEmulatorManager.exe") : Path.Combine(package, "Manager/SNESEmulatorManager.exe");
        var directories = new[]
        {
            "SNESEmulator",
            "SNESEmulatorManager",
            "SNESEmulator.Contracts",
            "SNESEmulator.Management",
            "SNESEmulator.Runtime",
            "SNESEmulator.Core"
        };
        Dictionary<string, string> Identities() => (package == null ? directories.SelectMany(d => Directory.EnumerateFiles(Path.Combine(repo, d, "bin", "Release"), "*", SearchOption.AllDirectories)) : Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories)).Where(p => p.EndsWith(".dll") || p.EndsWith(".exe") || p.EndsWith(".deps.json") || p.EndsWith(".runtimeconfig.json")).ToDictionary(p => Path.GetRelativePath(repo, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var before = Identities();
        File.WriteAllText(Path.Combine(root, "native-before.json"), JsonSerializer.Serialize(before));
        async Task Wait(Func<bool> condition, string message, int seconds = 30)
        {
            var w = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    if (condition())
                        return;
                }
                catch (IOException)
                {
                }

                if (w.Elapsed.TotalSeconds > seconds)
                    throw new TimeoutException(message);
                await Task.Delay(25);
            }
        }

        var children = new List<Process>();
        var logs = new List<Task>();
        Process Start(string executable, string name, params string[] args) => StartInDataRoot(executable, name, root, args);
        Process StartInDataRoot(string executable, string name, string dataRoot, params string[] args)
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = repo
            };
            if (package != null) { start.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System); start.Environment["DOTNET_ROOT"] = Path.Combine(root, "no-sdk"); start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0"; }
            start.Environment["SNESEMULATOR_DATA_DIR"] = dataRoot;
            start.Environment["SNESEMULATOR_IPC_DIR"] = dataRoot;
            start.Environment["SNESEMULATOR_MANAGER_TEST_ID"] = Path.GetFileName(dataRoot);
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            var launchIdentity = Identities();
            File.WriteAllText(Path.Combine(root, name + ".identity-before.json"), JsonSerializer.Serialize(launchIdentity));
            var p = Process.Start(start)!;
            children.Add(p);
            var exited = p.WaitForExitAsync();
            logs.Add(exited.ContinueWith(_ => File.WriteAllText(Path.Combine(root, name + ".identity-after.json"), JsonSerializer.Serialize(Identities()))));
            int launchedPid = p.Id; long launchedStart = p.StartTime.ToUniversalTime().Ticks;
            logs.Add(exited.ContinueWith(_ => File.WriteAllText(Path.Combine(root, name + ".exit.json"), JsonSerializer.Serialize(new { Pid = launchedPid, StartTicks = launchedStart, ExitCode = p.ExitCode, ExitUtc = p.ExitTime.ToUniversalTime(), RecordedUtc = DateTimeOffset.UtcNow }))));
            logs.Add(p.StandardOutput.ReadToEndAsync().ContinueWith(t => File.WriteAllText(Path.Combine(root, name + ".stdout.log"), t.Result)));
            logs.Add(p.StandardError.ReadToEndAsync().ContinueWith(t => File.WriteAllText(Path.Combine(root, name + ".stderr.log"), t.Result)));
            return p;
        }

        async Task<Response> Send(InstanceInfo info, string command, object? payload = null)
        {
            if (!Discovery.IsAlive(info))
            {
                string observed;
                try { using var process = Process.GetProcessById(info.ProcessId); observed = $"Exited={process.HasExited} observedStart={Discovery.StartIdentity(process)} kind={Discovery.StartIdentityKind}"; } catch (Exception e) { observed = e.ToString(); }
                throw new IOException($"Before {command}: selected={JsonSerializer.Serialize(info)} observed={observed}");
            }
            return await new IpcClient(info).SendAsync(Request.Create(command, payload));
        }

        async Task<StatusInfo> Status(InstanceInfo info)
        {
            var r = await Send(info, "Status");
            check(r.Success, "status failed:" + r.Message);
            return r.Value<StatusInfo>();
        }

        string aDir = Path.Combine(root, "emulator-a"), bDir = Path.Combine(root, "emulator-b");
        Directory.CreateDirectory(aDir);
        Directory.CreateDirectory(bDir);
        Process? a = null, b = null;
        InstanceInfo? ai = null, bi = null;
        try
        {
            await test("M9g real offline WPF unknown bool string object disabled and preserved on Apply",async()=>
            {
                string data=Path.Combine(root,"unknown settings apply");Directory.CreateDirectory(data);
                using var future=JsonDocument.Parse("{\"array\":[1,true,\"keep\"],\"number\":1.2300}");
                var settings=new EmulatorSettings{AudioVolume=27,AdditionalSettings=new(){["FutureEnabled"]=JsonSerializer.SerializeToElement(true),["FutureText"]=JsonSerializer.SerializeToElement("future\ntext \u2603"),["FutureObject"]=future.RootElement.Clone()}};
                string path=Path.Combine(data,"settings.json");settings.Save(path);
                var process=StartInDataRoot(manager,"unknown-settings-apply",data,"--settings-extensions-smoke",data);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));check(process.ExitCode==0,"Unknown-settings WPF probe failed");
                using var result=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data,"result.json")));var r=result.RootElement;
                check(r.GetProperty("Success").GetBoolean()&&r.GetProperty("Volume").GetInt32()==55,"Actual offline apply missing");
                foreach(string name in new[]{"FutureEnabled","FutureText","FutureObject"})check(r.GetProperty("Disabled").GetProperty(name).GetBoolean()&&r.GetProperty("Before").GetProperty(name).GetString()==r.GetProperty("After").GetProperty(name).GetString(),"Unknown JSON changed or remained editable: "+name);
                var loaded=EmulatorSettings.Load(path);check(loaded.AudioVolume==55&&loaded.AdditionalSettings!["FutureEnabled"].GetBoolean()&&loaded.AdditionalSettings["FutureText"].GetString()=="future\ntext \u2603"&&loaded.AdditionalSettings["FutureObject"].GetProperty("number").GetRawText()=="1.2300","Exact persisted future values");
                check(new FileInfo(Path.Combine(data,"settings-extensions.png")).Length>1000,"WPF rendering absent");
            });
            await test("M9e real offline WPF settings recovery unavailable normalization visible and read-only", async () =>
            {
                foreach (string mode in new[] { "recovered", "unavailable", "normalized" })
                {
                    string data = Path.Combine(root, "offline-" + mode);
                    Directory.CreateDirectory(data);
                    string path = Path.Combine(data, "settings.json");
                    if (mode == "recovered")
                    {
                        new EmulatorSettings { AudioVolume = 23 }.Save(path);
                        new EmulatorSettings { AudioVolume = 24 }.Save(path);
                        File.WriteAllText(path, "corrupt primary retained");
                    }
                    else File.WriteAllText(path, mode == "normalized" ? JsonSerializer.Serialize(new EmulatorSettings { AudioVolume = 999 }) : "unreadable settings retained");
                    byte[] primary = File.ReadAllBytes(path);
                    byte[]? backup = File.Exists(path + ".bak") ? File.ReadAllBytes(path + ".bak") : null;
                    var process = StartInDataRoot(manager, "offline-" + mode, data, "--settings-smoke", data);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    check(process.ExitCode == 0, "Offline WPF probe failed " + mode);
                    using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "result.json")));
                    var r = result.RootElement;
                    string marker = mode == "recovered" ? "RECOVERED" : mode == "normalized" ? "NORMALIZED" : "UNAVAILABLE";
                    check(r.GetProperty("Visible").GetBoolean() && r.GetProperty("Warning").GetString()!.Contains(marker), "Missing visible warning " + mode);
                    check(r.GetProperty("Volume").GetString() == (mode == "recovered" ? "23" : mode == "normalized" ? "100" : "80"), "Wrong recovered editor value " + mode);
                    check(r.GetProperty("ReadThread").GetInt32() != r.GetProperty("UiThread").GetInt32(), "Settings read blocked dispatcher");
                    check(primary.SequenceEqual(File.ReadAllBytes(path)) && (backup == null ? !File.Exists(path + ".bak") : backup.SequenceEqual(File.ReadAllBytes(path + ".bak"))), "Read changed settings source " + mode);
                    check(new FileInfo(Path.Combine(data, "settings.png")).Length > 1000, "Actual WPF rendering missing");
                }
            });
            await test("M9e real emulator two instance discovery exact target and transactional ROM load", async () =>
            {
                a = Start(exe, "emulator-a", "--manager-ipc-probe", aDir);
                b = Start(exe, "emulator-b", "--manager-ipc-probe", bDir);
                await Wait(() => File.Exists(Path.Combine(aDir, "ready.json")) && File.Exists(Path.Combine(bDir, "ready.json")), "Both emulator windows ready");
                ai = Discovery.List().Single(i => i.ProcessId == a.Id);
                bi = Discovery.List().Single(i => i.ProcessId == b.Id);
                check(ai.StartTicks != 0 && bi.StartTicks != 0 && ai.PipeName != bi.PipeName, "distinct discovery identity");
                string first = Path.Combine(root, "a.sfc"), second = Path.Combine(root, "b.sfc");
                File.WriteAllBytes(first, DiagnosticRom.Create());
                File.WriteAllBytes(second, BatteryRom());
                check((await Send(ai, "LoadRom", new PathCommand(first))).Success, "loadA");
                check((await Send(bi, "LoadRom", new PathCommand(second))).Success, "loadB");
                var sa = await Status(ai);
                var sb = await Status(bi);
                check(sa.CartridgeHash == new Cartridge(DiagnosticRom.Create()).Hash && sb.CartridgeHash == new Cartridge(BatteryRom()).Hash && sa.ProcessId == a.Id && sb.ProcessId == b.Id, "wrong selected instance");
                var ready = Stopwatch.StartNew();
                while ((await Status(ai)).Field < 8 || (await Status(bi)).Field < 8)
                {
                    if (ready.Elapsed.TotalSeconds > 15)
                        throw new TimeoutException("Eight real worker fields");
                    await Task.Delay(25);
                }
            });
            if (ai != null && bi != null)
            {
                await test("M9e acknowledged settings menu conflict invalid values mode and stale epoch", async () =>
                {
                    var original = (await Send(ai, "Settings")).Value<SettingsInfo>();
                    var settings = original.Settings.Deserialize<EmulatorSettings>()!;
                    settings.AudioVolume = 50;
                    var applied = await Send(ai, "ApplySettings", new ApplySettings(original.Revision, JsonSerializer.SerializeToElement(settings)));
                    check(applied.Success, "apply failed" + applied.Message);
                    var ack = applied.Value<SettingsInfo>();
                    check(ack.Settings.GetProperty("AudioVolume").GetInt32() == 50 && ack.Revision > original.Revision, "Ack values");
                    File.WriteAllText(Path.Combine(aDir, "menu-volume.tmp"), "request");
                    File.Move(Path.Combine(aDir, "menu-volume.tmp"), Path.Combine(aDir, "menu-volume"));
                    await Wait(() => File.Exists(Path.Combine(aDir, "menu-ack")), "actual menu mutation barrier");
                    var conflict = await Send(ai, "ApplySettings", new ApplySettings(ack.Revision, ack.Settings));
                    check(!conflict.Success && conflict.Code == "REVISION_CONFLICT", "lost concurrent menu update");
                    var current = (await Send(ai, "Settings")).Value<SettingsInfo>();
                    check(current.Settings.GetProperty("AudioVolume").GetInt32() == 60, "menu setting not authoritative");
                    settings = current.Settings.Deserialize<EmulatorSettings>()!;
                    settings.AudioVolume = 101;
                    check(!(await Send(ai, "ApplySettings", new ApplySettings(current.Revision, JsonSerializer.SerializeToElement(settings)))).Success, "invalid settings accepted");
                    check((await Send(ai, "Mode", new { Paused = true, Turbo = false })).Value<StatusInfo>().Paused, "mode Ack before pause");
                    var state = await Status(ai);
                    var stale = await Send(ai, "SaveState", new PathCommand(Path.Combine(root, "stale.snessav"), state.Epoch - 1));
                    check(!stale.Success && stale.Code == "STALE_EPOCH" && !File.Exists(Path.Combine(root, "stale.snessav")), "stale mutation accepted");
                });
                await test("M9e IPC State9 capture preview corrupt isolation recovery sameROM and lease rollback", async () =>
                {
                    string state = Path.Combine(root, "ipc.snessav");
                    check((await Send(ai, "SaveState", new PathCommand(state))).Success, "save failed");
                    check((await Send(ai, "StatePreview", new PathCommand(state))).Value<StateInfo>().Pixels?.Length == 256 * 224, "actual preview");
                    check((await Send(ai, "SaveState", new PathCommand(state))).Success, "second generation");
                    File.WriteAllText(state, "corrupt");
                    var beforeState = await Status(ai);
                    string hardwareBefore = Path.Combine(root, "before-corrupt.snessav"), hardwareAfter = Path.Combine(root, "after-corrupt.snessav");
                    check((await Send(ai, "SaveState", new PathCommand(hardwareBefore))).Success, "before exact capture");
                    check(!(await Send(ai, "LoadState", new PathCommand(state))).Success, "corrupt strict accepted");
                    check((await Status(ai)).CartridgeHash == beforeState.CartridgeHash, "corrupt load changed cartridge");
                    check((await Send(ai, "SaveState", new PathCommand(hardwareAfter))).Success && Hardware(hardwareBefore).SequenceEqual(Hardware(hardwareAfter)), "rejected state changed paused hardware/SRAM bytes");
                    check((await Send(ai, "LoadState", new PathCommand(state, Recover: true))).Success, "validated recovery load");
                    check((await Send(ai, "RecoverState", new PathCommand(state))).Success, "explicit recovery install");
                    check((await Send(ai, "LoadRom", new PathCommand(Path.Combine(root, "a.sfc")))).Success, "same ROM reload");
                    check((await Send(bi, "Mode", new { Paused = true, Turbo = false })).Success, "pause B");
                    string leaseBefore = Path.Combine(root, "before-lease.snessav"), leaseAfter = Path.Combine(root, "after-lease.snessav");
                    check((await Send(bi, "SaveState", new PathCommand(leaseBefore))).Success, "capture battery hardware before lease");
                    var occupied = await Send(bi, "LoadRom", new PathCommand(Path.Combine(root, "a.sfc")));
                    check(!occupied.Success && (await Status(bi)).CartridgeHash == new Cartridge(BatteryRom()).Hash, "lease rejection did not retain prior session");
                    check((await Send(bi, "SaveState", new PathCommand(leaseAfter))).Success && Hardware(leaseBefore).SequenceEqual(Hardware(leaseAfter)), "failed lease changed hardware/SRAM");
                    var beforeReload = SNESEmulator.SaveStates.StateSerializer.Load(new Cartridge(BatteryRom()), leaseAfter).Bus.Cartridge!.Sram.ToArray();
                    check((await Send(bi, "LoadRom", new PathCommand(Path.Combine(root, "b.sfc")))).Success, "same SRAM ROM reload");
                    string reload = Path.Combine(root, "same-rom.snessav");
                    check((await Send(bi, "SaveState", new PathCommand(reload))).Success, "capture reloaded SRAM");
                    check(beforeReload.SequenceEqual(SNESEmulator.SaveStates.StateSerializer.Load(new Cartridge(BatteryRom()), reload).Bus.Cartridge!.Sram), "same ROM SRAM lost");
                    check((await Send(ai, "DeleteState", new PathCommand(state))).Success && !File.Exists(state) && !File.Exists(state + ".bak"), "logical delete");
                });
                await test("M9e actual WPF lifecycle all pages screenshot and exact connect argument", async () =>
                {
                    using (var db = new LibraryStore(Path.Combine(root, "manager", "library.db")))
                    {
                        await db.InitializeAsync();
                        await db.ScanAsync(root);
                    }

                    string stateFolder = Path.Combine(root, "saves", new Cartridge(DiagnosticRom.Create()).Hash);
                    Directory.CreateDirectory(stateFolder);
                    StateCatalog.Import(Path.Combine(root, "before-corrupt.snessav"), Path.Combine(stateFolder, "manager-view.snessav"), new Cartridge(DiagnosticRom.Create()));
                    StateCatalog.Import(Path.Combine(root, "before-corrupt.snessav"), Path.Combine(stateFolder, "manager-view.snessav"), new Cartridge(DiagnosticRom.Create()));
                    string output = Path.Combine(root, "manager-native");
                    var p = Start(manager, "manager-smoke", "--connect", ai.ProcessId.ToString(), "--manager-smoke", output);
                    await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    check(p.ExitCode == 0 && File.Exists(Path.Combine(output, "result.json")), "Manager smoke exited incorrectly");
                    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "result.json")));
                    check(doc.RootElement.GetProperty("WindowLoaded").GetBoolean() && doc.RootElement.GetProperty("Tabs").GetInt32() == 5 && doc.RootElement.GetProperty("SelectedPid").GetInt32() == ai.ProcessId, "WPF apphooks selection");
                    check(Directory.GetFiles(output, "tab-*.png").Length == 5, "all actual rendered pages");
                });
                await test("M9e single manager forwards queued activation to exact second instance", async () =>
                {
                    string output = Path.Combine(root, "manager-single");
                    var primary = Start(manager, "manager-primary", "--connect", ai.ProcessId.ToString(), "--manager-probe", output);
                    await Wait(() => File.Exists(Path.Combine(output, "ready.json")), "Primary manager ready");
                    check((await Send(ai, "Mode", new { Paused = false, Turbo = false })).Success, "unpause before F10");
                    File.WriteAllText(Path.Combine(aDir, "launch-manager.tmp"), "request");
                    File.Move(Path.Combine(aDir, "launch-manager.tmp"), Path.Combine(aDir, "launch-manager"));
                    await Wait(() => File.Exists(Path.Combine(output, "activation.jsonl")), "F10 manager activation delivery");
                    check(!(await Status(ai)).MenuOpen, "F10 left menu open");
                    var resumed = Stopwatch.StartNew();
                    while ((await Status(ai)).Paused)
                    {
                        if (resumed.Elapsed.TotalSeconds > 5)
                            throw new TimeoutException("F10 manager launch left emulator paused");
                        await Task.Delay(25);
                    }

                    var hold = Start(manager, "manager-forward-hold", "--probe-hold", "1500");
                    await hold.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    check(hold.ExitCode == 0, "forward hold Ack");
                    await Wait(() => File.Exists(Path.Combine(output, "hold-entered")), "active operation barrier");
                    var second = Start(manager, "manager-forward-connect", "--connect", bi.ProcessId.ToString());
                    await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    check(second.ExitCode == 0, "second process did not acknowledge queued arguments");
                    await Wait(() => File.Exists(Path.Combine(output, "activation.jsonl")) && File.ReadAllText(Path.Combine(output, "activation.jsonl")).Contains("\"SelectedPid\":" + bi.ProcessId), "queued activation target");
                    File.WriteAllText(Path.Combine(output, "close.tmp"), "close");
                    File.Move(Path.Combine(output, "close.tmp"), Path.Combine(output, "close"));
                    await primary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    check(primary.ExitCode == 0, "primary shutdown");
                });
                await test("M9e actual remote network client rejects hardware and active network settings", async () =>
                {
                    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                    listener.Start();
                    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                    listener.Stop();
                    foreach (var tuple in new[]
                    {
                        (aDir, "host-port"),
                        (bDir, "join-port")
                    }

                    )
                    {
                        string target = Path.Combine(tuple.Item1, tuple.Item2);
                        File.WriteAllText(target + ".tmp", port.ToString());
                        File.Move(target + ".tmp", target);
                        if (tuple.Item2 == "host-port")
                        {
                            var w = Stopwatch.StartNew();
                            while ((await Status(ai)).Network != "HOST")
                            {
                                if (w.Elapsed.TotalSeconds > 15)
                                    throw new TimeoutException("actual host readiness");
                                await Task.Delay(50);
                            }
                        }
                    }

                    var watch = Stopwatch.StartNew();
                    while ((await Status(bi)).Network != "CLIENT")
                    {
                        if (watch.Elapsed.TotalSeconds > 20)
                            throw new TimeoutException("actual TLS client readiness");
                        await Task.Delay(50);
                    }

                    var refused = await Send(bi, "Reset");
                    check(!refused.Success && refused.Code == "NETWORK_CLIENT_LOCKED", "remote hardware mutation accepted");
                    var current = (await Send(bi, "Settings")).Value<SettingsInfo>();
                    var candidate = current.Settings.Deserialize<EmulatorSettings>()!;
                    candidate.Port = port == 65535 ? port - 1 : port + 1;
                    check((await Send(bi, "ApplySettings", new ApplySettings(current.Revision, JsonSerializer.SerializeToElement(candidate)))).Code == "NETWORK_CLIENT_LOCKED", "network mutation accepted");
                });
                await test("M9e joined native close with pending IPC and manager state-only startup", async () =>
                {
                    var pending = Enumerable.Range(0, 8).Select(_ => Send(ai, "StatePreview", new PathCommand(Path.Combine(root, "saves", new Cartridge(DiagnosticRom.Create()).Hash, "manager-view.snessav")))).ToArray();
                    check((await Send(ai, "Close")).Success, "Close request Ack");
                    check((await Send(bi, "Close")).Success, "Close client Ack");
                    await a!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                    await b!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                    foreach (var request in pending)
                    {
                        try
                        {
                            await request;
                        }
                        catch (Exception e) when (e is IOException or OperationCanceledException)
                        {
                        }
                    }

                    check(a.ExitCode == 0 && b.ExitCode == 0, "close pending failed");
                    string state = Path.Combine(root, "saves", new Cartridge(DiagnosticRom.Create()).Hash, "manager-view.snessav"), output = Path.Combine(root, "manager-state-start");
                    var process = Start(manager, "manager-state-start", "--state", state, "--manager-smoke", output);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    check(process.ExitCode == 0 && File.Exists(Path.Combine(output, "result.json")), "State-only manager startup failed");
                    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "result.json")));
                    int pid = doc.RootElement.GetProperty("SelectedPid").GetInt32();
                    var instance = Discovery.List().Single(x => x.ProcessId == pid);
                    var live = await Status(instance);
                    check(live.CartridgeHash == new Cartridge(DiagnosticRom.Create()).Hash, "State-only startup did not load matching canonical ROM");
                    using var owned = Process.GetProcessById(pid);
                    check((await Send(instance, "Close")).Success, "close owned launched emulator");
                    await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                    check(owned.ExitCode == 0, "launched emulator close");
                });
            }
        }
        finally
        {
            var cleanupErrors = new List<Exception>();
            foreach (string dir in new[] { aDir, bDir, Path.Combine(root, "manager-single") })
            {
                try { Directory.CreateDirectory(dir); string tmp = Path.Combine(dir, "cleanup-close.tmp"); File.WriteAllText(tmp, "close"); File.Move(tmp, Path.Combine(dir, "close"), true); } catch (Exception error) { cleanupErrors.Add(error); }
            }
            // Include only the emulator launched through this isolated test's Manager.
            foreach (var info in Discovery.List().Where(i => string.Equals(i.Executable, exe, StringComparison.OrdinalIgnoreCase) && !children.Any(p => p.Id == i.ProcessId)))
            {
                try { var launched = Process.GetProcessById(info.ProcessId); if (launched.StartTime.ToUniversalTime().Ticks != info.StartTicks) { launched.Dispose(); continue; } children.Add(launched); await Send(info, "Close"); } catch (Exception error) { cleanupErrors.Add(error); }
            }
            foreach (var process in children)
            {
                try { if (!process.HasExited) { try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25)); } catch (TimeoutException) { cleanupErrors.Add(new TimeoutException("Owned process did not join:" + process.Id)); process.Kill(true); await process.WaitForExitAsync(); } } } catch (Exception error) { cleanupErrors.Add(error); } finally { }
            }
            try { await Task.WhenAll(logs); } catch (Exception error) { cleanupErrors.Add(error); }
            foreach (var process in children) process.Dispose();
            try { var after = Identities(); File.WriteAllText(Path.Combine(root, "native-after.json"), JsonSerializer.Serialize(after)); if (before.Count != after.Count || before.Any(p => after.GetValueOrDefault(p.Key) != p.Value)) cleanupErrors.Add(new Exception("Native binary/configuration identities changed")); } catch (Exception error) { cleanupErrors.Add(error); }
            if (cleanupErrors.Count > 0) await test("M9e owned cleanup and identities", () => Task.FromException(new AggregateException(cleanupErrors)));
        }
    }
}
