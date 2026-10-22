namespace SNESEmulator.Core;

public sealed class SnesSystem
{
    public SystemBus Bus { get; } = new();
    public Cpu65816 Cpu { get; }
    public long Frames { get; set; }
    public int MasterRemainder { get; set; }
    public SnesSystem() { Cpu = new(Bus); Bus.Reset(); }
    public void Insert(Cartridge cartridge) { Bus.Cartridge = cartridge; Reset(); }
    public void Reset() { Bus.Reset(); Cpu.Reset(); Frames = 0; MasterRemainder = 0; }
    public void RunFrame()
    {
        if (Bus.Cartridge == null) return;
        int lines = Bus.Cartridge.Pal ? 312 : 262;
        for (int line = 0; line < lines; line++)
        {
            Bus.Ppu.Scanline = line;
            if (line == 0) Bus.NmiFlag = false;
            if (line == Ppu.Height) Bus.BeginVblank();
            while (MasterRemainder < 1364)
            {
                if (Bus.NmiPending) { Bus.NmiPending = false; Cpu.Nmi(); }
                MasterRemainder += Cpu.Step() * 6 + Bus.DmaMasterCycles;
                Bus.DmaMasterCycles = 0;
            }
            MasterRemainder -= 1364;
            Bus.Ppu.RenderScanline(line);
        }
        Frames++;
    }
}
