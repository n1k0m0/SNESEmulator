using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Runtime;
using SNESEmulator.SaveStates;
static class InteractionTests
{
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    public static void Run(Action<string,Action> test,string root)
    {
        test("M9g binding grammar preserves positive axes alternatives and explicit chords",()=>{
            foreach(string expression in new[]{"Axis1+;Hat0Down","Axis1+&Button2","Axis1-&Hat0Left;Button255","Button0&Button1&Button2&Button3",""})Check(InputBindings.Valid(expression,false),"valid "+expression);
            foreach(string expression in new[]{"Axis256+","Button256","Button1&&Button2",";Button1","Button1;","Button0&Button1&Button2&Button3&Button4","@Start"})Check(!InputBindings.Valid(expression,false),"reject "+expression);
            Check(InputBindings.Matches("Axis1+&Button2;Hat0Down",new HashSet<string>{"Axis1+","Button2"}),"positive axis chord");Check(!InputBindings.Matches("Axis1+&Button2",new HashSet<string>{"Axis1+"}),"partial chord");Check(InputBindings.Matches("Axis1+&Button2;Hat0Down",new HashSet<string>{"Hat0Down"}),"OR preserved");
            Check(InputBindings.Valid("ControlLeft&F10;F7",true)&&!InputBindings.Valid("NoSuchKey",true),"keyboard vocabulary");
        });
        test("M9g migration preserves all16 main and64 savestate legacy combinations",()=>{
            var b=InputBindings.SNESEmulatorPadDefaults();b["Start"]="Button10;Button11;Button12;Button13";b["Select"]="Button20;Button21;Button22;Button23";b["L"]="Button30;Button31;Button32;Button33";b["R"]="Button40;Button41;Button42;Button43";
            foreach(string start in b["Start"].Split(';'))foreach(string select in b["Select"].Split(';')){var session=new FrontendInputSession();session.Update(true,"pad",new HashSet<string>(),b,InputBindings.PadActions(b),false,false);Check(session.Update(true,"pad",new HashSet<string>{start,select},b,InputBindings.PadActions(b),false,false).Actions.Contains("MainMenu"),"all main alternatives");}
            foreach(string l in b["L"].Split(';'))foreach(string r in b["R"].Split(';'))foreach(string select in b["Select"].Split(';')){var session=new FrontendInputSession();session.Update(true,"pad",new HashSet<string>(),b,InputBindings.PadActions(b),false,false);Check(session.Update(true,"pad",new HashSet<string>{l,r,select},b,InputBindings.PadActions(b),false,false).Actions.Contains("SaveStates"),"all save alternatives");}
            var settings=new EmulatorSettings{GamepadBindings=new(){["B"]=17}};InputBindings.Ensure(settings);Check(settings.GamepadControls!["B"]=="Button17"&&settings.GamepadActions!["MainMenu"]=="@Start&@Select","legacy integer migration");
        });
        test("M9g frontend edges do not swallow separate SNES Start Select or repeat toggles",()=>{
            var b=InputBindings.SNESEmulatorPadDefaults();var a=InputBindings.PadActions();var s=new FrontendInputSession();FrontendInputDecision Sample(params string[] raw)=>s.Update(true,"p",raw.ToHashSet(),b,a,false,false);
            Sample();Check(Sample("Button7").GameButtons==(ushort)SnesButtons.Start,"Start alone");Sample();Check(Sample("Button6").GameButtons==(ushort)SnesButtons.Select,"Select alone");var edge=Sample("Button6","Button7");Check(edge.GameButtons==0&&edge.Actions.Contains("MainMenu"),"complete chord reserved");Check(Sample("Button6","Button7").Actions.Count==0,"one edge");Check(Sample("Button0").GameButtons==0,"menu transition neutral latch");Sample();Check(Sample("Button0").GameButtons==(ushort)SnesButtons.B,"released gameplay resumes");
        });
        test("M9g focus hotplug mapping and capture leases require neutral input",()=>{
            var b=InputBindings.SNESEmulatorPadDefaults();var a=InputBindings.PadActions();var s=new FrontendInputSession();var held=new HashSet<string>{"Button6","Button7"};
            Check(s.Update(false,"p",held,b,a,false,false).GameButtons==0,"unfocused");Check(s.Update(true,"p",held,b,a,false,false).Actions.Count==0,"focus held");s.Update(true,"p",new HashSet<string>(),b,a,false,false);Check(s.Update(true,"p",held,b,a,false,false).Actions.Contains("MainMenu"),"focus release");Check(s.Update(true,"q",held,b,a,false,false).Actions.Count==0,"hotplug held");s.Update(true,"q",new HashSet<string>(),b,a,false,false);Check(s.Update(true,"q",held,b,a,false,true).Actions.Count==0,"capture consumes");s.Reset();Check(s.Update(true,"q",held,b,a,false,false).Actions.Count==0,"configuration lease");Check(!s.Update(true,null,held,b,a,false,false).Active,"disconnect");
        });
        test("M9g turbo fullscreen reserve only their keys and preserve held game directions",()=>{
            var b=InputBindings.KeyboardDefaults();var a=b.Where(x=>InputBindings.Actions.Contains(x.Key)).ToDictionary();var s=new FrontendInputSession();FrontendInputDecision Sample(bool menu,params string[] raw)=>s.Update(true,"keyboard",raw.ToHashSet(),b,a,menu,false);
            Sample(false);Check(Sample(false,"Right").GameButtons==(ushort)SnesButtons.Right,"moving");var toggle=Sample(false,"Right","F9");Check(toggle.Actions.Contains("Turbo")&&toggle.GameButtons==(ushort)SnesButtons.Right,"turbo keeps unrelated direction");Check(Sample(false,"Right","F9").Actions.Count==0,"no repeated toggle");Check(Sample(false,"Right","F11").GameButtons==(ushort)SnesButtons.Right,"fullscreen keeps direction");
            b["Turbo"]="Right";a["Turbo"]="Right";s.Reset();Sample(false);Check(Sample(false,"Right").GameButtons==0,"conflicting action consumes gameplay");
        });
        test("M9g held gameplay action cannot auto-select newly opened menu",()=>{
            var b=InputBindings.KeyboardDefaults();var a=b.Where(x=>InputBindings.Actions.Contains(x.Key)).ToDictionary();var s=new FrontendInputSession();s.Update(true,"k",new HashSet<string>(),b,a,false,false);Check(s.Update(true,"k",new HashSet<string>{"Enter"},b,a,false,false).GameButtons==(ushort)SnesButtons.Start,"Enter is gameplay Start");Check(!s.Update(true,"k",new HashSet<string>{"Enter"},b,a,true,false).Actions.Contains("MenuSelect"),"held Start not select");s.Update(true,"k",new HashSet<string>(),b,a,true,false);Check(s.Update(true,"k",new HashSet<string>{"Enter"},b,a,true,false).Actions.Contains("MenuSelect"),"fresh menu select");
        });
        test("M9g settings preserve unknown JSON and roundtrip presentation input reset options",()=>{
            var settings=JsonSerializer.Deserialize<EmulatorSettings>("{\"FutureSetting\":{\"values\":[1,true,\"keep\"]},\"GamepadBindings\":{\"B\":18}}")!;InputBindings.Ensure(settings);settings.RenderFrameLimit=120;settings.PresentationVSync=true;settings.VideoZoom=true;settings.TurboMode=true;settings.ResetMode="Power";settings.KeyboardControls!["Turbo"]="ControlLeft&F7";string path=Path.Combine(root,"interaction-settings.json");settings.Save(path);var loaded=EmulatorSettings.Load(path);Check(loaded.AdditionalSettings!["FutureSetting"].GetProperty("values")[2].GetString()=="keep","unknown setting retained");Check(loaded.GamepadControls!["B"]=="Button18"&&loaded.KeyboardControls!["Turbo"]=="ControlLeft&F7"&&loaded.RenderFrameLimit==120&&loaded.PresentationVSync&&loaded.VideoZoom&&loaded.TurboMode&&loaded.ResetMode=="Power","roundtrip");
        });
        test("M9g owner reset and power preserve dirty SRAM and invalidate output epochs",()=>{
            byte[] rom=DiagnosticRom.Create();rom[0x7fd8]=1;var cart=new Cartridge(rom);var initial=new SnesSystem();initial.Insert(cart);cart.Sram[3]=0x5a;initial.Bus.Wram[3]=0x7a;string path=Path.Combine(root,"reset-seed.snessav");StateSerializer.Save(initial,path);using var worker=new EmulationWorker();worker.InsertAsync(rom).GetAwaiter().GetResult();
            foreach(bool power in new[]{false,true}){worker.LoadAsync(File.ReadAllBytes(path)).GetAwaiter().GetResult();var epoch=worker.Status.Epoch;var input=worker.Status.InputEpoch;if(power)worker.PowerCycleAsync().GetAwaiter().GetResult();else worker.ResetAsync().GetAwaiter().GetResult();Check(worker.Status.Epoch>epoch&&worker.Status.InputEpoch>input&&worker.Latest==null,"epoch/output invalidated");var battery=worker.BatteryAsync().GetAwaiter().GetResult();Check(battery!.Bytes[3]==0x5a,"dirty SRAM retained");byte[] raw=worker.CaptureAsync().GetAwaiter().GetResult();string captured=Path.Combine(root,"reset-after.snessav");StateSerializer.SaveCaptured(raw,captured,new Cartridge(rom));var restored=StateSerializer.Load(new Cartridge(rom),captured);Check(restored.Bus.Wram[3]==(power?0:0x7a),"existing warm/power WRAM distinction");}
        });
    }
}
