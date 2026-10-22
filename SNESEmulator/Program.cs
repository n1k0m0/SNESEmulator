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
            if(args.Length>=2 && args[0]=="--write-diagnostic-rom")
            { File.WriteAllBytes(args[1],DiagnosticRom.Create()); return 0; }
            if(args.Length>=3 && args[0]=="--headless")
            {
                var machine=new SnesSystem(); machine.Insert(new Cartridge(File.ReadAllBytes(args[1]),args[1]));
                int frames=int.Parse(args[2]); if(frames is <1 or >100000) throw new ArgumentOutOfRangeException(nameof(frames));
                for(int i=0;i<frames;i++) machine.RunFrame();
                if(args.Length>=4) WritePpm(args[3],machine.Bus.Ppu.Pixels);
                if(args.Length>=5) File.WriteAllText(args[4],$"Frames={machine.Frames}\nInstructions={machine.Cpu.Instructions}\nPC={machine.Cpu.PBR:X2}:{machine.Cpu.PC:X4}\n");
                return 0;
            }
            if(args.Length>=2 && args[0]=="--ui-smoke")
            {
                using var preview=new SnesWindow();
                int count=0; string output=args[1];
                preview.RenderFrame+=_=>
                {
                    count++;
                    if(count==5 && args.Length>=3)
                    {
                        void Key(OpenTK.Input.Key key) => preview.OnUserKeyDown(new SharpPixels.KeyEventArgs(key,0));
                        switch(args[2])
                        {
                            case "demo": Key(OpenTK.Input.Key.F6); break;
                            case "main": Key(OpenTK.Input.Key.F10); break;
                            case "settings": Key(OpenTK.Input.Key.F10); Key(OpenTK.Input.Key.Enter); break;
                            case "network": Key(OpenTK.Input.Key.F10); Key(OpenTK.Input.Key.Down); Key(OpenTK.Input.Key.Enter); break;
                            case "saves": Key(OpenTK.Input.Key.F6); break;
                        }
                    }
                    if(count==15 && args.Length>=3 && args[2]=="saves")
                    {
                        preview.OnUserKeyDown(new SharpPixels.KeyEventArgs(OpenTK.Input.Key.F12,0));
                        preview.OnUserKeyDown(new SharpPixels.KeyEventArgs(OpenTK.Input.Key.S,0));
                    }
                    if(count!=30) return;
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
            using var window=new SnesWindow(args.Length>0?args[0]:null); window.Run(); return 0;
        }
        catch(Exception e)
        {
            Directory.CreateDirectory(UserDataPaths.GetBaseDirectory());
            File.WriteAllText(Path.Combine(UserDataPaths.GetBaseDirectory(),"last-error.log"),e.ToString());
            if(args.Length==0) System.Windows.Forms.MessageBox.Show(e.Message,"SNESEmulator");
            return 1;
        }
    }
    private static void WritePpm(string path,uint[] pixels)
    {
        using var output=File.Create(path);
        output.Write(Encoding.ASCII.GetBytes($"P6\n{Ppu.Width} {Ppu.Height}\n255\n"));
        foreach(uint p in pixels) output.Write([(byte)(p>>16),(byte)(p>>8),(byte)p]);
    }
}
