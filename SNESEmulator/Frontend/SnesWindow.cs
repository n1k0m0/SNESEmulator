using OpenTK.Input;
using OpenTK.Mathematics;
using SNESEmulator.Core;
using SNESEmulator.Network;
using SNESEmulator.SaveStates;
using SNESEmulator.Runtime;
using SharpPixels;
using Hat = OpenTK.Windowing.Common.Input.Hat;

namespace SNESEmulator.Frontend;

public sealed partial class SnesWindow : SharpPixelsWindow
{
    private enum Menu { None, Main, Settings, Network, Media, Saves, Reset, Delete, Controller }
    private readonly EmulationWorker worker = new();
    private readonly BatteryPersistence battery;
    private readonly BatteryAutosave autosave;
    private readonly SettingsPersistence settingsPersistence;
    private readonly HostPresentation presentation;
    // Detached media is used only for previews; it is never the worker cartridge.
    private Cartridge? previewCartridge;
    private EmulatorSettings settings = EmulatorSettings.Load();
    private readonly SnesNetServer server;
    private readonly SnesNetClient client = new();
    private readonly UiMailbox pending = new();
    private readonly CancellationTokenSource lifetime=new();
    private Task mediaTask=Task.CompletedTask,previewTask=Task.CompletedTask,networkTask=Task.CompletedTask;
    private Exception? unreportedMediaError;
    private string? nextHostMenu;
    private Menu menu;
    private int selection, selectedClient;
    private bool turbo, busy, capturing, mediaBusy,closing;
    private bool? sentPause,sentTurbo;
    private bool localSuppressed=true,captureArmed;
    private readonly GamepadSession padSession=new();
    private HashSet<string> rawPad=[];
    private string? padIdentity;
    private string? lastPadIdentity;
    private ushort padButtons;
    private int mappingGeneration;
    private double inputTimer, repeatTimer;
    private string status = "F10 MENU - F12 SAVESTATES - F6 DIAGNOSTIC ROM";
    private string hostMenu = "", password = "", relayPassword = "";
    private List<string> entries = [];
    private string? deletePath;
    private readonly EpochPublication<SnesNetVideoFrame> remoteVideo=new();
    private string? previewPath;
    private uint[]? savePreview;
    private bool previewBusy;
    private int previewWidth,previewHeight;

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
        battery=new(()=>worker.BatteryAsync(),BatteryPath,pending.SetStatus);
        autosave=new(battery,pending.SetStatus);
        settingsPersistence=new(pending.SetStatus);
        if(settings.PersistenceNotice!=null)status=settings.PersistenceNotice;
        server=new(256,224);
        if(settings.GamepadControls==null){settings.GamepadControls=GamepadMapping.Defaults();foreach(var binding in settings.GamepadBindings)if(GamepadMapping.Names.Contains(binding.Key)&&binding.Value>=0)settings.GamepadControls[binding.Key]="Button"+binding.Value;}
        foreach(var binding in GamepadMapping.Defaults())settings.GamepadControls.TryAdd(binding.Key,binding.Value);
        presentation=new(worker,server,pending.SetStatus);
        startupRom = rom; ClientSize = new Vector2i(1024, 576); UpdateFrequency = 60;
        server.StatusChanged += pending.SetStatus;
        client.StatusChanged += pending.SetStatus;
        client.HostOverlayStatusChanged += s => Interlocked.Exchange(ref nextHostMenu,s);
        client.SessionStarted += remoteVideo.Clear;
        client.TimelineChanged += remoteVideo.Update;
        client.FrameReceived += f =>
        {
            remoteVideo.Publish(f.Timeline,f);
        };
    }
    public override void OnUserInitialize()
    {
        // SharpPixels resets this during OnLoad, so apply the cap after that reset.
        UpdateFrequency = 60;
        if (settings.Fullscreen) ToggleFullscreen();
        Directory.CreateDirectory(UserDataPaths.GetMediaDirectory());
        if (startupRom != null) Try(() => LoadRom(startupRom));
        StartManagerIpc();
    }
    public override void OnUserUpdate(double time)
    {
        MeasureUi();
        if(managerCloseAfter!=0&&System.Diagnostics.Stopwatch.GetTimestamp()>=managerCloseAfter){Close();return;}
        autosave.Tick(time,!closing&&!mediaBusy);
        pending.Drain();
        if(pending.TakeStatus() is {} message)status=message;
        if(Interlocked.Exchange(ref nextHostMenu,null) is {} overlay)hostMenu=overlay;
        PollGamepad(time);client.AudioVolume=settings.AudioVolume/100f;presentation.Volume=settings.AudioVolume/100f;
        bool remote = client.IsConnected;
        ushort raw=(ushort)(KeyboardButtons()|padButtons);
        if(!IsFocused||menu!=Menu.None||mediaBusy)localSuppressed=true;
        if(raw==0&&IsFocused&&menu==Menu.None&&!mediaBusy)localSuppressed=false;
        ushort input = menu == Menu.None && IsFocused&&!localSuppressed ? raw : (ushort)0;
        if (remote)
        {
            inputTimer += time;
            if (inputTimer >= 0.05||input==0) { inputTimer = 0; client.SendJoystickState((ushort)~input, true); }
            client.PollLatency();
        }
        else
        {
            ushort p1=(ushort)~server.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1),p2=(ushort)~server.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2);
            if(settings.ControllerPort==1)p1|=input;else p2|=input;
            worker.SubmitInput(new(p1,p2));
        }
        bool pause=menu!=Menu.None||remote||mediaBusy||managerPaused;
        if(sentPause!=pause||sentTurbo!=turbo){sentPause=pause;sentTurbo=turbo;Observe(worker.SetModeAsync(pause,turbo));}
        var runtime=worker.Status;
        if(runtime.Fault!=null)status="CORE STOPPED: "+runtime.Fault.Split('\n')[0];
        else if(!runtime.AudioSupported)status="LAB APU RATE: LOCAL AUDIO / AUD2 MUTED (32000 HZ REQUIRED)";
        server.SetHostOverlayStatus(menu == Menu.None ? (runtime.Fault!=null ? "SERVER CORE STOPPED" : remote?"SERVER PAUSED":"") : "SERVER IN " + menu.ToString().ToUpperInvariant() + " MENU");
        DrawScreen(remote);
        Title = $"SNESEmulator | {(remote ? "CLIENT" : server.IsRunning ? "HOST" : "LOCAL")} | {(runtime.Title.Length==0?"NO CARTRIDGE":runtime.Title)} | {FPS} UI FPS";
    }
    private void DrawScreen(bool remote)
    {
        ClearScreen();
        var field=worker.Latest?.Video;
        // GPU API consumes synchronously; retain one UI copy per completed field.
        if(field!=null&&!ReferenceEquals(drawnField,field)){drawnField=field;drawnPixels=field.Argb.ToArray();}
        uint[] pixels=field!=null?drawnPixels:[];int width=field?.Geometry.Width??256,height=field?.Geometry.Height??224;
        if(remote){var frame=remoteVideo.Read(client.TimelineIdentity);pixels=frame?.Pixels??[];width=frame?.Width??256;height=frame?.Height??224;}
        if (pixels.Length>0)
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
        Text(8,276,Clip(!worker.Status.Paused&&worker.Status.PacingLateSeconds>.25?$"EMULATION LATE {worker.Status.PacingLateSeconds:F2} S - HARDWARE TICKS PRESERVED":status,82),1,192,210,225);
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
    private PpuFrame? drawnField;
    private uint[] drawnPixels=[];
    private void Open(Menu next)
    {
        menu = next; selection = 0; capturing = false;localSuppressed=true;
        worker.SubmitInput(default);
        if (next == Menu.Media) RefreshMedia();
        if (next == Menu.Saves) { RefreshSaves(); previewPath=null; savePreview=null; }
    }
    private string[] Rows() => menu switch
    {
        Menu.Main => ["SETTINGS", "NETWORK", "MEDIA    " + (worker.Status.Title.Length==0?"NO MEDIA":worker.Status.Title), "RESET", "MANAGER"],
        Menu.Settings => [$"VIDEO FILTER    {new[]{"SHARP","CRT","TV"}[settings.Filter]}",
            $"VIDEO UPSCALE   {((SharpPixelsGpuUpscaleMode)settings.Upscale).ToString().ToUpperInvariant()}",
            $"FULLSCREEN      {settings.Fullscreen}", $"GAMEPAD         {settings.Gamepad}",
            $"CONTROLLER PORT {settings.ControllerPort}", "CONTROLLER MAPPING", "RUN DIAGNOSTIC ROM", $"AUDIO VOLUME    {settings.AudioVolume}%", "RUN AUDIO DIAGNOSTIC ROM",$"GAMEPAD DEVICE  {(settings.GamepadDevice<0?"FIRST CONNECTED":settings.GamepadDevice.ToString())}"],
        Menu.Network => NetworkRows(),
        Menu.Media => entries.Select(p => p==Directory.GetParent(settings.MediaDirectory)?.FullName ? "[..] PARENT DIRECTORY" : (Directory.Exists(p) ? "[DIR] " : "") + Path.GetFileName(p)).ToArray(),
        Menu.Saves => entries.Select(Path.GetFileNameWithoutExtension).Select(n => n ?? "").ToArray(),
        Menu.Reset => ["NO - RETURN", "YES - RESET MACHINE"],
        Menu.Delete => ["NO - KEEP SAVESTATE", "YES - DELETE SAVESTATE"],
        Menu.Controller => GamepadMapping.Names.Select((name,i)=>name+"    "+settings.GamepadControls![name]+(capturing&&selection==i?"  RELEASE THEN MOVE / PRESS":"")).ToArray(),
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
        if(menu==Menu.Saves && entries.Count>0 && previewCartridge is {} cart)
        {
            string path=entries[Math.Clamp(selection,0,entries.Count-1)];
            if(previewPath!=path&&!previewBusy)
            {
                previewPath=path; savePreview=null;previewBusy=true;
                previewTask=Task.Run(()=>{try {using var compressed=new MemoryStream(ReadStateBytes(path),false);var saved=StateSerializer.Load(cart,compressed).Bus.Ppu;var image=saved.CompletedFrame??saved.Frame;var copy=image.Argb.ToArray();Post(()=>{if(previewPath==path){savePreview=copy;previewWidth=image.Geometry.Width;previewHeight=image.Geometry.Height;}});}catch(Exception e){pending.SetStatus("PREVIEW UNAVAILABLE: "+e.Message);}finally{Post(()=>previewBusy=false);}});
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
        if(!IsFocused||closing)return;
        if (key == Key.F11) { settings.Fullscreen = !settings.Fullscreen; ToggleFullscreen(); SaveSettings(); return; }
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
        if(menu==Menu.Controller&&key==Key.Delete){settings.GamepadControls![GamepadMapping.Names[selection]]="";SaveSettings();ResetLocalInput();return;}
        if (menu == Menu.Saves && key == Key.Delete && entries.Count > 0) { deletePath=entries[selection]; Open(Menu.Delete); return; }
        if (menu == Menu.Network && EditNetwork(key,modifiers)) return;
        if (key is Key.Enter or Key.Right or Key.Left || menu == Menu.Saves && key == Key.L) Activate(key == Key.Left ? -1 : 1);
    }
    private void Activate(int direction)
    {
        switch (menu)
        {
            case Menu.Main: if(selection==4){LaunchManager();Open(Menu.None);}else Open(new[]{Menu.Settings,Menu.Network,Menu.Media,Menu.Reset}[selection]); break;
            case Menu.Settings:
                switch(selection)
                {
                    case 0: settings.Filter=(settings.Filter+direction+3)%3; break;
                    case 1: settings.Upscale=(settings.Upscale+direction+6)%6; break;
                    case 2: settings.Fullscreen=!settings.Fullscreen; ToggleFullscreen(); break;
                    case 3: settings.Gamepad=!settings.Gamepad;ResetLocalInput(); break;
                    case 4: settings.ControllerPort=3-settings.ControllerPort;ResetLocalInput(); break;
                    case 5: Open(Menu.Controller); break;
                    case 6: LoadDiagnostic(); break;
                    case 7: settings.AudioVolume=Math.Clamp(settings.AudioVolume+direction*10,0,100);break;
                    case 8: LoadAudioDiagnostic();break;
                    case 9: var devices=JoystickStates.Where(p=>p!=null).Select(p=>p.Id).Prepend(-1).ToArray();int index=Array.IndexOf(devices,settings.GamepadDevice);settings.GamepadDevice=devices[(index+direction+devices.Length)%devices.Length];ResetLocalInput();break;
                }
                SaveSettings(); break;
            case Menu.Controller: capturing=true;captureArmed=false; break;
            case Menu.Media:
                if (client.IsConnected) { status="LEAVE REMOTE SESSION BEFORE LOADING MEDIA"; break; }
                if(entries.Count==0) break;
                string path=entries[selection];
                if(Directory.Exists(path)) { settings.MediaDirectory=path; selection=0; RefreshMedia(); SaveSettings(); }
                else LoadRom(path);
                break;
            case Menu.Saves:
                if (client.IsConnected || entries.Count==0) break;
                string statePath=entries[selection];RunMedia(async()=>{byte[] bytes=ReadStateBytes(statePath);await worker.LoadAsync(bytes);},()=>{Open(Menu.None);status="SAVESTATE LOADED";});break;
            case Menu.Reset:
                if(selection==1 && !client.IsConnected)RunMedia(()=>worker.ResetAsync(),()=>{Open(Menu.None);status="RESET";});else Open(Menu.Main);break;
            case Menu.Delete:
                if(selection==1 && deletePath!=null){string removed=deletePath;RunMedia(()=>Task.Run(()=>AtomicFile.DeleteLogical(removed)),()=>Open(Menu.Saves));}
                else Open(Menu.Saves); break;
            case Menu.Network: ActivateNetwork(direction); break;
        }
    }
    private byte[] ReadStateBytes(string path)
    {
        var cart=previewCartridge??throw new InvalidOperationException("Load matching cartridge first.");
        byte[] bytes=StateSerializer.ReadForLoad(cart,path,out bool recovered);
        if(recovered)pending.SetStatus("STATE RECOVERED FROM VALIDATED BACKUP; DAMAGED FILE RETAINED");
        return bytes;
    }
    private void Post(Action action)
    {
        if(closing)return;
        _=pending.PostAsync(action,lifetime.Token).ContinueWith(t=>{_ = t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
    }
    private void Observe(Task task)=>_=task.ContinueWith(t=>Post(()=>{status=t.Exception!.GetBaseException().Message;sentPause=null;sentTurbo=null;}),TaskContinuationOptions.OnlyOnFaulted);
    private void RunMedia(Func<Task> action,Action completed)
    {
        if(mediaBusy){status="MEDIA OPERATION IN PROGRESS";return;}
        mediaBusy=true;ResetLocalInput();
        mediaTask=Task.Run(async()=>{try{await worker.SetModeAsync(true,turbo);await action();Post(()=>{mediaBusy=false;sentPause=null;completed();});}
            catch(Exception e){unreportedMediaError=e;Post(()=>{mediaBusy=false;sentPause=null;status=e.Message;unreportedMediaError=null;});}});
    }
    private void LoadAudioDiagnostic()=>LoadImage(AudioDiagnosticRom.Create(),"ORIGINAL CPU -> SPC -> DSP STEREO DIAGNOSTIC");
    private void LoadDiagnostic()=>LoadImage(DiagnosticRom.Create(),"ORIGINAL SNES DIAGNOSTIC - CPU / DMA / PPU");
    private void LoadImage(byte[] image,string message,string source="")
    {
        if(client.IsConnected)return;
        RunMedia(async()=>{
            var cartridge=new Cartridge(image,source);
            await battery.SwitchAsync(cartridge.Hash,cartridge.Sram.Length,bytes=>worker.InsertAsync(image,bytes,source));
            Post(()=>previewCartridge=cartridge);
        },()=>{Open(Menu.None);status=message;});
    }
    private void LoadRom(string path)
    {
        if(client.IsConnected||mediaBusy)return;
        // Disk/validation/compression run outside the UI and hardware loops.
        RunMedia(async()=>{
            var image=await File.ReadAllBytesAsync(path);var cartridge=new Cartridge(image,path);
            await battery.SwitchAsync(cartridge.Hash,cartridge.Sram.Length,bytes=>worker.InsertAsync(image,bytes,path));
            Post(()=>previewCartridge=cartridge);
        },()=>{Open(Menu.None);status="LOADED "+worker.Status.Title;});
    }
    private static string BatteryPath(string hash)=>Path.Combine(UserDataPaths.GetBaseDirectory(),"battery",hash+".srm");
    private Task FlushSramAsync()=>battery.FlushAsync();
    private void SaveSettings(){settingsRevision++;settingsPersistence.Queue(settings.Serialize());}
    private void RefreshMedia()
    {
        if(!Directory.Exists(settings.MediaDirectory)) settings.MediaDirectory=UserDataPaths.GetMediaDirectory();
        entries=[];
        string? parent=Directory.GetParent(settings.MediaDirectory)?.FullName;
        if(parent!=null) entries.Add(parent);
        entries.AddRange(Directory.GetDirectories(settings.MediaDirectory).Order(StringComparer.OrdinalIgnoreCase));
        entries.AddRange(Directory.GetFiles(settings.MediaDirectory).Where(p=>new[]{".sfc",".smc"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Order(StringComparer.OrdinalIgnoreCase));
    }
    private string SaveFolder() => Path.Combine(UserDataPaths.GetSaveDirectory(),worker.Status.CartridgeHash.Length==0?"no-cartridge":worker.Status.CartridgeHash);
    private void RefreshSaves() => entries=Directory.Exists(SaveFolder()) ? Directory.GetFiles(SaveFolder(),"*.snessav").Concat(Directory.GetFiles(SaveFolder(),"*.snessav.bak").Select(p=>p[..^4])).Distinct(StringComparer.OrdinalIgnoreCase).OrderDescending().ToList() : [];
    private void Save()
    {
        if(client.IsConnected) return;
        string path=Path.Combine(SaveFolder(),DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff")+".snessav");
        RunMedia(async()=>{byte[] state=await worker.CaptureAsync();StateSerializer.SaveCaptured(state,path,previewCartridge??throw new InvalidOperationException("Load a cartridge first."));},()=>{RefreshSaves();selection=0;status="SAVESTATE SAVED";});
    }
    private void Try(Action action)
    {
        try { action(); } catch(Exception e) { status=e.Message; }
    }
    public override void OnUserKeyPress(SharpPixels.KeyEventArgs e) { }
    public override void OnUserKeyUp(SharpPixels.KeyEventArgs e) { }
    public override void OnUserMouseMove(int x,int y) { }
    public override void OnUserMouseClick(int x,int y,MouseState state) { }
    public override void OnUserFileDrop(string[] names) { if(!client.IsConnected && names.Length>0) Try(()=>LoadRom(names[0])); }
    protected override void OnUnload()
    {
        closing=true;lifetime.Cancel();var errors=new List<Exception>();
        void Finish(Action action){try{action();}catch(Exception e){errors.Add(e);}}
        // Own I/O drains before final capture. Failures survive window shutdown:
        // Program records last-error.log, returns nonzero and shows a normal-launch
        // error dialog. CLI probes never display blocking dialogs.
        Finish(()=>{if(managerIpc!=null)managerIpc.DisposeAsync().AsTask().GetAwaiter().GetResult();});
        Finish(()=>mediaTask.GetAwaiter().GetResult());
        if(unreportedMediaError!=null)errors.Add(unreportedMediaError);
        Finish(()=>previewTask.GetAwaiter().GetResult());
        Finish(()=>networkTask.GetAwaiter().GetResult());
        Finish(()=>autosave.Pending.GetAwaiter().GetResult());
        Finish(()=>settingsPersistence.Pending.GetAwaiter().GetResult());
        Finish(()=>worker.SetModeAsync(true,false).GetAwaiter().GetResult());
        Finish(()=>Task.Run(FlushSramAsync).GetAwaiter().GetResult());
        Finish(()=>Task.Run(()=>SettingsPersistence.WriteOrdered(settings.Serialize())).GetAwaiter().GetResult());
        Finish(battery.Dispose);Finish(presentation.Dispose);Finish(worker.Dispose);
        Finish(client.Dispose);Finish(server.Dispose);Finish(pending.Dispose);Finish(lifetime.Dispose);base.OnUnload();
        if(errors.Count!=0)throw new AggregateException("Shutdown save failed. Previous valid files were retained; see last-error.log and retry after fixing the storage error.",errors);
    }
    private ushort KeyboardButtons()
    {
        ushort buttons=0;
        foreach(var binding in KeyboardMap) if(IsKeyPressed(binding.key)) buttons|=(ushort)binding.button;
        return buttons;
    }
    private void ResetLocalInput()
    {
        padSession.Reset();mappingGeneration++;localSuppressed=true;padButtons=0;worker.SubmitInput(default);
        if(client.IsConnected)client.SendJoystickState(ushort.MaxValue,true);
    }
    private void PollGamepad(double time)
    {
        var joystick=settings.Gamepad?JoystickStates.FirstOrDefault(p=>p!=null&&(settings.GamepadDevice<0||p.Id==settings.GamepadDevice)):null;
        rawPad=[];padIdentity=null;
        if(joystick!=null)
        {
            padIdentity=$"{joystick.Id}:{joystick.Name}:{joystick.ButtonCount}:{joystick.AxisCount}:{joystick.HatCount}:{mappingGeneration}";
            for(int i=0;i<joystick.ButtonCount;i++)if(joystick.IsButtonDown(i))rawPad.Add("Button"+i);
            for(int i=0;i<joystick.AxisCount;i++){float axis=joystick.GetAxis(i);if(axis<-.45f)rawPad.Add("Axis"+i+"-");if(axis>.45f)rawPad.Add("Axis"+i+"+");}
            for(int i=0;i<joystick.HatCount;i++)
            {
                var hat=joystick.GetHat(i);
                if(hat is Hat.Up or Hat.LeftUp or Hat.RightUp)rawPad.Add("Hat"+i+"Up");
                if(hat is Hat.Down or Hat.LeftDown or Hat.RightDown)rawPad.Add("Hat"+i+"Down");
                if(hat is Hat.Left or Hat.LeftUp or Hat.LeftDown)rawPad.Add("Hat"+i+"Left");
                if(hat is Hat.Right or Hat.RightUp or Hat.RightDown)rawPad.Add("Hat"+i+"Right");
            }
        }
        var pad=(SnesButtons)GamepadMapping.Read(settings.GamepadControls!,rawPad);
        if(padIdentity!=lastPadIdentity){lastPadIdentity=padIdentity;captureArmed=false;}
        var decision=padSession.Update(IsFocused,padIdentity,(ushort)pad,rawPad.Count>0,menu!=Menu.None,capturing);
        padButtons=decision.GameButtons;
        if(!IsFocused){capturing=false;localSuppressed=true;return;}
        if(capturing)
        {
            if(rawPad.Count==0)captureArmed=true;
            else if(captureArmed&&joystick!=null)
            {
                settings.GamepadControls![GamepadMapping.Names[selection]]=rawPad.Order(StringComparer.Ordinal).First();
                capturing=false;SaveSettings();ResetLocalInput();
            }
            return;
        }
        if(decision.Saves)HandleKey(Key.F12);
        else if(decision.MainMenu)HandleKey(Key.F10);
        else if(menu!=Menu.None&&decision.Active)
        {
            var pressed=(SnesButtons)decision.Pressed;
            repeatTimer-=time;
            if(pressed!=0||repeatTimer<=0)
            {
                if((pad&SnesButtons.Up)!=0)HandleKey(Key.Up);
                if((pad&SnesButtons.Down)!=0)HandleKey(Key.Down);
                if((pad&SnesButtons.Left)!=0)HandleKey(Key.Left);
                if((pad&SnesButtons.Right)!=0)HandleKey(Key.Right);
                repeatTimer=pressed!=0?.35:.1;
            }
            if((pressed&SnesButtons.A)!=0)Try(()=>HandleKey(Key.Enter));
            if((pressed&SnesButtons.B)!=0)HandleKey(Key.Escape);
            if(menu==Menu.Saves&&(pressed&SnesButtons.X)!=0)Try(Save);
            if(menu==Menu.Saves&&(pressed&SnesButtons.Y)!=0)HandleKey(Key.Delete);
        }
    }
}
