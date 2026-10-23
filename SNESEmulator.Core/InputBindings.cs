using System.Text.RegularExpressions;
namespace SNESEmulator.Core;

/// <summary>Platform-neutral input expressions: semicolon alternatives, ampersand simultaneous chord members.</summary>
public static class InputBindings
{
    public static readonly string[] Buttons=["B","A","Y","X","L","R","Select","Start","Up","Down","Left","Right"];
    public static readonly string[] Actions=["MenuSelect","MenuBack","MainMenu","Turbo","SaveStates","Fullscreen","Diagnostic"];
    public static readonly string[] KeyboardKeys=["ShiftLeft","ShiftRight","ControlLeft","AltLeft","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12","Up","Down","Left","Right","Enter","Escape","Space","Tab","BackSpace","Delete","Home","PageUp","PageDown","KeypadMultiply","A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z","Number0","Number1","Number2","Number3","Number4","Number5","Number6","Number7","Number8","Number9","Minus","Plus","BracketLeft","BracketRight","Semicolon","Quote","Comma","Period","Slash","BackSlash","C64ArrowUp"];
    public static Dictionary<string,string> KeyboardDefaults()=>new(){["Up"]="Up",["Down"]="Down",["Left"]="Left",["Right"]="Right",["B"]="Z",["A"]="X",["Y"]="A",["X"]="S",["L"]="Q",["R"]="W",["Select"]="ShiftRight",["Start"]="Enter",["MenuSelect"]="Enter",["MenuBack"]="Escape",["MainMenu"]="F10",["Turbo"]="F9",["SaveStates"]="F12",["Fullscreen"]="F11",["Diagnostic"]="F6"};
    public static Dictionary<string,string> PadActions(IReadOnlyDictionary<string,string>? game=null)=>new(){["MenuSelect"]="@A",["MenuBack"]="@B",["MainMenu"]="@Start&@Select",["SaveStates"]="@L&@R&@Select",["Turbo"]="",["Fullscreen"]="",["Diagnostic"]=""};
    private static readonly Regex PadAtom=new(@"^(Button(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])|Axis(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])[+-]|Hat(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])(?:Up|Down|Left|Right))$",RegexOptions.CultureInvariant);

    public static bool Valid(string? expression,bool keyboard,bool mappedReferences=false)
    {
        if(expression==null||expression.Length>128)return false;
        if(expression.Length==0)return true;
        var alternatives=expression.Split(';');
        if(alternatives.Length>4)return false;
        foreach(string alternative in alternatives)
        {
            string[] members=alternative.Split('&');
            if(members.Length>4)return false;
            foreach(string member in members)
            {
                bool valid=keyboard?KeyboardKeys.Contains(member):PadAtom.IsMatch(member)||
                    (mappedReferences&&member.StartsWith("@",StringComparison.Ordinal)&&Buttons.Contains(member[1..]));
                if(!valid)return false;
            }
        }
        return true;
    }

    public static bool Matches(string? expression,IReadOnlySet<string> active)=>
        !string.IsNullOrEmpty(expression)&&expression.Split(';').Any(a=>a.Split('&').All(active.Contains));

    public static HashSet<string> Consumed(string? expression,IReadOnlySet<string> active)=>
        string.IsNullOrEmpty(expression)?[]:expression.Split(';').Where(a=>a.Split('&').All(active.Contains))
            .SelectMany(a=>a.Split('&')).ToHashSet(StringComparer.Ordinal);

    public static void Ensure(EmulatorSettings settings)
    {
        if(settings.GamepadControls==null){settings.GamepadControls=SNESEmulatorPadDefaults();foreach(var b in settings.GamepadBindings)if(Buttons.Contains(b.Key)&&b.Value>=0)settings.GamepadControls[b.Key]="Button"+b.Value;}
        foreach(var b in SNESEmulatorPadDefaults())settings.GamepadControls.TryAdd(b.Key,b.Value);
        settings.GamepadActions??=PadActions(settings.GamepadControls);foreach(var b in PadActions(settings.GamepadControls))settings.GamepadActions.TryAdd(b.Key,b.Value);
        settings.KeyboardControls??=KeyboardDefaults();foreach(var b in KeyboardDefaults())settings.KeyboardControls.TryAdd(b.Key,b.Value);
    }
    public static Dictionary<string,string> SNESEmulatorPadDefaults()=>new(){["B"]="Button0",["A"]="Button1",["Y"]="Button2",["X"]="Button3",["L"]="Button4",["R"]="Button5",["Select"]="Button6",["Start"]="Button7",["Up"]="Axis1-;Hat0Up",["Down"]="Axis1+;Hat0Down",["Left"]="Axis0-;Hat0Left",["Right"]="Axis0+;Hat0Right"};
}
