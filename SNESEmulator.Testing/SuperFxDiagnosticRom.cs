using SNESEmulator.Core;
namespace SNESEmulator.Testing;

/// <summary>Authored CPU/GSU mailbox: execute from cache while S-CPU polls in WRAM.</summary>
public static class SuperFxDiagnosticRom
{
    public static byte[] Create(bool pal=false)
    {
        byte[] rom=DiagnosticRom.Create();rom[0x7fd6]=0x15;rom[0x7fd8]=7;rom[0x7fd9]=(byte)(pal?2:1);
        // GSU: IWT R0,$1234; IWT R1,$0020; STW(R1); INC R0;
        // IWT R2,$0022; STW(R2); STOP. Padding validates the prefetch line.
        byte[] gsu=[0xf0,0x34,0x12,0xf1,0x20,0,0x31,0xd0,0xf2,0x22,0,0x32,0,1,1,1];
        var cpu=new List<byte>();
        void Write(int address,int value){cpu.AddRange([0xa9,(byte)value,0x8d,(byte)address,(byte)(address>>8)]);}
        Write(0x3034,0);for(int i=0;i<gsu.Length;i++)Write(0x3100+i,gsu[i]);
        // Copy the small driver to WRAM before surrendering ROM. Its terminal
        // code is STP, so completion is proven by both mailboxes and stopped CPU.
        byte[] driver=[0xa9,0x18,0x8d,0x3a,0x30,0xa9,0,0x8d,0x1e,0x30,0x8d,0x1f,0x30,
            0xad,0x30,0x30,0x29,0x20,0xd0,0xf9,0xad,0x31,0x30,0x85,2,
            0xaf,0x20,0,0x70,0x85,0,0xaf,0x21,0,0x70,0x85,1,0xdb];
        for(int i=0;i<driver.Length;i++)Write(0x100+i,driver[i]);cpu.AddRange([0x4c,0,1]);cpu.ToArray().CopyTo(rom,0);return rom;
    }
}
