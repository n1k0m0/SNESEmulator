using SNESEmulator.Core;

namespace SNESEmulator.Runtime;

public readonly record struct PadDecision(ushort GameButtons,ushort Pressed,bool MainMenu,bool Saves,bool Active);

/// <summary>Focus/device/config transitions require release before any new action.</summary>
public sealed class GamepadSession
{
    private string? identity;
    private bool armed,suppressed;
    private ushort previous;
    public void Reset(){identity=null;armed=false;suppressed=true;previous=0;}
    public PadDecision Update(bool focused,string? device,ushort mapped,bool anyRawInput,bool menu,bool capture)
    {
        if(!focused||device==null){Reset();return default;}
        if(identity!=device){identity=device;armed=false;suppressed=true;previous=0;}
        if(!armed){if(!anyRawInput)armed=true;return default;}
        ushort pressed=(ushort)(mapped&~previous);
        const ushort main=(ushort)(SnesButtons.Start|SnesButtons.Select),saves=(ushort)(SnesButtons.L|SnesButtons.R|SnesButtons.Select);
        bool mainEdge=!capture&&(mapped&main)==main&&(previous&main)!=main;
        bool saveEdge=!capture&&(mapped&saves)==saves&&(previous&saves)!=saves;
        previous=mapped;
        if(menu||capture||mainEdge||saveEdge)suppressed=true;
        if(suppressed&&!anyRawInput)suppressed=false;
        return new(suppressed||menu||capture?(ushort)0:mapped,capture?(ushort)0:pressed,mainEdge&&!saveEdge,saveEdge,!capture);
    }
}

public static class GamepadMapping
{
    public static readonly string[] Names=["B","A","Y","X","L","R","Select","Start","Up","Down","Left","Right"];
    public static Dictionary<string,string> Defaults()=>new()
    {
        ["B"]="Button0",["A"]="Button1",["Y"]="Button2",["X"]="Button3",["L"]="Button4",["R"]="Button5",["Select"]="Button6",["Start"]="Button7",
        ["Up"]="Axis1-;Hat0Up",["Down"]="Axis1+;Hat0Down",["Left"]="Axis0-;Hat0Left",["Right"]="Axis0+;Hat0Right"
    };
    public static ushort Read(IReadOnlyDictionary<string,string> bindings,IReadOnlySet<string> active)
    {
        ushort result=0;
        foreach(string name in Names)if(bindings.TryGetValue(name,out string? value)&&value!=null&&InputBindings.Matches(value,active)&&Enum.TryParse<SnesButtons>(name,out var button))result|=(ushort)button;
        return result;
    }
}
