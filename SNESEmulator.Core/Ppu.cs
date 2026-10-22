namespace SNESEmulator.Core;

/// <summary>Initial scanline renderer: mode 0/1 backgrounds, 8x8 tiles, CGRAM and VRAM ports.</summary>
public sealed class Ppu
{
    public const int Width = 256, Height = 224;
    public byte[] Vram { get; } = new byte[65536];
    public byte[] Cgram { get; } = new byte[512];
    public byte[] Oam { get; } = new byte[544];
    public byte[] Registers { get; } = new byte[64];
    public uint[] Pixels { get; } = new uint[Width * Height];
    public ushort[] Scroll { get; } = new ushort[8];
    public ushort VramAddress { get; set; }
    public ushort CgramAddress { get; set; }
    public ushort OamAddress { get; set; }
    public byte ScrollLatch { get; set; }
    public int Scanline { get; set; }
    public bool InVblank => Scanline >= ((Registers[0x33]&4)!=0?240:225);

    public void Reset()
    {
        Array.Clear(Vram); Array.Clear(Cgram); Array.Clear(Oam); Array.Clear(Registers); Array.Clear(Scroll);
        VramAddress = CgramAddress = OamAddress = 0; ScrollLatch = 0; Scanline = 0;
        Registers[0] = 0x80; Array.Fill(Pixels, 0xFF000000u);
    }
    private int Increment => (Registers[0x15] & 3) switch { 0 => 1, 1 => 32, _ => 128 };
    private int VramByteAddress(bool high)
    {
        int a = VramAddress;
        a = ((Registers[0x15] >> 2) & 3) switch
        {
            1 => (a & 0xFF00) | ((a & 31) << 3) | ((a >> 5) & 7),
            2 => (a & 0xFE00) | ((a & 63) << 3) | ((a >> 6) & 7),
            3 => (a & 0xFC00) | ((a & 127) << 3) | ((a >> 7) & 7), _ => a
        };
        return ((a << 1) | (high ? 1 : 0)) & 0xFFFF;
    }
    public void Write(int register, byte value)
    {
        register &= 63;
        Registers[register] = value;
        switch (register)
        {
            case 2: OamAddress = (ushort)((OamAddress & 0x200) | value << 1); break;
            case 3: OamAddress = (ushort)((OamAddress & 0x1FF) | (value & 1) << 9); break;
            case 4: Oam[OamAddress++ % 544] = value; break;
            case >= 0x0D and <= 0x14:
                int i = register - 0x0D;
                Scroll[i] = (ushort)(((value << 8) | ScrollLatch) & 0x3FF);
                ScrollLatch = value; break;
            case 0x16: VramAddress = (ushort)((VramAddress & 0xFF00) | value); break;
            case 0x17: VramAddress = (ushort)((VramAddress & 255) | value << 8); break;
            case 0x18: case 0x19:
                bool high = register == 0x19;
                Vram[VramByteAddress(high)] = value;
                if (((Registers[0x15] & 0x80) != 0) == high) VramAddress += (ushort)Increment;
                break;
            case 0x21: CgramAddress = (ushort)(value * 2); break;
            case 0x22:
                Cgram[CgramAddress & 511] = (byte)(value & ((CgramAddress & 1) != 0 ? 127 : 255));
                CgramAddress = (ushort)((CgramAddress + 1) & 511); break;
        }
    }
    public byte Read(int register) => (register & 63) switch
    {
        0x3E => 1, 0x3F => 3, _ => 0
    };
    private uint Color(int index)
    {
        int color = Cgram[(index * 2) & 511] | Cgram[(index * 2 + 1) & 511] << 8;
        int brightness = Registers[0] & 15;
        int r = (color & 31) * 255 * brightness / 465;
        int g = ((color >> 5) & 31) * 255 * brightness / 465;
        int b = ((color >> 10) & 31) * 255 * brightness / 465;
        return 0xFF000000u | (uint)(r << 16 | g << 8 | b);
    }
    public void RenderScanline(int y)
    {
        if (y >= Height) return;
        bool blank = (Registers[0] & 0x80) != 0;
        for (int x = 0; x < Width; x++)
        {
            uint color = blank ? 0xFF000000 : Color(0);
            int mode = Registers[5] & 7;
            if (!blank && mode is 0 or 1)
            {
                // Basic low/high priority backgrounds. OBJ/window/color math are later phases.
                for (int priority = 0; priority < 2; priority++)
                for (int bg = mode == 0 ? 3 : 2; bg >= 0; bg--)
                {
                    if ((Registers[0x2C] & (1 << bg)) == 0) continue;
                    int bpp = mode == 0 || bg == 2 ? 2 : 4;
                    int sx = (x + Scroll[bg * 2]) & 1023, sy = (y + Scroll[bg * 2 + 1]) & 1023;
                    int map = Registers[7 + bg], tileX = sx >> 3, tileY = sy >> 3;
                    int screenX = (map & 1) != 0 ? (tileX >> 5) & 1 : 0;
                    int screenY = (map & 2) != 0 ? (tileY >> 5) & 1 : 0;
                    int screen = screenX + screenY * ((map & 1) != 0 ? 2 : 1);
                    int address = ((map & 0xFC) * 512 + screen * 2048 + ((tileY & 31) * 32 + (tileX & 31)) * 2) & 65535;
                    int tile = Vram[address] | Vram[(address + 1) & 65535] << 8;
                    if (((tile >> 13) & 1) != priority) continue;
                    int px = sx & 7, py = sy & 7;
                    if ((tile & 0x4000) != 0) px = 7 - px;
                    if ((tile & 0x8000) != 0) py = 7 - py;
                    int baseAddress = ((Registers[0x0B + bg / 2] >> ((bg & 1) * 4)) & 15) * 8192;
                    int data = baseAddress + (tile & 1023) * bpp * 8 + py * 2;
                    int pixel = 0;
                    for (int plane = 0; plane < bpp; plane++)
                        pixel |= ((Vram[(data + plane / 2 * 16 + plane % 2) & 65535] >> (7 - px)) & 1) << plane;
                    if (pixel == 0) continue;
                    int palette = (tile >> 10) & 7;
                    color = Color((mode == 0 ? bg * 32 : 0) + palette * (1 << bpp) + pixel);
                }
            }
            Pixels[y * Width + x] = color;
        }
    }
}
