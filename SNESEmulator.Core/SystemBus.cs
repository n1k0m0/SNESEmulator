namespace SNESEmulator.Core;

public interface ICpuBus
{
    byte Read(int address);
    void Write(int address, byte value);
}

public sealed class SystemBus : ICpuBus
{
    /// <summary>Logical access observer. No cycle timing or CPU/DMA-origin guarantee.</summary>
    public Action<string, int, byte>? AccessObserved { get; set; }
    public byte[] Wram { get; } = new byte[128 * 1024];
    public byte[] Io { get; } = new byte[0x400];
    public byte[] DmaRegisters { get; } = new byte[128];
    public Ppu Ppu { get; } = new();
    public Apu Apu { get; } = new();
    public ControllerPorts Controllers { get; } = new();
    public Cartridge? Cartridge { get; set; }
    public byte OpenBus { get; set; }
    public int WramAddress { get; set; }
    public bool NmiFlag { get; set; }
    public bool NmiPending { get; set; }
    public int DmaMasterCycles { get; set; }

    public byte Read(int address)
    {
        address &= 0xFFFFFF;
        int bank = address >> 16, a = address & 0xFFFF;
        byte result = OpenBus;
        if (bank is 0x7E or 0x7F) result = Wram[address & 0x1FFFF];
        else if ((bank & 0x7F) < 0x40 && a < 0x2000) result = Wram[a];
        else if ((bank & 0x7F) < 0x40 && a is >= 0x2100 and < 0x2140) result = Ppu.Read(a);
        else if ((bank & 0x7F) < 0x40 && a is >= 0x2140 and < 0x2180) result = Apu.ApuToCpu[a & 3];
        else if ((bank & 0x7F) < 0x40 && a == 0x2180) { result = Wram[WramAddress]; WramAddress = (WramAddress + 1) & 0x1FFFF; }
        else if ((bank & 0x7F) < 0x40 && a is 0x4016 or 0x4017) result = (byte)((OpenBus & 0xFC) | Controllers.Read(a & 1));
        else if ((bank & 0x7F) < 0x40 && a is >= 0x4300 and < 0x4380) result = DmaRegisters[a & 127];
        else if ((bank & 0x7F) < 0x40 && a is >= 0x4200 and < 0x4400)
        {
            result = a switch
            {
                0x4210 => (byte)((NmiFlag ? 0x80 : 0) | 2),
                0x4212 => (byte)(Ppu.InVblank ? 0x80 : 0),
                >= 0x4218 and <= 0x421B => (byte)(Controllers.Latched[(a - 0x4218) / 2] >> ((a & 1) * 8)),
                _ => Io[a - 0x4200]
            };
            if (a == 0x4210) NmiFlag = false;
        }
        else if (Cartridge is { } cart)
        {
            int sram = cart.SramOffset(address);
            if (sram >= 0) result = cart.Sram[sram];
            else if (a >= 0x8000 || (bank & 0x7F) >= 0x40) result = cart.Rom[cart.RomOffset(address)];
        }
        AccessObserved?.Invoke("read", address, result);
        return OpenBus = result;
    }

    public void Write(int address, byte value)
    {
        AccessObserved?.Invoke("write", address & 0xFFFFFF, value);
        address &= 0xFFFFFF; OpenBus = value;
        int bank = address >> 16, a = address & 0xFFFF;
        if (bank is 0x7E or 0x7F) Wram[address & 0x1FFFF] = value;
        else if ((bank & 0x7F) < 0x40 && a < 0x2000) Wram[a] = value;
        else if ((bank & 0x7F) < 0x40 && a is >= 0x2100 and < 0x2140) Ppu.Write(a, value);
        else if ((bank & 0x7F) < 0x40 && a is >= 0x2140 and < 0x2180) Apu.CpuToApu[a & 3] = value;
        else if ((bank & 0x7F) < 0x40 && a == 0x2180) { Wram[WramAddress] = value; WramAddress = (WramAddress + 1) & 0x1FFFF; }
        else if ((bank & 0x7F) < 0x40 && a is >= 0x2181 and <= 0x2183)
        {
            int shift = (a - 0x2181) * 8;
            WramAddress = ((WramAddress & ~(255 << shift)) | value << shift) & 0x1FFFF;
        }
        else if ((bank & 0x7F) < 0x40 && a == 0x4016) Controllers.Write(value);
        else if ((bank & 0x7F) < 0x40 && a is >= 0x4300 and < 0x4380) DmaRegisters[a & 127] = value;
        else if ((bank & 0x7F) < 0x40 && a is >= 0x4200 and < 0x4400)
        {
            byte old = Io[a - 0x4200]; Io[a - 0x4200] = value;
            if (a == 0x4200 && (old & 0x80) == 0 && (value & 0x80) != 0 && NmiFlag) NmiPending = true;
            if (a == 0x4203) { int v = Io[2] * value; Io[0x16] = (byte)v; Io[0x17] = (byte)(v >> 8); }
            if (a == 0x4206)
            {
                int numerator = Io[4] | Io[5] << 8;
                int q = value == 0 ? 65535 : numerator / value, r = value == 0 ? numerator : numerator % value;
                Io[0x14] = (byte)q; Io[0x15] = (byte)(q >> 8); Io[0x16] = (byte)r; Io[0x17] = (byte)(r >> 8);
            }
            if (a == 0x420B) RunDma(value);
        }
        else if (Cartridge is { } cart && cart.SramOffset(address) is var sram && sram >= 0) cart.Sram[sram] = value;
    }
    public void BeginVblank()
    {
        NmiFlag = true;
        if ((Io[0] & 0x80) != 0) NmiPending = true;
        if ((Io[0] & 1) != 0) Controllers.Latch();
    }
    private static readonly int[][] DmaPatterns = [[0], [0,1], [0,0], [0,0,1,1], [0,1,2,3], [0,1,0,1], [0,0], [0,0,1,1]];
    public void RunDma(byte channels)
    {
        for (int ch = 0; ch < 8; ch++)
        {
            if ((channels & (1 << ch)) == 0) continue;
            int d = ch * 16, control = DmaRegisters[d], source = DmaRegisters[d+2] | DmaRegisters[d+3] << 8;
            int bank = DmaRegisters[d+4] << 16, count = DmaRegisters[d+5] | DmaRegisters[d+6] << 8;
            if (count == 0) count = 65536;
            var pattern = DmaPatterns[control & 7];
            for (int i = 0; i < count; i++)
            {
                int b = 0x2100 | ((DmaRegisters[d+1] + pattern[i % pattern.Length]) & 255);
                if ((control & 0x80) == 0) Write(b, Read(bank | source)); else Write(bank | source, Read(b));
                if ((control & 8) == 0) source = (source + ((control & 16) != 0 ? -1 : 1)) & 65535;
            }
            DmaRegisters[d+2] = (byte)source; DmaRegisters[d+3] = (byte)(source >> 8);
            DmaRegisters[d+5] = DmaRegisters[d+6] = 0;
            DmaMasterCycles += 8 + count * 8;
        }
        Io[0x0B] = 0;
    }
    public void Reset()
    {
        Array.Clear(Wram); Array.Clear(Io); Array.Clear(DmaRegisters);
        Ppu.Reset(); Apu.Reset(); WramAddress = 0; OpenBus = 0; NmiPending = NmiFlag = false; DmaMasterCycles = 0;
        Array.Clear(Controllers.Buttons); Controllers.Latch(); Controllers.Write(0);
    }
}
