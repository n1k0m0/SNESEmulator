using System.Text;
using SNESEmulator.Core;
using SNESEmulator.Frontend;

namespace SNESEmulator;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if(args.Length==4&&args[0]=="--audio-recovery-smoke"){return AudioRecoverySmoke.Run(args[1],int.Parse(args[2]),args[3]=="adaptive");}
            if(args.Length==6&&args[0]=="--ui-soak"){return SoakSmoke.Run(args[1],args[2],double.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture),args[4],args[5]);}
            if(args.Length>=2&&args[0]=="--ui-interaction-smoke"){InteractionSmoke.Run(args[1]);return 0;}
            if(args.Length>=2&&args[0]=="--ui-update-smoke"){UpdateSmoke.Run(args[1],args.Length>2?args[2]:"check",args.Length>3?args[3]:null);return 0;}
            if(args.Length>=2&&args[0]=="--manager-ipc-probe"){ManagerIpcProbe.Run(args[1],args.Length>2?args[2]:null);return 0;}
            if(args.Length>=2&&args[0]=="--ui-persistence-smoke"){PersistenceSmoke.Run(args[1],args.Length>=3?args[2]:"normal");return 0;}
            if(args.Length>=2&&args[0]=="--ui-runtime-smoke"){RuntimeSmoke.Run(args[1],args.Length>=3&&args[2]!="-"?args[2]:null,args.Length>=4?double.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture):8);return 0;}
            if(args.Length>=2 && args[0]=="--write-diagnostic-rom")
            { File.WriteAllBytes(args[1],DiagnosticRom.Create()); return 0; }
            if(args.Length>=3 && args[0]=="--headless")
            {
                var machine=new SnesSystem(); machine.Insert(new Cartridge(File.ReadAllBytes(args[1]),args[1]));
                int frames=int.Parse(args[2]); if(frames is <1 or >100000) throw new ArgumentOutOfRangeException(nameof(frames));
                for(int i=0;i<frames;i++) machine.RunFrame();
                if(args.Length>=4) WritePpm(args[3],machine.Bus.Ppu.CompletedFrame??machine.Bus.Ppu.Frame);
                if(args.Length>=5) File.WriteAllText(args[4],$"Frames={machine.Frames}\nInstructions={machine.Cpu.Instructions}\nPC={machine.Cpu.PBR:X2}:{machine.Cpu.PC:X4}\n");
                return 0;
            }
            if(args.Length>=2 && args[0]=="--ui-smoke")
            {
                using var preview=new SnesWindow(args.Length>=4?args[3]:null);
                int count=0;bool savesTriggered=false; string output=args[1];
                preview.RenderFrame+=_=>
                {
                    count++;
                    if(count==5 && args.Length>=3)
                    {
                        void Key(OpenTK.Input.Key key) => preview.ProbeKey(key);
                        switch(args[2])
                        {
                            case "demo": Key(OpenTK.Input.Key.F6); break;
                            case "main": Key(OpenTK.Input.Key.F10); break;
                            case "settings": Key(OpenTK.Input.Key.F10); Key(OpenTK.Input.Key.Enter); break;
                            case "network": Key(OpenTK.Input.Key.F10); Key(OpenTK.Input.Key.Down); Key(OpenTK.Input.Key.Enter); break;
                            case "saves": Key(OpenTK.Input.Key.F6); break;
                            case "display-stretch":
                                preview.ProbeInput(true,null,new HashSet<string>(),new HashSet<string>());
                                preview.ProbeOpen("Settings",17);preview.ProbeActivate();
                                if(!preview.ProbeSettings.VideoStretch)throw new InvalidOperationException("Stretch menu did not enable stretching");
                                Key(OpenTK.Input.Key.F10);break;
                        }
                    }
                    if(count>1800)throw new TimeoutException("Native preview did not reach a completed worker field");
                    if(count>=15 && !savesTriggered && args.Length>=3 && args[2]=="saves"&&!preview.MediaOperationPending&&preview.RuntimeStatus.Field>0)
                    {
                        savesTriggered=true;
                        preview.ProbeKey(OpenTK.Input.Key.F12);
                        preview.ProbeKey(OpenTK.Input.Key.S);
                    }
                    if(count<30||preview.MediaOperationPending) return;
                    // Allow transient notifications to fade when inspecting the
                    // complete display area; these modes use isolated probe settings.
                    if(args.Length>=3&&args[2].StartsWith("display",StringComparison.Ordinal)&&(count<480||!preview.HasPresentedImage))return;
                    if(args.Length>=3&&args[2] is "demo" or "saves"&&!preview.HasPresentedImage)return;
                    if(args.Length>=3&&args[2]=="saves"&&!savesTriggered)return;
                    using var screenshot=new System.Drawing.Bitmap(preview.ClientSize.X,preview.ClientSize.Y);
                    var bits=screenshot.LockBits(new System.Drawing.Rectangle(0,0,screenshot.Width,screenshot.Height),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    OpenTK.Graphics.OpenGL4.GL.ReadBuffer(OpenTK.Graphics.OpenGL4.ReadBufferMode.Front);
                    OpenTK.Graphics.OpenGL4.GL.ReadPixels(0,0,screenshot.Width,screenshot.Height,
                        OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte,bits.Scan0);
                    screenshot.UnlockBits(bits);
                    screenshot.RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipY);
                    screenshot.Save(output,System.Drawing.Imaging.ImageFormat.Png);
                    preview.Close();
                };
                preview.Run(); return 0;
            }
            SNESEmulator.Management.PreparedUpdate? install;
            using(var window=new SnesWindow(args.Length>0?args[0]:null)){window.Run(); install=window.InstallAfterNormalClose;}
            if(install != null)SnesWindow.StartPreparedInstaller(install); return 0;
        }
        catch(Exception e)
        {
            try{Directory.CreateDirectory(UserDataPaths.GetBaseDirectory());File.WriteAllText(Path.Combine(UserDataPaths.GetBaseDirectory(),"last-error.log"),e.ToString());}
            catch(Exception logError){Console.Error.WriteLine("Could not write last-error.log: "+logError.Message);}
            Console.Error.WriteLine(e);
            bool automated=args.Length>0&&args[0].StartsWith("--",StringComparison.Ordinal);
            if(!automated)System.Windows.Forms.MessageBox.Show(e.Message,"SNESEmulator");
            return 1;
        }
    }
    private static void WritePpm(string path,PpuFrame field)
    {
        using var output=File.Create(path);
        output.Write(Encoding.ASCII.GetBytes($"P6\n{field.Geometry.Width} {field.Geometry.Height}\n255\n"));
        foreach(uint p in field.Argb.Span) output.Write([(byte)(p>>16),(byte)(p>>8),(byte)p]);
    }
}
