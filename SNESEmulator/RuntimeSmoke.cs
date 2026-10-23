using System.Diagnostics;
using System.Text.Json;
using OpenTK.Input;
using SNESEmulator.Frontend;
using SNESEmulator.Core;

namespace SNESEmulator;

/// <summary>Real native window integration probe; injected app keys are explicitly synthetic.</summary>
internal static class RuntimeSmoke
{
    public static void Run(string output,string? rom,double paceSeconds=8)
    {
        Directory.CreateDirectory(output);
        using var window=new SnesWindow(rom);
        var timer=Stopwatch.StartNew();var rows=new List<object>();int step=0;double since=0,paceStart=0,nextSample=0;long master=0,field=0;bool loaded=false,deletedOnce=false;long epochBeforeLoad=0;
        void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("UI runtime smoke: "+why);}
        void Press(Key key)=>window.OnUserKeyDown(new SharpPixels.KeyEventArgs(key,0));
        void Row(string name)=>rows.Add(new{Name=name,WallSeconds=timer.Elapsed.TotalSeconds,Metrics=window.RuntimeMetrics});
        void Next(){step++;since=timer.Elapsed.TotalSeconds;}
        window.RenderFrame+=_=>{
            double now=timer.Elapsed.TotalSeconds;if(now>paceSeconds+90)throw new TimeoutException("UI smoke stage "+step);
            var runtime=window.RuntimeStatus;
            if(runtime.Fault!=null)throw new InvalidOperationException(runtime.Fault);
            if(now-since<.2)return;
            switch(step)
            {
                case 0:
                    if(!loaded){if(rom==null)Press(Key.F6);loaded=true;return;}
                    if(runtime.Field<20||window.MediaOperationPending)return;
                    Row("pace-start");master=runtime.MasterClock;field=runtime.Field;paceStart=now;nextSample=now;Next();break;
                case 1:
                    if(now>=nextSample){Row("pace-sample");nextSample=now+1;}
                    if(now-paceStart<paceSeconds)return;
                    Row("pace-end");rows.Add(new{Name="measured-pace",WallSeconds=now-paceStart,Fields=runtime.Field-field,MasterClocks=runtime.MasterClock-master,HardwareSeconds=(runtime.MasterClock-master)*(window.RuntimeProfileDenominator/(double)window.RuntimeProfileNumerator),Meaning="Actual native app sample including initial catchup debt; not sustained realtime acceptance"});
                    Press(Key.F10);Next();break;
                case 2:
                    if(!runtime.Paused)return;
                    Check(window.CurrentMenu=="Main"&&window.HasPresentedImage,"paused game stays behind menu");master=runtime.MasterClock;Row("main-paused");Screenshot(window,Path.Combine(output,"main.png"));Next();break;
                case 3:
                    Check(runtime.MasterClock==master,"menu clock stable");Press(Key.Enter);Next();break;
                case 4:Check(window.CurrentMenu=="Settings","settings path");Press(Key.F11);Row("fullscreen");Next();break;
                case 5:Press(Key.F11);for(int i=0;i<5;i++)Press(Key.Down);Press(Key.Enter);Next();break;
                case 6:Check(window.CurrentMenu=="Controller","mapping menu path");Screenshot(window,Path.Combine(output,"mapping.png"));Press(Key.Enter);Press(Key.Escape);Press(Key.F10);Press(Key.F12);Next();break;
                case 7:Check(window.CurrentMenu=="Saves","F12 saves path");Press(Key.S);Next();break;
                case 8:
                    if(window.MediaOperationPending)return;
                    if(deletedOnce&&Directory.GetFiles(UserDataPaths.GetSaveDirectory(),"*.snessav",SearchOption.AllDirectories).Length==0){Press(Key.S);return;}
                    Check(Directory.GetFiles(UserDataPaths.GetSaveDirectory(),"*.snessav",SearchOption.AllDirectories).Length==1,"save completed");Screenshot(window,Path.Combine(output,"save.png"));
                    if(!deletedOnce){Press(Key.Delete);Check(window.CurrentMenu=="Delete","delete confirmation path");Screenshot(window,Path.Combine(output,"delete.png"));Press(Key.Down);Press(Key.Enter);deletedOnce=true;return;}
                    Row("saved");epochBeforeLoad=runtime.Epoch;Press(Key.Enter);Next();break;
                case 9:
                    if(window.MediaOperationPending||window.CurrentMenu!="None")return;
                    Check(window.RuntimeStatus.Epoch>epochBeforeLoad,"state epoch available");Row("loaded");Press(Key.F9);Next();break;
                case 10:Check(runtime.Turbo,"F9 turbo active");Row("turbo");Press(Key.F9);Press(Key.F10);Press(Key.Down);Press(Key.Enter);Next();break;
                case 11:Check(window.CurrentMenu=="Network","network menu path");Row("network");Press(Key.Escape);Press(Key.Down);Press(Key.Down);Press(Key.Enter);Next();break;
                case 12:Check(window.CurrentMenu=="Media","media browser path");Row("media");Press(Key.Escape);Press(Key.Down);Press(Key.Down);Press(Key.Down);Press(Key.Enter);Next();break;
                case 13:Check(window.CurrentMenu=="Reset","reset confirmation path");Press(Key.Down);Press(Key.Enter);Next();break;
                case 14:
                    if(window.MediaOperationPending)return;
                    Check(window.CurrentMenu=="None","reset returns to game");Row("reset");Screenshot(window,Path.Combine(output,"final.png"));
                    // Intentionally close with a real save command outstanding.
                    Press(Key.F12);Press(Key.S);Check(window.MediaOperationPending,"close during owned I/O");Row("close-with-save-pending");window.Close();break;
            }
        };
        window.Run();
        Check(step==14,"all stages ran");
        Check(Directory.GetFiles(UserDataPaths.GetSaveDirectory(),"*.snessav",SearchOption.AllDirectories).Length==2,"close waits for pending save");
        // Parse every byte and validate the complete format9 after close.
        var cartridge=new SNESEmulator.Core.Cartridge(rom==null?DiagnosticRom.Create():File.ReadAllBytes(rom));
        foreach(string state in Directory.GetFiles(UserDataPaths.GetSaveDirectory(),"*.snessav",SearchOption.AllDirectories))_ = SNESEmulator.SaveStates.StateSerializer.Load(cartridge,state);
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Success=true,Rows=rows,Limits=new[]{"Synthetic app key callbacks; physical gamepad untested","No hours-long A/V soak","Short app pacing sample does not close M9a sustained gate","No hardware console evidence"}},new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static void Screenshot(SnesWindow window,string path)
    {
        using var screenshot=new System.Drawing.Bitmap(window.ClientSize.X,window.ClientSize.Y);
        var bits=screenshot.LockBits(new System.Drawing.Rectangle(0,0,screenshot.Width,screenshot.Height),System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        OpenTK.Graphics.OpenGL4.GL.ReadBuffer(OpenTK.Graphics.OpenGL4.ReadBufferMode.Front);
        OpenTK.Graphics.OpenGL4.GL.ReadPixels(0,0,screenshot.Width,screenshot.Height,OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte,bits.Scan0);
        screenshot.UnlockBits(bits);screenshot.RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipY);screenshot.Save(path,System.Drawing.Imaging.ImageFormat.Png);
    }
}
