using System.Text;

namespace SNESEmulator.Core;

/// <summary>Original, freely redistributable LoROM exercising CPU, DMA, VRAM and CGRAM.</summary>
public static class DiagnosticRom
{
    public static byte[] Create()
    {
        byte[] rom = new byte[32768]; Array.Fill(rom, (byte)0xEA);
        var code = new List<byte>();
        void Emit(params byte[] bytes) => code.AddRange(bytes);
        void Store(byte value, ushort address) => Emit(0xA9, value, 0x8D, (byte)address, (byte)(address >> 8));
        Emit(0x78, 0x18, 0xFB, 0xC2, 0x10, 0xA2, 0xFF, 0x1F, 0x9A); // Native mode, 16-bit X, stack.
        Store(0x80, 0x2100); Store(0, 0x2105); Store(4, 0x2107); // Mode 0, map at byte $0800.
        Store(0, 0x210B); Store(1, 0x212C);
        Store(0, 0x2121);
        foreach (byte b in new byte[] { 0x00,0x00, 0x1F,0x00, 0xE0,0x03, 0x00,0x7C }) Store(b, 0x2122);
        Store(0x80, 0x2115); Store(0, 0x2116); Store(0, 0x2117);
        // DMA a two-plane 8x8 tile from cartridge offset $1000 to VRAM.
        Store(1, 0x4300); Store(0x18, 0x4301); Store(0, 0x4302); Store(0x90, 0x4303);
        Store(0, 0x4304); Store(16, 0x4305); Store(0, 0x4306); Store(1, 0x420B);
        Store(0x0F, 0x2100); Store(0x81, 0x4200); // Brightness, NMI, auto joypad.
        int loop = code.Count;
        Emit(0xCB, 0x80, 0xFD); // WAI/BRA, resumed by NMI.
        int nmi = code.Count;
        Emit(0x48); // PHA; animate BG1 and let the controller change its direction/speed.
        Emit(0xEE, 0x00, 0x00); // INC scroll
        Emit(0xAD, 0x19, 0x42, 0x29, 0x02, 0xF0, 0x06); // JOY1 left?
        Emit(0xCE, 0x00, 0x00, 0xCE, 0x00, 0x00); // Two decrements reverse the automatic motion.
        Emit(0xAD, 0x19, 0x42, 0x29, 0x01, 0xF0, 0x03); // JOY1 right?
        Emit(0xEE, 0x00, 0x00);
        Emit(0xAD, 0x00, 0x00, 0x8D, 0x0D, 0x21);
        Store(0, 0x210D);
        Emit(0x68, 0x40);
        code.CopyTo(rom);
        for (int y = 0; y < 8; y++) { rom[0x1000+y*2] = (byte)(y % 2 == 0 ? 0xAA : 0x55); rom[0x1001+y*2] = (byte)(y < 4 ? 0xF0 : 0x0F); }
        Encoding.ASCII.GetBytes("SNES CSHARP DIAGNOSTIC").CopyTo(rom, 0x7FC0);
        rom[0x7FD5] = 0x20; rom[0x7FD6] = 0; rom[0x7FD7] = 5; rom[0x7FD8] = 0; rom[0x7FD9] = 1;
        rom[0x7FDC] = rom[0x7FDD] = 0xFF; rom[0x7FDE] = rom[0x7FDF] = 0;
        void Vector(int offset, int value) { rom[offset] = (byte)value; rom[offset+1] = (byte)(value >> 8); }
        Vector(0x7FEA, 0x8000+nmi); Vector(0x7FFA, 0x8000+nmi); Vector(0x7FFC, 0x8000); Vector(0x7FFE, 0x8000+loop);
        return rom;
    }
}
