using System.Diagnostics;
using System.Text.Json;
using OpenTK.Input;
using SNESEmulator.Core;
using SNESEmulator.Frontend;
using SNESEmulator.Runtime;
using SNESEmulator.SaveStates;

namespace SNESEmulator;

internal static class PersistenceSmoke
{
    public static void Run(string output,string mode)
    {
        Directory.CreateDirectory(output);var rom=DiagnosticRom.Create();rom[0x7fd8]=1;
        byte[] code=[0x78,0xaf,0,0,0x70,0x1a,0x8f,0,0,0x70,0x80,0xf5];code.CopyTo(rom,0x2000);rom[0x7ffc]=0;rom[0x7ffd]=0xa0;
        string romPath=Path.Combine(output,"battery-loop.sfc");File.WriteAllBytes(romPath,rom);var cart=new Cartridge(rom,romPath);
        using var window=new SnesWindow(romPath);FileStream? blocker=null;var watch=Stopwatch.StartNew();var rows=new List<object>();int stage=0;long firstField=0;string? statePath=null;byte[]? firstBattery=null;long loadEpoch=0;double ready=0;
        window.ProbeInput(true,null,new HashSet<string>(),new HashSet<string>());
        string batteryPath=BatteryPersistence.ProtectedPath(Path.Combine(UserDataPaths.GetBaseDirectory(),"battery",cart.Hash+".srm"));
        void Check(bool ok,string why){if(!ok){File.WriteAllText(Path.Combine(output,"failure.json"),JsonSerializer.Serialize(new{Why=why,Stage=stage,Seconds=watch.Elapsed.TotalSeconds,Menu=window.CurrentMenu,Selection=window.ProbeSelection,Metrics=window.RuntimeMetrics,Rows=rows}));throw new InvalidOperationException("Persistence native probe: "+why);}}
        void Press(Key key)=>window.ProbeKey(key);
        void Row(string name)=>rows.Add(new{Name=name,Seconds=watch.Elapsed.TotalSeconds,Metrics=window.RuntimeMetrics});
        window.RenderFrame+=frameArgs=>{
            if(watch.Elapsed.TotalSeconds>110)throw new TimeoutException("Persistence probe stage "+stage);
            if(window.RuntimeStatus.Fault!=null)throw new InvalidOperationException(window.RuntimeStatus.Fault);
            switch(stage)
            {
                case 0:
                    if(window.MediaOperationPending||window.RuntimeStatus.Field<20)return;
                    ready=watch.Elapsed.TotalSeconds;Row("ready-20-fields");
                    if(mode!="normal")
                    {
                        string blocked=UserDataPaths.GetSettingsPath();
                        if(mode=="battery-failure")
                        {
                            var snapshot=window.CaptureBatteryForProbe().GetAwaiter().GetResult()!;byte[] encoded=BatteryPersistence.Encode(snapshot);
                            AtomicFile.Write(batteryPath,stream=>stream.Write(encoded),path=>{try{_ = BatteryPersistence.Decode(AtomicFile.ReadBytes(path,BatteryPersistence.MaximumBytes+256),cart.Hash,cart.Sram.Length);return true;}catch(InvalidDataException){return false;}});blocked=batteryPath;
                        }
                        if(mode=="log-failure")Directory.CreateDirectory(Path.Combine(UserDataPaths.GetBaseDirectory(),"last-error.log"));
                        File.Copy(blocked,Path.Combine(output,"blocked-before.bin"));
                        File.WriteAllText(Path.Combine(output,"blocked-path.txt"),blocked);
                        blocker=new FileStream(blocked+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);firstField=window.RuntimeStatus.Field;stage=8;return;
                    }
                    Check(!File.Exists(batteryPath),"no startup write before periodic deadline");stage=1;break;
                case 1:
                    if(window.BatteryWrites<1||window.BatteryOperationPending)return;
                    Check(window.BatteryAttempts>=1&&!window.RuntimeStatus.Paused,"periodic frontend save while running");firstField=window.RuntimeStatus.Field;
                    firstBattery=BatteryPersistence.Decode(File.ReadAllBytes(batteryPath),cart.Hash,cart.Sram.Length);Row("first-periodic-save");stage=2;break;
                case 2:
                    if(window.BatteryWrites<2||window.BatteryOperationPending)return;
                    Check(window.BatteryAttempts>=2&&window.RuntimeStatus.Field>firstField&&!window.RuntimeStatus.Paused,"second real frontend interval advances hardware");
                    Check(!BatteryPersistence.Decode(File.ReadAllBytes(batteryPath),cart.Hash,cart.Sram.Length).SequenceEqual(firstBattery!),"CPU changed dirty SRAM between actual intervals");
                    _ = BatteryPersistence.Decode(File.ReadAllBytes(batteryPath+".bak"),cart.Hash,cart.Sram.Length);Row("second-periodic-save");Press(Key.F12);Press(Key.S);stage=3;break;
                case 3:
                    if(window.MediaOperationPending)return;
                    statePath=Directory.GetFiles(UserDataPaths.GetSaveDirectory(),"*.snessav",SearchOption.AllDirectories).Single();
                    var saved=StateSerializer.Load(cart,statePath);StateSerializer.Save(saved,statePath);File.WriteAllBytes(statePath,[1,2,3]);Press(Key.F12);Press(Key.F12);stage=4;break;
                case 4:
                    if(!window.PreviewAvailable)return;
                    Check(window.VisibleStatus.Contains("RECOVERED"),"recovery is visible in real preview");Check(File.ReadAllBytes(statePath!).SequenceEqual(new byte[]{1,2,3}),"preview retains corrupt primary");
                    RuntimeSmoke.Screenshot(window,Path.Combine(output,"recovered-preview.png"));Row("corrupt-primary-valid-backup-preview");
                    File.Delete(statePath!);Press(Key.F12);Press(Key.F12);stage=5;break;
                case 5:
                    if(!window.PreviewAvailable)return;
                    Check(window.SaveEntries==1,"backup-only logical save listed");Row("missing-primary-valid-backup-preview");loadEpoch=window.RuntimeStatus.Epoch;Press(Key.Enter);stage=6;break;
                case 6:
                    if(window.MediaOperationPending||window.CurrentMenu!="None")return;
                    Check(window.RuntimeStatus.Epoch>loadEpoch,"recovered state installed");Row("loaded-backup");Press(Key.F12);Press(Key.Delete);Press(Key.Down);Press(Key.Enter);stage=7;break;
                case 7:
                    if(window.MediaOperationPending)return;
                    Check(!File.Exists(statePath!)&&!File.Exists(statePath+".bak"),"logical delete removes recovery generation");Row("deleted-logical-save");Press(Key.S);Check(window.MediaOperationPending,"close with asynchronous save");stage=9;window.Close();break;
                case 8:
                    if(window.RuntimeStatus.Field<firstField+10)return;
                    Row("close-storage-conflict");File.WriteAllText(Path.Combine(output,"rows-before-failure.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));window.Close();break;
            }
        };
        try{window.Run();Check(mode=="normal"&&stage==9,"expected close policy");}
        finally{blocker?.Dispose();}
        foreach(string path in Directory.GetFiles(UserDataPaths.GetSaveDirectory(),"*.snessav",SearchOption.AllDirectories))_ = StateSerializer.Load(cart,path);
        _ = BatteryPersistence.Decode(File.ReadAllBytes(batteryPath),cart.Hash,cart.Sram.Length);
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Success=true,Mode=mode,ReadySeconds=ready,Rows=rows,Limits="Synthetic app callbacks with synthetic focus and OS-file locks; no physical power-cut guarantee"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
