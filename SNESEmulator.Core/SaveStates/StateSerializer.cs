using System.IO.Compression;
using SNESEmulator.Core;

namespace SNESEmulator.SaveStates;

public static class StateSerializer
{
    // Includes all state currently implemented. Validate into a temporary machine
    // before replacing the running instance; rejected/corrupt files leave it intact.
    public static void Save(SnesSystem machine, string path)
    {
        var cart = machine.Bus.Cartridge ?? throw new InvalidOperationException("Load a cartridge first.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        using (var file = File.Create(temporary))
        using (var zip = new GZipStream(file, CompressionLevel.Fastest))
        using (var w = new BinaryWriter(zip))
        {
            w.Write("SNESSAV"); w.Write(1); w.Write(cart.Hash); w.Write(cart.Sram.Length);
            var c = machine.Cpu; var b = machine.Bus; var p = b.Ppu;
            w.Write(c.A); w.Write(c.X); w.Write(c.Y); w.Write(c.S); w.Write(c.D); w.Write(c.PC);
            w.Write(c.P); w.Write(c.PBR); w.Write(c.DBR); w.Write(c.E); w.Write(c.Waiting); w.Write(c.Stopped); w.Write(c.Instructions);
            w.Write(machine.Frames); w.Write(machine.MasterRemainder);
            w.Write(b.Wram); w.Write(b.Io); w.Write(b.DmaRegisters); w.Write(cart.Sram);
            w.Write(b.OpenBus); w.Write(b.WramAddress); w.Write(b.NmiFlag); w.Write(b.NmiPending); w.Write(b.DmaMasterCycles);
            w.Write(p.Vram); w.Write(p.Cgram); w.Write(p.Oam); w.Write(p.Registers);
            w.Write(p.VramAddress); w.Write(p.CgramAddress); w.Write(p.OamAddress); w.Write(p.ScrollLatch); w.Write(p.Scanline);
            foreach (ushort value in p.Scroll) w.Write(value);
            foreach (uint pixel in p.Pixels) w.Write(pixel);
            w.Write(b.Apu.CpuToApu); w.Write(b.Apu.ApuToCpu);
            foreach (ushort value in b.Controllers.Buttons) w.Write(value);
            foreach (ushort value in b.Controllers.Latched) w.Write(value);
            foreach (int value in b.Controllers.Position) w.Write(value);
            w.Write(b.Controllers.Strobe);
        }
        File.Move(temporary, path, true);
    }
    public static SnesSystem Load(Cartridge cartridge, string path)
    {
        using var file = File.OpenRead(path);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var r = new BinaryReader(zip);
        if (r.ReadString() != "SNESSAV" || r.ReadInt32() != 1 || r.ReadString() != cartridge.Hash || r.ReadInt32() != cartridge.Sram.Length)
            throw new InvalidDataException("Savestate version or cartridge does not match.");
        var m = new SnesSystem(); m.Insert(new Cartridge(cartridge.Rom, cartridge.SourcePath));
        var c = m.Cpu; var b = m.Bus; var p = b.Ppu;
        c.A = r.ReadUInt16(); c.X = r.ReadUInt16(); c.Y = r.ReadUInt16(); c.S = r.ReadUInt16(); c.D = r.ReadUInt16(); c.PC = r.ReadUInt16();
        c.P = r.ReadByte(); c.PBR = r.ReadByte(); c.DBR = r.ReadByte(); c.E = r.ReadBoolean(); c.Waiting = r.ReadBoolean(); c.Stopped = r.ReadBoolean(); c.Instructions = r.ReadInt64();
        m.Frames = r.ReadInt64(); m.MasterRemainder = r.ReadInt32();
        Read(r, b.Wram); Read(r, b.Io); Read(r, b.DmaRegisters); Read(r, b.Cartridge!.Sram);
        b.OpenBus = r.ReadByte(); b.WramAddress = r.ReadInt32(); b.NmiFlag = r.ReadBoolean(); b.NmiPending = r.ReadBoolean(); b.DmaMasterCycles = r.ReadInt32();
        Read(r, p.Vram); Read(r, p.Cgram); Read(r, p.Oam); Read(r, p.Registers);
        p.VramAddress = r.ReadUInt16(); p.CgramAddress = r.ReadUInt16(); p.OamAddress = r.ReadUInt16(); p.ScrollLatch = r.ReadByte(); p.Scanline = r.ReadInt32();
        for (int i = 0; i < p.Scroll.Length; i++) p.Scroll[i] = r.ReadUInt16();
        for (int i = 0; i < p.Pixels.Length; i++) p.Pixels[i] = r.ReadUInt32();
        Read(r, b.Apu.CpuToApu); Read(r, b.Apu.ApuToCpu);
        for (int i = 0; i < 2; i++) b.Controllers.Buttons[i] = r.ReadUInt16();
        for (int i = 0; i < 2; i++) b.Controllers.Latched[i] = r.ReadUInt16();
        for (int i = 0; i < 2; i++) b.Controllers.Position[i] = r.ReadInt32();
        b.Controllers.Restore(r.ReadBoolean());
        if (b.WramAddress is < 0 or > 0x1FFFF || p.Scanline is < 0 or > 311 ||
            b.Controllers.Position.Any(v => v is < 0 or > 16) || m.MasterRemainder is < 0 or > 600000 || zip.ReadByte() != -1)
            throw new InvalidDataException("Invalid savestate values.");
        return m;
    }
    private static void Read(BinaryReader reader, byte[] bytes) => reader.BaseStream.ReadExactly(bytes);
}
