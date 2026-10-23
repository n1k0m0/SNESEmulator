using System.Text.Json;

namespace SNESEmulator.Core;

public sealed class EmulatorSettings
{
    public int AudioVolume {get;set;}=80;
    public int Filter { get; set; }
    public int Upscale { get; set; }
    public int RenderFrameLimit {get;set;}=60; // 0=unlimited, 60, 120; presentation only.
    public bool PresentationVSync {get;set;}
    public bool VideoZoom {get;set;}
    public bool TurboMode {get;set;}
    public string ResetMode {get;set;}="Warm";
    public Dictionary<string,string>? GamepadActions {get;set;}
    public Dictionary<string,string>? KeyboardControls {get;set;}
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,JsonElement>? AdditionalSettings {get;set;}
    public bool Fullscreen { get; set; }
    public bool Gamepad { get; set; } = true;
    public int ControllerPort { get; set; } = 1;
    public int GamepadDevice {get;set;}=-1;
    // Null denotes the pre-M9 button-only settings schema; migration is lossless.
    public Dictionary<string,string>? GamepadControls {get;set;}
    public string MediaDirectory { get; set; } = UserDataPaths.GetMediaDirectory();
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1991;
    public string RelayHost { get; set; } = "localhost";
    public int RelayPort { get; set; } = 1992;
    public string ConnectionId { get; set; } = "snes-session";
    public string PlayerName { get; set; } = "player";
    public bool Relay { get; set; }
    public bool Observer { get; set; }
    public Dictionary<string, int> GamepadBindings { get; set; } = new()
    {
        ["B"] = 0, ["A"] = 1, ["Y"] = 2, ["X"] = 3,
        ["L"] = 4, ["R"] = 5, ["Select"] = 6, ["Start"] = 7
    };
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PersistenceNotice {get;private set;}
    public const int MaximumBytes=64*1024;
    private static readonly JsonSerializerOptions JsonOptions=new(){MaxDepth=16,WriteIndented=true};
    public void Validate()
    {
        if(RenderFrameLimit is not (0 or 60 or 120))RenderFrameLimit=60;
        if(ResetMode is not ("Warm" or "Power"))ResetMode="Warm";
        if(GamepadActions!=null)GamepadActions=GamepadActions.Where(x=>InputBindings.Actions.Contains(x.Key)&&InputBindings.Valid(x.Value,false,true)).ToDictionary();
        if(KeyboardControls!=null)KeyboardControls=KeyboardControls.Where(x=>(InputBindings.Actions.Contains(x.Key)||InputBindings.Buttons.Contains(x.Key))&&InputBindings.Valid(x.Value,true)).ToDictionary();
        AudioVolume=Math.Clamp(AudioVolume,0,100);Filter=Math.Clamp(Filter,0,2);Upscale=Math.Clamp(Upscale,0,5);
        ControllerPort=Math.Clamp(ControllerPort,1,2);GamepadDevice=Math.Clamp(GamepadDevice,-1,255);
        Port=Math.Clamp(Port,1,65535);RelayPort=Math.Clamp(RelayPort,1,65535);
        string Clean(string? value,string fallback,int limit)
        {string cleaned=new string((value??"").Where(c=>!char.IsControl(c)).Take(limit).ToArray());return string.IsNullOrWhiteSpace(cleaned)?fallback:cleaned;}
        Host=Clean(Host,"localhost",253);RelayHost=Clean(RelayHost,"localhost",253);
        ConnectionId=Clean(ConnectionId,"snes-session",128);PlayerName=Clean(PlayerName,"player",64);
        MediaDirectory=Clean(MediaDirectory,UserDataPaths.GetMediaDirectory(),1024);
        try{MediaDirectory=Path.GetFullPath(MediaDirectory);}catch(Exception e) when(e is ArgumentException or NotSupportedException or PathTooLongException){MediaDirectory=UserDataPaths.GetMediaDirectory();}
        string[] names=["Up","Down","Left","Right","B","A","Y","X","L","R","Select","Start"];
        GamepadBindings??=new EmulatorSettings().GamepadBindings;
        GamepadBindings=GamepadBindings.Where(x=>names.Contains(x.Key)&&x.Value is >=0 and <=255).ToDictionary(x=>x.Key,x=>x.Value);
        if(GamepadControls!=null)
        {
            var controls=new Dictionary<string,string>();
            foreach(var entry in GamepadControls.Where(x=>names.Contains(x.Key)))
            {
                string token=entry.Value??"";
                bool valid=token.Length<=128&&token.Split(';').Length<=4&&token.Split(';').All(part=>System.Text.RegularExpressions.Regex.IsMatch(part,@"^(Button(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])|Axis(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])[+-]|Hat(?:[0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])(?:Up|Down|Left|Right))?$",System.Text.RegularExpressions.RegexOptions.CultureInvariant));
                if(valid||InputBindings.Valid(token,false))controls[entry.Key]=token;
            }
            GamepadControls=controls;
        }
    }
    private static bool Valid(byte[] bytes)
    {
        try{return bytes.Length<=MaximumBytes&&JsonSerializer.Deserialize<EmulatorSettings>(bytes,JsonOptions)!=null;}catch(JsonException){return false;}
    }
    public static EmulatorSettings Load(string? path=null)
    {
        try
        {
            byte[] bytes=AtomicFile.Read(path??UserDataPaths.GetSettingsPath(),MaximumBytes,Valid,out bool recovered);
            var settings=JsonSerializer.Deserialize<EmulatorSettings>(bytes,JsonOptions)!;string before=JsonSerializer.Serialize(settings);settings.Validate();
            settings.PersistenceNotice=recovered?"SETTINGS RECOVERED FROM BACKUP; DAMAGED FILE RETAINED":before!=JsonSerializer.Serialize(settings)?"INVALID SETTINGS NORMALIZED; REVIEW SETTINGS":null;
            return settings;
        }
        catch(FileNotFoundException){return new();}
        catch(Exception e) when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException)
        {return new(){PersistenceNotice="SETTINGS UNAVAILABLE: "+e.Message};}
    }
    public string Serialize(){Validate();return JsonSerializer.Serialize(this,JsonOptions);}
    public static void SaveSerialized(string json,string? path=null)
    {
        if(json.Length>MaximumBytes)throw new InvalidDataException("Settings exceed 64 KiB limit.");
        byte[] bytes=System.Text.Encoding.UTF8.GetBytes(json);if(!Valid(bytes))throw new InvalidDataException("Invalid settings; existing file retained.");
        AtomicFile.Write(path??UserDataPaths.GetSettingsPath(),stream=>stream.Write(bytes),candidate=>new FileInfo(candidate).Length<=MaximumBytes&&Valid(AtomicFile.ReadBytes(candidate,MaximumBytes)));
    }
    public void Save(string? path=null)=>SaveSerialized(Serialize(),path);
}
