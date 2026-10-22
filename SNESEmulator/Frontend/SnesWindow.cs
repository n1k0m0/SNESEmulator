using System.Collections.Concurrent;
using OpenTK.Input;
using OpenTK.Mathematics;
using SNESEmulator.Core;
using SNESEmulator.Network;
using SNESEmulator.SaveStates;
using SharpPixels;
using Hat = OpenTK.Windowing.Common.Input.Hat;

namespace SNESEmulator.Frontend;

public sealed partial class SnesWindow : SharpPixelsWindow
{
    private enum Menu { None, Main, Settings, Network, Media, Saves, Reset, Delete, Controller }
    private SnesSystem machine = new();
    private readonly EmulatorSettings settings = EmulatorSettings.Load();
    private readonly SnesNetServer server;
    private readonly SnesNetClient client = new();
    private readonly ConcurrentQueue<Action> pending = new();
    private PcmAudioOutput? localAudio;
    private bool audioAttempted;
    private Menu menu;
    private int selection, selectedClient;
    private bool turbo, fault, busy, capturing;
    private double accumulator, inputTimer, repeatTimer;
    private SnesButtons previousPad;
    private string status = "F10 MENU - F12 SAVESTATES - F6 DIAGNOSTIC ROM";
    private string hostMenu = "", password = "", relayPassword = "";
    private List<string> entries = [];
    private string? deletePath;
    private uint[]? remotePixels;
    private string? previewPath;
    private uint[]? savePreview;
    private int previewWidth,previewHeight;
    private int remoteWidth,remoteHeight;
    private readonly string? startupRom;
    private static readonly Dictionary<char, byte[]> Font = CreateOverlayFont();
    private static readonly (Key key, SnesButtons button)[] KeyboardMap =
    [
        (Key.Up,SnesButtons.Up),(Key.Down,SnesButtons.Down),(Key.Left,SnesButtons.Left),(Key.Right,SnesButtons.Right),
        (Key.Z,SnesButtons.B),(Key.X,SnesButtons.A),(Key.A,SnesButtons.Y),(Key.S,SnesButtons.X),
        (Key.Q,SnesButtons.L),(Key.W,SnesButtons.R),(Key.Enter,SnesButtons.Start),(Key.ShiftRight,SnesButtons.Select)
    ];
    public SnesWindow(string? rom = null) : base(512, 288, "SNESEmulator - C# core")
    {
        server=new(machine.Bus.Ppu.FrameGeometry.Width,machine.Bus.Ppu.FrameGeometry.Height);
        startupRom = rom; ClientSize = new Vector2i(1024, 576); UpdateFrequency = 60;
        server.StatusChanged += s => pending.Enqueue(() => status = s);
        client.StatusChanged += s => pending.Enqueue(() => status = s);
        client.HostOverlayStatusChanged += s => pending.Enqueue(() => hostMenu = s);
        client.FrameReceived += f =>
        {
            // The decoder reuses its buffer: publish an owned copy to the UI thread.
            var pixels = f.Pixels.ToArray();
            lock (client) { remotePixels = pixels; remoteWidth = f.Width; remoteHeight = f.Height; }
        };
    }
    public override void OnUserInitialize()
    {
        // SharpPixels resets this during OnLoad, so apply the cap after that reset.
        UpdateFrequency = 60;
        if (settings.Fullscreen) ToggleFullscreen();
        Directory.CreateDirectory(UserDataPaths.GetMediaDirectory());
        if (startupRom != null) Try(() => LoadRom(startupRom));
    }
    public override void OnUserUpdate(double time)
    {
        while (pending.TryDequeue(out var action)) action();
        PollGamepad(time);client.AudioVolume=settings.AudioVolume/100f;
        bool remote = client.IsConnected;
        if (!IsFocused) { machine.Bus.Controllers.Buttons[0] = machine.Bus.Controllers.Buttons[1] = 0; }
        ushort input = menu == Menu.None && IsFocused ? (ushort)(KeyboardButtons() | ReadPad()) : (ushort)0;
        if (remote)
        {
            inputTimer += time;
            if (inputTimer >= 0.05) { inputTimer = 0; client.SendJoystickState((ushort)~input, true); }
            client.PollLatency();
        }
        else
        {
            machine.Bus.Controllers.Buttons[0] = (ushort)~server.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1);
            machine.Bus.Controllers.Buttons[1] = (ushort)~server.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2);
            machine.Bus.Controllers.Buttons[settings.ControllerPort - 1] |= input;
            if (menu == Menu.None && !fault && machine.Bus.Cartridge != null)
            {
                double fieldTime = machine.Profile.ClockSeconds(machine.Profile.FieldMasterClocks(machine.Clock.Field,machine.Clock.Interlace));
                accumulator = Math.Min(accumulator + time * (turbo ? 4 : 1), fieldTime * 4);
                while (accumulator >= machine.Profile.ClockSeconds(machine.Clock.ClocksUntilFieldEnd) && !fault)
                {
                    long before = machine.Clock.Now;
                    Try(() => { machine.RunFrame(); PresentAudio(); server.BroadcastVideoFrame(machine.Bus.Ppu.CompletedFrame??machine.Bus.Ppu.Frame); }, true);
                    accumulator -= machine.Profile.ClockSeconds(machine.Clock.Now-before);
                }
            }
            else {accumulator = 0;localAudio?.Clear();machine.Bus.Apu.Dsp.ClearPresentation();}
        }
        server.SetHostOverlayStatus(menu == Menu.None ? (fault ? "SERVER CORE STOPPED" : "") : "SERVER IN " + menu.ToString().ToUpperInvariant() + " MENU");
        DrawScreen(remote);
        Title = $"SNESEmulator | {(remote ? "CLIENT" : server.IsRunning ? "HOST" : "LOCAL")} | {machine.Bus.Cartridge?.Title ?? "NO CARTRIDGE"} | {FPS} FPS";
    }
    private void DrawScreen(bool remote)
    {
        ClearScreen();
        var field=machine.Bus.Ppu.CompletedFrame??machine.Bus.Ppu.Frame;
        uint[] pixels = field.Argb.ToArray(); int width = field.Geometry.Width, height = field.Geometry.Height;
        if (remote) lock (client) { if (remotePixels != null) { pixels = remotePixels; width = remoteWidth; height = remoteHeight; } }
        if (remote || machine.Bus.Cartridge != null)
            DrawArgbPixelsGpuPresented(pixels,width,height,0,0,width,height,85,12,342,256,
                (SharpPixelsGpuFilterMode)settings.Filter,(SharpPixelsGpuUpscaleMode)settings.Upscale,
                settings.Upscale is 2 or 4 ? 3 : settings.Upscale == 5 ? 4 : 2,8,10,18);
        else
        {
            Text(34,58,"SNES EMULATOR",3,108,214,182);
            Text(34,100,"OWN C# CORE - DEVELOPMENT BUILD",1);
            Text(34,126,"F10  SETTINGS / NETWORK / MEDIA / RESET",1);
            Text(34,144,"F12  SAVE AND LOAD    F11 FULLSCREEN",1);
            Text(34,162,"F6   RUN ORIGINAL DIAGNOSTIC ROM",1);
            Text(34,196,"CORE: CPU / SPC700 / DMA / HDMA / PPU0-7",1);
            Text(34,214,"SPRITES / COLOR MATH / NATIVE STEREO DSP",1,255,220,140);
        }
        Text(8,276,Clip(status,82),1,192,210,225);
        if (remote && hostMenu.Length > 0) { DrawFilledRectangleWithAlpha(8,4,496,18,10,14,24,240); Text(14,9,Clip(hostMenu,78),1); }
        if (turbo) Text(452,8,"TURBO",1,255,220,140);
        if (menu != Menu.None) DrawMenu();
    }
    private void Text(int x, int y, string text, int scale = 1, byte r = 240, byte g = 248, byte b = 255)
    {
        foreach (char ch in text.ToUpperInvariant())
        {
            if (Font.TryGetValue(ch, out var glyph))
                for (int row = 0; row < 7; row++) for (int col = 0; col < 5; col++)
                    if ((glyph[row] & (1 << (4-col))) != 0) DrawFilledRectangle(x+col*scale,y+row*scale,scale,scale,r,g,b);
            x += 6*scale;
        }
    }
    private static string Clip(string value, int count) => value.Length <= count ? value : value[..(count-3)] + "...";
    private void Open(Menu next)
    {
        menu = next; selection = 0; capturing = false;
        if (next == Menu.Media) RefreshMedia();
        if (next == Menu.Saves) { RefreshSaves(); previewPath=null; savePreview=null; }
    }
    private string[] Rows() => menu switch
    {
        Menu.Main => ["SETTINGS", "NETWORK", "MEDIA    " + (machine.Bus.Cartridge?.Title ?? "NO MEDIA"), "RESET"],
        Menu.Settings => [$"VIDEO FILTER    {new[]{"SHARP","CRT","TV"}[settings.Filter]}",
            $"VIDEO UPSCALE   {((SharpPixelsGpuUpscaleMode)settings.Upscale).ToString().ToUpperInvariant()}",
            $"FULLSCREEN      {settings.Fullscreen}", $"GAMEPAD         {settings.Gamepad}",
            $"CONTROLLER PORT {settings.ControllerPort}", "CONTROLLER MAPPING", "RUN DIAGNOSTIC ROM", $"AUDIO VOLUME    {settings.AudioVolume}%", "RUN AUDIO DIAGNOSTIC ROM"],
        Menu.Network => NetworkRows(),
        Menu.Media => entries.Select(p => p==Directory.GetParent(settings.MediaDirectory)?.FullName ? "[..] PARENT DIRECTORY" : (Directory.Exists(p) ? "[DIR] " : "") + Path.GetFileName(p)).ToArray(),
        Menu.Saves => entries.Select(Path.GetFileNameWithoutExtension).Select(n => n ?? "").ToArray(),
        Menu.Reset => ["NO - RETURN", "YES - RESET MACHINE"],
        Menu.Delete => ["NO - KEEP SAVESTATE", "YES - DELETE SAVESTATE"],
        Menu.Controller => settings.GamepadBindings.Select(kv => kv.Key + "    BUTTON " + kv.Value + (capturing && settings.GamepadBindings.Keys.ElementAt(selection)==kv.Key ? "  PRESS BUTTON" : "")).ToArray(),
        _ => []
    };
    private void DrawMenu()
    {
        int x = 30, y = 20, w = 452, h = 242;
        DrawFilledRectangleWithAlpha(x,y,w,h,10,14,24,244);
        DrawLine(x,y,x+w,y,108,214,182); DrawLine(x,y+h,x+w,y+h,108,214,182);
        DrawLine(x,y,x,y+h,108,214,182); DrawLine(x+w,y,x+w,y+h,108,214,182);
        Text(x+16,y+12,menu == Menu.Main ? "MAIN MENU" : menu.ToString(),2);
        Text(x+16,y+34,menu == Menu.Saves ? "S/F5 SAVE  ENTER LOAD  DEL DELETE  ESC BACK" : "UP/DOWN SELECT  ENTER CHANGE  ESC BACK",1,192,210,225);
        var rows = Rows(); int first = Math.Max(0,selection-8);
        for (int i = first; i < Math.Min(rows.Length,first+10); i++)
            Text(x+16,y+56+(i-first)*15,(selection == i ? "> " : "  ")+Clip(rows[i],menu==Menu.Saves?38:66),1,
                selection==i?(byte)255:(byte)192, selection==i?(byte)243:(byte)210, selection==i?(byte)168:(byte)225);
        if(menu==Menu.Saves && entries.Count>0 && machine.Bus.Cartridge is {} cart)
        {
            string path=entries[Math.Clamp(selection,0,entries.Count-1)];
            if(previewPath!=path)
            {
                previewPath=path; savePreview=null;
                try { var saved=StateSerializer.Load(cart,path).Bus.Ppu;var image=saved.CompletedFrame??saved.Frame;savePreview=image.Argb.ToArray();previewWidth=image.Geometry.Width;previewHeight=image.Geometry.Height; }
                catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException) { status="PREVIEW UNAVAILABLE: "+e.Message; }
            }
            if(savePreview!=null) { DrawArgbPixelsCroppedStretched(savePreview,previewWidth,previewHeight,0,0,previewWidth,previewHeight,330,112,128,96); Text(330,98,"PREVIEW",1,108,214,182); }
        }
        if(rows.Length>10)
        {
            int barHeight=Math.Max(8,145*10/rows.Length), barY=y+56+(145-barHeight)*selection/Math.Max(1,rows.Length-1);
            DrawFilledRectangle(x+w-10,y+56,3,145,40,48,60); DrawFilledRectangle(x+w-10,barY,3,barHeight,108,214,182);
        }
        if (rows.Length == 0) Text(x+16,y+60,"NO ENTRIES",1);
        Text(x+16,y+h-17,menu == Menu.Media ? Clip(settings.MediaDirectory,67) : $"{Math.Min(selection+1,rows.Length)}/{rows.Length}",1,108,214,182);
    }
    public override void OnUserKeyDown(SharpPixels.KeyEventArgs e) => Try(() => HandleKey(e.Key,e.Modifiers));
    private void HandleKey(Key key, KeyModifiers modifiers = 0)
    {
        if (key == Key.F11) { settings.Fullscreen = !settings.Fullscreen; ToggleFullscreen(); settings.Save(); return; }
        if (key == Key.F10) { Open(menu == Menu.None ? Menu.Main : Menu.None); return; }
        if (key == Key.F12) { if (client.IsConnected) { status="SAVESTATES ARE MANAGED BY HOST"; return; } Open(menu==Menu.Saves?Menu.None:Menu.Saves); return; }
        if (key == Key.F9 && !client.IsConnected) { turbo = !turbo; return; }
        if (key == Key.F6 && !client.IsConnected) { LoadDiagnostic(); return; }
        if (menu == Menu.None) return;
        if (key == Key.Escape) { Open(menu == Menu.Main ? Menu.None : menu is Menu.Delete ? Menu.Saves : Menu.Main); return; }
        if (capturing) return;
        var rows = Rows();
        if (key == Key.Up) { selection = (selection+Math.Max(1,rows.Length)-1)%Math.Max(1,rows.Length); return; }
        if (key == Key.Down) { selection = (selection+1)%Math.Max(1,rows.Length); return; }
        if (key == Key.PageDown) { selection = Math.Min(Math.Max(0,rows.Length-1),selection+10); return; }
        if (key == Key.PageUp) { selection = Math.Max(0,selection-10); return; }
        if(menu==Menu.Media && key is >=Key.A and <=Key.Z)
        {
            char initial=(char)('A'+key-Key.A);
            for(int offset=1;offset<=entries.Count;offset++)
            {
                int candidate=(selection+offset)%entries.Count;
                if(Path.GetFileName(entries[candidate]).StartsWith(initial.ToString(),StringComparison.OrdinalIgnoreCase)) {selection=candidate;break;}
            }
            return;
        }
        if (menu == Menu.Saves && key is Key.S or Key.F5) { Save(); return; }
        if (menu == Menu.Saves && key == Key.Delete && entries.Count > 0) { deletePath=entries[selection]; Open(Menu.Delete); return; }
        if (menu == Menu.Network && EditNetwork(key,modifiers)) return;
        if (key is Key.Enter or Key.Right or Key.Left || menu == Menu.Saves && key == Key.L) Activate(key == Key.Left ? -1 : 1);
    }
    private void Activate(int direction)
    {
        switch (menu)
        {
            case Menu.Main: Open(new[]{Menu.Settings,Menu.Network,Menu.Media,Menu.Reset}[selection]); break;
            case Menu.Settings:
                switch(selection)
                {
                    case 0: settings.Filter=(settings.Filter+direction+3)%3; break;
                    case 1: settings.Upscale=(settings.Upscale+direction+6)%6; break;
                    case 2: settings.Fullscreen=!settings.Fullscreen; ToggleFullscreen(); break;
                    case 3: settings.Gamepad=!settings.Gamepad; break;
                    case 4: settings.ControllerPort=3-settings.ControllerPort; break;
                    case 5: Open(Menu.Controller); break;
                    case 6: LoadDiagnostic(); break;
                    case 7: settings.AudioVolume=Math.Clamp(settings.AudioVolume+direction*10,0,100);break;
                    case 8: LoadAudioDiagnostic();break;
                }
                settings.Save(); break;
            case Menu.Controller: capturing=true; break;
            case Menu.Media:
                if (client.IsConnected) { status="LEAVE REMOTE SESSION BEFORE LOADING MEDIA"; break; }
                if(entries.Count==0) break;
                string path=entries[selection];
                if(Directory.Exists(path)) { settings.MediaDirectory=path; selection=0; RefreshMedia(); settings.Save(); }
                else LoadRom(path);
                break;
            case Menu.Saves:
                if (client.IsConnected || entries.Count==0) break;
                var cart=machine.Bus.Cartridge ?? throw new InvalidOperationException("LOAD THE MATCHING ROM FIRST");
                localAudio?.Clear();machine=StateSerializer.Load(cart,entries[selection]); fault=false; Open(Menu.None); status="SAVESTATE LOADED"; break;
            case Menu.Reset:
                if(selection==1 && !client.IsConnected) { machine.Reset(); localAudio?.Clear();fault=false; Open(Menu.None); status="RESET"; } else Open(Menu.Main); break;
            case Menu.Delete:
                if(selection==1 && deletePath!=null) File.Delete(deletePath);
                Open(Menu.Saves); break;
            case Menu.Network: ActivateNetwork(direction); break;
        }
    }
    private void LoadAudioDiagnostic(){if(client.IsConnected)return;FlushSram();machine.Insert(new Cartridge(AudioDiagnosticRom.Create()));localAudio?.Clear();fault=false;Open(Menu.None);status="ORIGINAL CPU -> SPC -> DSP STEREO DIAGNOSTIC";}
    private void PresentAudio()
    {
        var samples=machine.Bus.Apu.Dsp.DrainSamples();if(samples.Length==0)return;
        byte[] pcm=new byte[samples.Length*4];for(int i=0;i<samples.Length;i++){System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4),samples[i].Left);System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4+2),samples[i].Right);}
        if(!audioAttempted){audioAttempted=true;try{localAudio=new PcmAudioOutput(32000);}catch(Exception ex){status="AUDIO DEVICE UNAVAILABLE: "+ex.Message;}}
        if(localAudio!=null)localAudio.Volume=settings.AudioVolume/100f;
        for(int offset=0;offset<samples.Length;offset+=2048){int frames=Math.Min(2048,samples.Length-offset);byte[] chunk=pcm.AsSpan(offset*4,frames*4).ToArray();server.BroadcastAudio(chunk,chunk.Length,samples[offset].Index,samples[offset].Crystal);localAudio?.Write(chunk,chunk.Length);}
    }
    private void LoadDiagnostic() { if(client.IsConnected) return; FlushSram(); machine.Insert(new Cartridge(DiagnosticRom.Create())); localAudio?.Clear();fault=false; Open(Menu.None); status="ORIGINAL SNES DIAGNOSTIC - CPU / DMA / PPU"; }
    private void LoadRom(string path)
    {
        var cartridge=new Cartridge(File.ReadAllBytes(path),path);
        string battery=BatteryPath(cartridge);
        if(File.Exists(battery)) { byte[] bytes=File.ReadAllBytes(battery); if(bytes.Length==cartridge.Sram.Length) bytes.CopyTo(cartridge.Sram,0); }
        FlushSram(); machine.Insert(cartridge); localAudio?.Clear();fault=false; Open(Menu.None); status="LOADED "+cartridge.Title;
    }
    private static string BatteryPath(Cartridge c) => Path.Combine(UserDataPaths.GetBaseDirectory(),"battery",c.Hash+".srm");
    private void FlushSram()
    {
        if(machine.Bus.Cartridge is not {} c || c.Sram.Length==0) return;
        string path=BatteryPath(c); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path+".tmp",c.Sram); File.Move(path+".tmp",path,true);
    }
    private void RefreshMedia()
    {
        if(!Directory.Exists(settings.MediaDirectory)) settings.MediaDirectory=UserDataPaths.GetMediaDirectory();
        entries=[];
        string? parent=Directory.GetParent(settings.MediaDirectory)?.FullName;
        if(parent!=null) entries.Add(parent);
        entries.AddRange(Directory.GetDirectories(settings.MediaDirectory).Order(StringComparer.OrdinalIgnoreCase));
        entries.AddRange(Directory.GetFiles(settings.MediaDirectory).Where(p=>new[]{".sfc",".smc"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Order(StringComparer.OrdinalIgnoreCase));
    }
    private string SaveFolder() => Path.Combine(UserDataPaths.GetSaveDirectory(),machine.Bus.Cartridge?.Hash ?? "no-cartridge");
    private void RefreshSaves() => entries=Directory.Exists(SaveFolder()) ? Directory.GetFiles(SaveFolder(),"*.snessav").OrderDescending().ToList() : [];
    private void Save()
    {
        if(client.IsConnected) return;
        StateSerializer.Save(machine,Path.Combine(SaveFolder(),DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff")+".snessav"));
        RefreshSaves(); selection=0; status="SAVESTATE SAVED";
    }
    private void Try(Action action, bool core = false)
    {
        try { action(); }
        catch(Exception e) { status=e.Message; if(core) fault=true; }
    }
    public override void OnUserKeyPress(SharpPixels.KeyEventArgs e) { }
    public override void OnUserKeyUp(SharpPixels.KeyEventArgs e) { }
    public override void OnUserMouseMove(int x,int y) { }
    public override void OnUserMouseClick(int x,int y,MouseState state) { }
    public override void OnUserFileDrop(string[] names) { if(!client.IsConnected && names.Length>0) Try(()=>LoadRom(names[0])); }
    protected override void OnUnload()
    {
        Try(()=>{ FlushSram(); settings.Save(); });
        localAudio?.Dispose();client.Dispose(); server.Dispose(); base.OnUnload();
    }
    private ushort KeyboardButtons()
    {
        ushort buttons=0;
        foreach(var binding in KeyboardMap) if(IsKeyPressed(binding.key)) buttons|=(ushort)binding.button;
        return buttons;
    }
    private ushort ReadPad()
    {
        if(!settings.Gamepad) return 0;
        ushort buttons=0;
        foreach(var pad in JoystickStates)
        {
            if(pad==null) continue;
            foreach(var binding in settings.GamepadBindings)
                if(binding.Value>=0 && binding.Value<pad.ButtonCount && pad.IsButtonDown(binding.Value) && Enum.TryParse<SnesButtons>(binding.Key,out var b)) buttons|=(ushort)b;
            if(pad.AxisCount>0) { if(pad.GetAxis(0)<-0.45f) buttons|=(ushort)SnesButtons.Left; if(pad.GetAxis(0)>0.45f) buttons|=(ushort)SnesButtons.Right; }
            if(pad.AxisCount>1) { if(pad.GetAxis(1)<-0.45f) buttons|=(ushort)SnesButtons.Up; if(pad.GetAxis(1)>0.45f) buttons|=(ushort)SnesButtons.Down; }
            if(pad.HatCount>0)
            {
                var hat=pad.GetHat(0);
                if(hat is Hat.Up or Hat.LeftUp or Hat.RightUp) buttons|=(ushort)SnesButtons.Up;
                if(hat is Hat.Down or Hat.LeftDown or Hat.RightDown) buttons|=(ushort)SnesButtons.Down;
                if(hat is Hat.Left or Hat.LeftUp or Hat.LeftDown) buttons|=(ushort)SnesButtons.Left;
                if(hat is Hat.Right or Hat.RightUp or Hat.RightDown) buttons|=(ushort)SnesButtons.Right;
            }
        }
        return buttons;
    }
    private void PollGamepad(double time)
    {
        var pad=(SnesButtons)ReadPad(); var pressed=pad & ~previousPad;
        if(capturing)
        {
            foreach(var joystick in JoystickStates)
                if(joystick!=null) for(int b=0;b<joystick.ButtonCount;b++) if(joystick.IsButtonPressed(b))
                { settings.GamepadBindings[settings.GamepadBindings.Keys.ElementAt(selection)]=b; capturing=false; settings.Save(); break; }
        }
        else
        {
            bool chord=(pad & (SnesButtons.Start|SnesButtons.Select))==(SnesButtons.Start|SnesButtons.Select);
            bool oldChord=(previousPad & (SnesButtons.Start|SnesButtons.Select))==(SnesButtons.Start|SnesButtons.Select);
            var saveChord=SnesButtons.L|SnesButtons.R|SnesButtons.Select;
            if((pad&saveChord)==saveChord && (previousPad&saveChord)!=saveChord) HandleKey(Key.F12);
            else if(chord && !oldChord) HandleKey(Key.F10);
            else if(menu!=Menu.None)
            {
                repeatTimer-=time;
                if(pressed!=0 || repeatTimer<=0)
                {
                    if((pad&SnesButtons.Up)!=0) HandleKey(Key.Up);
                    if((pad&SnesButtons.Down)!=0) HandleKey(Key.Down);
                    if((pad&SnesButtons.Left)!=0) HandleKey(Key.Left);
                    if((pad&SnesButtons.Right)!=0) HandleKey(Key.Right);
                    repeatTimer=pressed!=0?0.35:0.1;
                }
                if((pressed&SnesButtons.A)!=0) Try(()=>HandleKey(Key.Enter));
                if((pressed&SnesButtons.B)!=0) HandleKey(Key.Escape);
                if(menu==Menu.Saves && (pressed&SnesButtons.X)!=0) Try(Save);
            }
        }
        previousPad=pad;
    }
}
