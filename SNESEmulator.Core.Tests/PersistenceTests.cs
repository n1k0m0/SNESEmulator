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
// Checks atomic generations, backup recovery, bounded state readers and failed-save handling.
// Assertions establish their stated test scope, not general physical SNES certification.
//

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SNESEmulator.Core;
using SNESEmulator.Runtime;
using SNESEmulator.SaveStates;

static class PersistenceTests
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Await(Task task)=>task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
    static T Await<T>(Task<T> task)=>task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
    static void Reject(Action action,string why){try{action();}catch(Exception e) when(e is IOException or InvalidDataException or OperationCanceledException){return;}throw new Exception("Accepted "+why);}
    static void InvalidBecause(Action action,string reason)
    {try{action();}catch(InvalidDataException e){Check(e.Message.Contains(reason,StringComparison.OrdinalIgnoreCase),"wrong rejection reason: "+e.Message);return;}throw new Exception("Expected bounded rejection: "+reason);}
    static byte[] Zip(byte[] raw){using var memory=new MemoryStream();using(var zip=new GZipStream(memory,CompressionLevel.Fastest,true))zip.Write(raw);return memory.ToArray();}
    static byte[] Payload(byte value){byte[] raw=Enumerable.Repeat(value,128).ToArray();return [..SHA256.HashData(raw),..raw];}
    static bool Valid(string path){var data=AtomicFile.ReadBytes(path,160);return data.Length==160&&SHA256.HashData(data.AsSpan(32)).AsSpan().SequenceEqual(data.AsSpan(0,32));}
    static void Write(string path,byte value)=>AtomicFile.Write(path,file=>file.Write(Payload(value)),Valid);
    internal static byte[] BatteryRom()
    {
        var rom=DiagnosticRom.Create();rom[0x7fd8]=1;
        // Genuine CPU loop increments cartridge SRAM continuously; no test-side memory writes.
        byte[] code=[0x78,0xaf,0,0,0x70,0x1a,0x8f,0,0,0x70,0x80,0xf5];code.CopyTo(rom,0x2000);rom[0x7ffc]=0;rom[0x7ffd]=0xa0;return rom;
    }
    public static void Run(Action<string,Action> test,string root)
    {
        test("M9c atomic fault and cancellation at every transaction boundary",()=>{
            foreach(string point in new[]{"before-write","after-write","before-flush","after-flush","before-backup","after-backup","before-replace","after-replace"})foreach(bool cancel in new[]{false,true})
            {
                string path=Path.Combine(root,"fault-"+point+cancel,"data");Write(path,1);Write(path,2);using var cts=new CancellationTokenSource();
                try{AtomicFile.Write(path,file=>file.Write(Payload(3)),Valid,cts.Token,at=>{if(at==point){if(cancel)cts.Cancel();else throw new IOException("injected "+at);}});Check(cancel&&point=="after-replace","unexpected completed transaction");}
                catch(Exception e) when(e is IOException or InvalidDataException or OperationCanceledException){}
                Check(Valid(path)&&Valid(path+".bak"),point+" complete generations");
                Check(File.ReadAllBytes(path).SequenceEqual(Payload(point=="after-replace"?(byte)3:(byte)2)),point+" primary commit boundary");
                Check(File.ReadAllBytes(path+".bak").SequenceEqual(Payload(point is "after-backup" or "before-replace" or "after-replace"?(byte)2:(byte)1)),point+" valid backup");
                Check(!Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp").Any(),point+" cleanup");
            }
        });
        test("M9c parallel same-path writes and normalized paths keep two valid generations",()=>{
            string path=Path.Combine(root,"parallel","save");Write(path,0);
            Await(Task.WhenAll(Enumerable.Range(1,40).Select(i=>Task.Run(()=>Write(i%2==0?path:Path.Combine(root,"parallel","..","parallel","save"),(byte)i)))));
            Check(Valid(path)&&Valid(path+".bak"),"parallel primary and backup checksums");Check(!File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(path+".bak")),"distinct committed generations");
        });
        test("M9c cross-process transaction lock reports conflict then permits retry",()=>{
            string path=Path.Combine(root,"process-lock","save");
            var info=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardInput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
            info.ArgumentList.Add(typeof(PersistenceTests).Assembly.Location);info.ArgumentList.Add("--persistence-lock");info.ArgumentList.Add(path);
            using var child=Process.Start(info)!;
            try{Check(Await(child.StandardOutput.ReadLineAsync())=="LOCKED","child readiness");Reject(()=>Write(path,1),"cross-process write");}
            finally{child.StandardInput.WriteLine("release");Await(child.WaitForExitAsync());}
            Check(child.ExitCode==0,"owned child exit");Write(path,2);Check(Valid(path),"retry after lock release");
        });
        test("M9c abruptly terminated owned writer cannot replace valid generations",()=>{
            string path=Path.Combine(root,"aborted-process","save");Write(path,1);Write(path,2);
            var info=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};info.ArgumentList.Add(typeof(PersistenceTests).Assembly.Location);info.ArgumentList.Add("--persistence-abort");info.ArgumentList.Add(path);
            using var child=Process.Start(info)!;try{Check(Await(child.StandardOutput.ReadLineAsync())=="PARTIAL","owned writer reached partial temp");}finally{if(!child.HasExited)child.Kill();Await(child.WaitForExitAsync());}
            Check(Valid(path)&&File.ReadAllBytes(path).SequenceEqual(Payload(2))&&Valid(path+".bak")&&File.ReadAllBytes(path+".bak").SequenceEqual(Payload(1)),"actual process abort retains both generations");
            Check(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp").Length==1,"abandoned temp retained as noncandidate evidence");
            byte[]? bytes=null;bool recovered=false;
            Check(SpinWait.SpinUntil(()=>{try{bytes=AtomicFile.Read(path,160,data=>data.SequenceEqual(Payload(2)),out recovered);return true;}catch(IOException){return false;}},TimeSpan.FromSeconds(3)),"terminated writer lock released by OS");
            Check(!recovered&&bytes!.SequenceEqual(Payload(2)),"partial temp never recovered");
        });
        test("M9c SRAM envelope identity checksum recovery and unchanged legacy migration",()=>{
            string PathFor(string hash)=>Path.Combine(root,"sram",hash+".srm");string hash=new Cartridge(BatteryRom()).Hash;byte[] legacy=Enumerable.Repeat((byte)7,2048).ToArray();Directory.CreateDirectory(Path.GetDirectoryName(PathFor(hash))!);File.WriteAllBytes(PathFor(hash),legacy);
            var notices=new List<string>();using(var store=new BatteryPersistence(()=>Task.FromResult<BatterySnapshot?>(null),PathFor,notices.Add))Check(Await(store.FlushAndReadAsync(hash,2048)).SequenceEqual(legacy),"legacy imported");
            Check(File.ReadAllBytes(PathFor(hash)).SequenceEqual(legacy)&&notices.Single().Contains("NO HISTORICAL CHECKSUM"),"legacy preserved with explicit limitation");
            byte[] current=Enumerable.Repeat((byte)8,2048).ToArray();using(var store=new BatteryPersistence(()=>Task.FromResult<BatterySnapshot?>(new(hash,current)),PathFor)){Await(store.FlushAsync());Await(store.FlushAsync());Check(store.Writes==1,"dirty only");}
            string path=BatteryPersistence.ProtectedPath(PathFor(hash));byte[] corrupt=File.ReadAllBytes(path);corrupt[^1]^=1;File.WriteAllBytes(path,corrupt);
            BatterySnapshot? recoveredSnapshot=null;
            using(var recovery=new BatteryPersistence(()=>Task.FromResult(recoveredSnapshot),PathFor,notices.Add))
            {
                byte[] recovered=Await(recovery.FlushAndReadAsync(hash,2048));Check(recovered.SequenceEqual(legacy),"validated previous generation");
                Check(File.ReadAllBytes(path).SequenceEqual(corrupt)&&notices.Last().Contains("RECOVERED"),"corrupt evidence retained on read");
                recoveredSnapshot=new(hash,recovered);Await(recovery.FlushAsync());Check(recovery.Writes==1&&BatteryPersistence.Decode(File.ReadAllBytes(path),hash,2048).SequenceEqual(legacy),"unchanged recovered SRAM repairs canonical primary at next flush");
                Await(recovery.FlushAsync());Check(recovery.Writes==1,"repaired clean SRAM coalesces again");
            }
            Reject(()=>BatteryPersistence.Decode(File.ReadAllBytes(path+".bak"),"WRONG",2048),"wrong ROM");Reject(()=>BatteryPersistence.Decode(File.ReadAllBytes(path+".bak"),hash,4096),"wrong size");
            using(var repair=new BatteryPersistence(()=>Task.FromResult<BatterySnapshot?>(new(hash,current)),PathFor))Await(repair.FlushAsync());
            Check(Directory.GetFiles(Path.GetDirectoryName(path)!,Path.GetFileName(path)+".corrupt-*").Any(p=>File.ReadAllBytes(p).SequenceEqual(corrupt)),"repair archives corrupt evidence");
        });
        test("M9c SRAM session lease rejects competing live instance",()=>{
            string PathFor(string hash)=>Path.Combine(root,"lease",hash+".srm");var snapshot=new BatterySnapshot("same",[1,2,3]);
            using var first=new BatteryPersistence(()=>Task.FromResult<BatterySnapshot?>(snapshot),PathFor);using var second=new BatteryPersistence(()=>Task.FromResult<BatterySnapshot?>(snapshot),PathFor);
            Await(first.FlushAsync());Reject(()=>Await(second.FlushAsync()),"competing SRAM session");first.Dispose();Await(second.FlushAsync());Check(second.Writes==1,"lease retry");
        });
        test("M9c failed SRAM imports and install keep old live machine and session lease",()=>{
            string PathFor(string hash)=>Path.Combine(root,"switch-rollback",hash+".srm");var rom=BatteryRom();var cart=new Cartridge(rom);using var worker=new EmulationWorker();Await(worker.InsertAsync(rom));Await(worker.StepAsync(default));
            using var store=new BatteryPersistence(()=>worker.BatteryAsync(),PathFor);Await(store.FlushAsync());byte[] stable=Await(worker.CaptureAsync());
            void OldLeaseHeld(){using var contender=new BatteryPersistence(()=>Task.FromResult<BatterySnapshot?>(new(cart.Hash,new byte[2048])),PathFor);Reject(()=>Await(contender.FlushAsync()),"old ROM lease lost on failed switch");Check(Await(worker.CaptureAsync()).SequenceEqual(stable),"failed switch mutated old hardware");}
            string target="other";string targetPath=BatteryPersistence.ProtectedPath(PathFor(target));File.WriteAllBytes(targetPath,[1,2,3]);
            Reject(()=>Await(store.SwitchAsync(target,2048,_=>worker.ResetAsync())),"corrupt target import");OldLeaseHeld();
            File.Delete(targetPath);using(var occupied=new BatteryPersistence(()=>Task.FromResult<BatterySnapshot?>(new(target,new byte[2048])),PathFor)){Await(occupied.FlushAsync());Reject(()=>Await(store.SwitchAsync(target,2048,_=>worker.ResetAsync())),"occupied target import");OldLeaseHeld();}
            Reject(()=>Await(store.SwitchAsync(target,2048,_=>Task.FromException(new IOException("injected failed owner install")))),"failed install");OldLeaseHeld();
            // Same-ROM reload first captures current SRAM, then installs those exact bytes.
            var before=Await(worker.BatteryAsync())!;Await(store.SwitchAsync(cart.Hash,2048,bytes=>worker.InsertAsync(rom,bytes)));Check(Await(worker.BatteryAsync())!.Bytes.SequenceEqual(before.Bytes),"same-ROM PowerOn retains newest SRAM");
            Await(worker.ResetAsync());Check(Await(worker.BatteryAsync())!.Bytes.SequenceEqual(before.Bytes),"Reset preserves SRAM");
        });
        test("M9c autosave coalesces and ordered capture prevents stale final write",()=>{
            string PathFor(string hash)=>Path.Combine(root,"ordered",hash+".srm");int captures=0;byte current=1;var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<BatterySnapshot?> Capture(){byte captured=current;if(Interlocked.Increment(ref captures)==1){entered.SetResult();await release.Task;}return new("same",[captured]);}
            using var store=new BatteryPersistence(Capture,PathFor);var errors=new List<string>();var autosave=new BatteryAutosave(store,errors.Add,1);
            autosave.Tick(1,true);Await(entered.Task);for(int i=0;i<100;i++)autosave.Tick(1,true);Check(autosave.Attempts==1&&captures==1,"one pending snapshot");
            current=2;var final=Task.Run(store.FlushAsync);var reload=Task.Run(()=>store.FlushAndReadAsync("same",1));Check(!final.IsCompleted&&!reload.IsCompleted,"later operations queue before capture");release.SetResult();Await(Task.WhenAll(autosave.Pending,final,reload));
            Check(BatteryPersistence.Decode(File.ReadAllBytes(BatteryPersistence.ProtectedPath(PathFor("same"))),"same",1).SequenceEqual(new byte[]{2}),"new final state survives old delayed autosave");
            long writes=store.Writes;autosave.Tick(1,true);Await(autosave.Pending);Check(store.Writes==writes&&errors.Count==0,"clean capture does not write");
        });
        test("M9c running CPU periodic battery capture writes dirty SRAM without pausing",()=>{
            string PathFor(string hash)=>Path.Combine(root,"running",hash+".srm");
            using var worker=new EmulationWorker();
            Await(worker.InsertAsync(BatteryRom()));
            var initial=Await(worker.BatteryAsync())!;
            var captures=new List<(long ObservedFieldAfterCapture,string Digest,byte[] Bytes)>();
            async Task<BatterySnapshot?> CaptureObserved()
            {
                var snapshot=await worker.BatteryAsync().ConfigureAwait(false);
                // Status is observed after the owner snapshot completes; the owner
                // can already have advanced. This is not an atomic SRAM timestamp.
                if(snapshot!=null)
                    captures.Add((worker.Status.Field,Convert.ToHexString(SHA256.HashData(snapshot.Bytes)),snapshot.Bytes));
                return snapshot;
            }
            using var store=new BatteryPersistence(CaptureObserved,PathFor);
            var errors=new List<string>();
            var autosave=new BatteryAutosave(store,errors.Add,.05);
            Await(worker.SetModeAsync(false,false));
            var watch=Stopwatch.StartNew();
            double previous=0;
            long validatedWrites=0;
            var persistedDigests=new HashSet<string>();
            string path=BatteryPersistence.ProtectedPath(PathFor(initial.Hash));
            // This is a persistence/liveness proof, not a one-second throughput
            // benchmark. Cold JIT or a slower CI runner must still complete the
            // same hardware and disk transitions; the watchdog remains bounded.
            try
            {
                while(worker.Status.Field<=10||persistedDigests.Count<2)
                {
                    Check(watch.Elapsed<TimeSpan.FromSeconds(15),"battery progress watchdog expired");
                    Check(worker.IsAlive&&!worker.Status.Paused&&worker.Status.Fault==null,"owner stopped during periodic capture");
                    double now=watch.Elapsed.TotalSeconds;
                    autosave.Tick(now-previous,true);
                    previous=now;
                    autosave.Pending.WaitAsync(TimeSpan.FromSeconds(15)-watch.Elapsed).GetAwaiter().GetResult();
                    Check(errors.Count==0,"periodic SRAM failure: "+string.Join("; ",errors));
                    if(store.Writes>validatedWrites)
                    {
                        // Validate while the CPU is still running. The file must
                        // contain the immutable owner snapshot, not merely exist.
                        byte[] disk=BatteryPersistence.Decode(File.ReadAllBytes(path),initial.Hash,initial.Bytes.Length);
                        Check(captures.Count>0&&disk.SequenceEqual(captures[^1].Bytes),"periodic disk differs from captured SRAM");
                        Check(!worker.Status.Paused&&worker.Status.Fault==null,"periodic write paused owner");
                        persistedDigests.Add(Convert.ToHexString(SHA256.HashData(disk)));
                        validatedWrites=store.Writes;
                    }
                    while(worker.TryDequeue(out _)){} // A real consumer; output backpressure is irrelevant here.
                    Thread.Sleep(10);
                }
                Check(store.Writes>1&&autosave.Attempts>1,"two periodic writes were not completed");
                Check(captures.Any(c=>!c.Bytes.SequenceEqual(initial.Bytes)),"diagnostic CPU never dirtied initial SRAM");
                // The previous complete generation must also remain readable.
                _ = BatteryPersistence.Decode(File.ReadAllBytes(path+".bak"),initial.Hash,initial.Bytes.Length);
            }
            finally
            {
                Console.WriteLine("RUNNING_BATTERY_OBSERVATION "+System.Text.Json.JsonSerializer.Serialize(new{
                    Seconds=watch.Elapsed.TotalSeconds,Status=worker.Status,Writes=store.Writes,Attempts=autosave.Attempts,
                    DistinctValidatedSnapshots=persistedDigests.Count,Errors=errors,
                    Captures=captures.Take(64).Select(c=>new{c.ObservedFieldAfterCapture,c.Digest}),Limits="Bounded functional persistence; not realtime performance"}));
            }
            Await(worker.SetModeAsync(true,false));
            var final=Await(worker.BatteryAsync())!;
            Await(store.FlushAsync());
            Check(BatteryPersistence.Decode(File.ReadAllBytes(path),final.Hash,final.Bytes.Length).SequenceEqual(final.Bytes),"final disk exactly matches owned SRAM");
        });
        test("M9c periodic write conflict reports failure and retains the last committed SRAM",()=>{
            string PathFor(string hash)=>Path.Combine(root,"periodic-conflict",hash+".srm");
            using var worker=new EmulationWorker();
            Await(worker.InsertAsync(BatteryRom()));
            using var store=new BatteryPersistence(()=>worker.BatteryAsync(),PathFor);
            Await(store.FlushAsync());
            var initial=Await(worker.BatteryAsync())!;
            string path=BatteryPersistence.ProtectedPath(PathFor(initial.Hash));
            byte[] committed=File.ReadAllBytes(path);
            var errors=new List<string>();
            var autosave=new BatteryAutosave(store,errors.Add,.05);
            Await(worker.SetModeAsync(false,false));
            // An owned file lock injects a real transaction conflict on both OSes;
            // no permissions assumption or test-side SRAM write is involved.
            using(var blocked=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                Check(SpinWait.SpinUntil(()=>worker.Status.Field>2||worker.Status.Fault!=null,TimeSpan.FromSeconds(15)),"owner made no hardware progress");
                autosave.Tick(.04,true);
                Check(autosave.Attempts==0,"autosave ran before interval");
                autosave.Tick(.01,true);
                Await(autosave.Pending);
                Check(autosave.Attempts==1&&errors.Count==1&&errors[0].StartsWith("SRAM SAVE FAILED:"),"periodic failure was not visible");
                Check(store.Writes==1&&File.ReadAllBytes(path).SequenceEqual(committed),"failed periodic write changed committed generation");
                Check(!worker.Status.Paused&&worker.Status.Fault==null,"storage error stopped hardware owner");
            }
            Await(worker.SetModeAsync(true,false));
            var final=Await(worker.BatteryAsync())!;
            Check(!final.Bytes.SequenceEqual(initial.Bytes),"CPU never dirtied SRAM during conflict");
            Await(store.FlushAsync());
            Check(store.Writes==2&&BatteryPersistence.Decode(File.ReadAllBytes(path),final.Hash,final.Bytes.Length).SequenceEqual(final.Bytes),"retry did not commit exact final SRAM");
        });
        test("M9c captured states retain independent validated backup and explicit recovery",()=>{
            var machine=new SnesSystem();machine.Insert(new Cartridge(BatteryRom()));machine.RunUntil(12345);var cart=machine.Bus.Cartridge!;byte[] first=StateSerializer.Capture(machine);string path=Path.Combine(root,"state","save.snessav");StateSerializer.SaveCaptured(first,path,cart);
            machine.RunUntil(30000);byte[] second=StateSerializer.Capture(machine);StateSerializer.SaveCaptured(second,path,cart);
            Check(StateSerializer.Capture(StateSerializer.Load(cart,path+".bak")).SequenceEqual(first),"independent prior state");Check(StateSerializer.Capture(StateSerializer.Load(cart,path)).SequenceEqual(second),"new state");
            File.WriteAllBytes(path,[1,2,3]);Reject(()=>StateSerializer.Load(cart,path),"strict corrupt primary");var recovered=StateSerializer.ReadForLoad(cart,path,out bool notice);using var stream=new MemoryStream(recovered);Check(notice&&StateSerializer.Capture(StateSerializer.Load(cart,stream)).SequenceEqual(first),"explicit validated recovery");
            File.Delete(path);_ = StateSerializer.ReadForLoad(cart,path,out notice);Check(notice,"missing primary recovery");File.WriteAllText(path+".corrupt-evidence","retain");AtomicFile.DeleteLogical(path);Check(!File.Exists(path+".bak")&&File.Exists(path+".corrupt-evidence"),"logical deletion removes backup only");Reject(()=>StateSerializer.ReadForLoad(cart,path,out _),"deleted save cannot recover");
        });
        test("M9c corrupt truncated oversized and gzip bomb states leave live machine intact",()=>{
            using var worker=new EmulationWorker();var rom=BatteryRom();Await(worker.InsertAsync(rom));Await(worker.StepAsync(new(0x8000,0)));byte[] stable=Await(worker.CaptureAsync());var good=Zip(stable);
            var inputs=new List<byte[]>{new byte[]{1,2,3},good[..^1],good[..^8],good[..(good.Length/2)],new byte[StateSerializer.MaximumCompressedBytes+1],Zip(new byte[StateSerializer.MaximumRawBytes+1])};
            using(var memory=new MemoryStream()){using(var writer=new BinaryWriter(memory,Encoding.UTF8,true))writer.Write7BitEncodedInt(StateSerializer.MaximumStringBytes+1);inputs.Add(Zip(memory.ToArray()));}
            // Profile is the third length-prefixed string; replace it without changing the rest.
            byte[] Profile(string profile){using var input=new MemoryStream(stable);using var reader=new BinaryReader(input);reader.ReadString();reader.ReadInt32();reader.ReadString();reader.ReadInt32();int start=(int)input.Position;reader.ReadString();int end=(int)input.Position;using var output=new MemoryStream();output.Write(stable.AsSpan(0,start));using(var writer=new BinaryWriter(output,Encoding.UTF8,true))writer.Write(profile);output.Write(stable.AsSpan(end));return Zip(output.ToArray());}
            using var profileReader=new BinaryReader(new MemoryStream(stable,false));profileReader.ReadString();profileReader.ReadInt32();profileReader.ReadString();profileReader.ReadInt32();string originalProfile=profileReader.ReadString();
            byte[] Extra(string field)=>Profile(originalProfile[..^1]+","+field+"}");
            InvalidBecause(()=>Await(worker.LoadAsync(Extra("\"deep\":"+new string('[',40)+"0"+new string(']',40)))),"depth");
            InvalidBecause(()=>Await(worker.LoadAsync(Extra("\"items\":["+string.Join(',',Enumerable.Repeat("0",512*239+1))+"]"))),"array exceeds");
            InvalidBecause(()=>Await(worker.LoadAsync(Extra("\"padding\":\""+new string('a',StateSerializer.MaximumStringBytes)+"\""))),"oversized savestate string");
            InvalidBecause(()=>StateSerializer.Decode(new MemoryStream(inputs[5],false)),"Decompressed savestate exceeds");
            InvalidBecause(()=>StateSerializer.Decode(new MemoryStream(new byte[StateSerializer.MaximumCompressedBytes+1],false)),"Compressed savestate exceeds");
            inputs.Add(Profile("null"));inputs.Add(Profile("{\"Id\":null}"));
            string firmware=new Apu().FirmwareHash;int firmwareOffset=stable.AsSpan().IndexOf(Encoding.ASCII.GetBytes(firmware));Check(firmwareOffset>=0,"serialized firmware identity");byte[] wrongFirmware=stable.ToArray();Encoding.ASCII.GetBytes(new string('0',firmware.Length)).CopyTo(wrongFirmware,firmwareOffset);inputs.Add(Zip(wrongFirmware));
            byte[] wrongVersion=stable.ToArray();wrongVersion[8]=8;inputs.Add(Zip(wrongVersion));
            foreach(byte[] bad in inputs){Reject(()=>Await(worker.LoadAsync(bad)),"malformed state");Check(Await(worker.CaptureAsync()).SequenceEqual(stable),"rejection preserves hardware");}
        });
        test("M9c pending state load is bounded cancelable epoch checked and drained on close",()=>{
            var rom=BatteryRom();var machine=new SnesSystem();machine.Insert(new Cartridge(rom));machine.RunUntil(12345);byte[] compressed=Zip(StateSerializer.Capture(machine));
            using var worker=new EmulationWorker();Await(worker.InsertAsync(rom));using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            worker.BeforeStateInstall=()=>{entered.Set();Check(release.Wait(TimeSpan.FromSeconds(10)),"release held parser");};
            Task load=worker.LoadAsync(compressed);Check(entered.Wait(TimeSpan.FromSeconds(10)),"off-owner load prepared");
            var rejections=Enumerable.Range(0,100).Select(_=>worker.LoadAsync(compressed)).ToArray();Check(rejections.All(t=>t.IsFaulted),"single pending decoder rejected before copying");
            foreach(var rejected in rejections){try{Await(rejected);throw new Exception("accepted concurrent decoder");}catch(InvalidOperationException){}}
            Await(worker.ResetAsync());byte[] stable=Await(worker.CaptureAsync());release.Set();
            try{Await(load);throw new Exception("accepted old epoch state");}catch(InvalidOperationException){}
            Check(Await(worker.CaptureAsync()).SequenceEqual(stable),"reset wins over old prepared state");
            entered.Reset();release.Reset();using var cancellation=new CancellationTokenSource();load=worker.LoadAsync(compressed,cancellation.Token);Check(entered.Wait(TimeSpan.FromSeconds(10)),"second load prepared");cancellation.Cancel();release.Set();Reject(()=>Await(load),"canceled prepared load");
            Check(Await(worker.CaptureAsync()).SequenceEqual(stable),"cancel retains live state");
            entered.Reset();release.Reset();load=worker.LoadAsync(compressed);Check(entered.Wait(TimeSpan.FromSeconds(10)),"third load prepared");var close=Task.Run(worker.Dispose);Thread.Sleep(20);release.Set();Await(close);Check(!worker.IsAlive&&load.IsCompleted,"shutdown drains owned decode");Reject(()=>Await(load),"shutdown canceled load");
        });
        test("M9c settings burst keeps one writer and the latest pending snapshot",()=>{
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var written=new List<string>();var errors=new List<string>();
            var writer=new SettingsPersistence(errors.Add,json=>{if(written.Count==0){entered.Set();Check(release.Wait(TimeSpan.FromSeconds(10)),"held settings write released");}written.Add(json);});
            writer.Queue("first");Check(entered.Wait(TimeSpan.FromSeconds(10)),"settings writer started");for(int i=0;i<1000;i++)writer.Queue("value-"+i);release.Set();Await(writer.Pending);
            Check(written.SequenceEqual(new[]{"first","value-999"})&&errors.Count==0,"bounded latest-wins settings burst");
        });
        test("M9c settings validation migration atomic backup and visible recovery",()=>{
            string path=Path.Combine(root,"settings","settings.json");var settings=new EmulatorSettings{Host=null!,RelayHost=null!,PlayerName=null!,ConnectionId=null!,MediaDirectory=null!,Filter=999,Upscale=-1,Port=-4,RelayPort=999999,ControllerPort=9,GamepadDevice=999,GamepadBindings=null!,GamepadControls=new(){["B"]=null!,["A"]="Axis0+;Hat0Right",["X"]="Button9999",["INVALID"]="Button0"}};
            settings.Save(path);var loaded=EmulatorSettings.Load(path);Check(loaded.Host=="localhost"&&loaded.Filter==2&&loaded.Upscale==0&&loaded.Port==1&&loaded.RelayPort==65535&&loaded.ControllerPort==2&&loaded.GamepadDevice==255,"numbers strings enums normalized");
            Check(loaded.GamepadControls!["A"]=="Axis0+;Hat0Right"&&loaded.GamepadControls["B"]==""&&!loaded.GamepadControls.ContainsKey("X")&&!loaded.GamepadControls.ContainsKey("INVALID"),"mapping tokens validated without losing alternatives");
            var legacy=new EmulatorSettings{GamepadControls=null,GamepadBindings=new(){["B"]=13}};legacy.Save(path);loaded=EmulatorSettings.Load(path);Check(loaded.GamepadControls==null&&loaded.GamepadBindings["B"]==13,"legacy bindings remain migratable");
            File.WriteAllText(path,"{\"Port\":\"bad\"}");loaded=EmulatorSettings.Load(path);Check(loaded.PersistenceNotice?.Contains("RECOVERED")==true&&loaded.Port==1,"bad numeric token recovered explicitly");
            File.Delete(path+".bak");loaded=EmulatorSettings.Load(path);Check(loaded.PersistenceNotice?.Contains("UNAVAILABLE")==true,"invalid without backup visible");
        });
    }
}
