using System.IO.Compression;
using System.Text.Json;
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
        using (var file = File.Create(temporary)) Write(machine,file);
        File.Move(temporary, path, true);
    }
    // The host captures uncompressed hardware bytes on its owner thread, then
    // compresses/writes those owned bytes outside the emulation loop.
    public static byte[] Capture(SnesSystem machine)
    {
        using var stream=new MemoryStream();Write(machine,stream,false);return stream.ToArray();
    }
    public static void SaveCaptured(byte[] bytes,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary=path+".tmp";
        using(var file=File.Create(temporary))
        using(var zip=new GZipStream(file,CompressionLevel.Fastest))zip.Write(bytes);
        File.Move(temporary,path,true);
    }
    private static void Write(SnesSystem machine,Stream destination,bool compress=true)
    {
        var cart=machine.Bus.Cartridge??throw new InvalidOperationException("Load a cartridge first.");
        using var zip=compress?new GZipStream(destination,CompressionLevel.Fastest,true):null;
        // Preserve the original compressed byte stream: closing BinaryWriter
        // must close GZip directly, rather than issuing a leave-open SyncFlush.
        using (var w = new BinaryWriter(zip??destination,System.Text.Encoding.UTF8,!compress))
        {
            w.Write("SNESSAV"); w.Write(9); w.Write(cart.Hash); w.Write(cart.Sram.Length);w.Write(JsonSerializer.Serialize(machine.Profile));w.Write(machine.ProfileForced);w.Write((int)cart.Mapping);w.Write(JsonSerializer.Serialize(cart.Options));
            var c = machine.Cpu; var b = machine.Bus; var p = b.Ppu;
            w.Write(c.A); w.Write(c.X); w.Write(c.Y); w.Write(c.S); w.Write(c.D); w.Write(c.PC);
            w.Write(c.P); w.Write(c.PBR); w.Write(c.DBR); w.Write(c.E); w.Write(c.Waiting); w.Write(c.Stopped); w.Write(c.Instructions);
            w.Write(machine.Frames); w.Write(machine.MasterRemainder);
            w.Write(b.Wram); w.Write(b.Io); w.Write(b.DmaRegisters); w.Write(cart.Sram);
            w.Write(b.OpenBus); w.Write(b.WramAddress); w.Write(b.NmiFlag); w.Write(b.NmiPending);
            w.Write(p.Vram); w.Write(p.Cgram); w.Write(p.Oam); w.Write(p.Registers);
            w.Write(p.VramAddress); w.Write(p.CgramAddress); w.Write(p.OamAddress); w.Write(p.ScrollLatch); w.Write(p.Scanline);
            foreach (ushort value in p.Scroll) w.Write(value);
            w.Write(JsonSerializer.Serialize(p.FrameGeometry));w.Write(p.Pixels.Length);
            foreach (uint pixel in p.Pixels) w.Write(pixel);
            foreach(ushort pixel in p.NativePixels)w.Write(pixel);w.Write(p.Brightness);w.Write(JsonSerializer.Serialize(p.CapturePipeline()));
            w.Write(JsonSerializer.Serialize(b.Apu.Capture()));
            foreach (ushort value in b.Controllers.Buttons) w.Write(value);
            foreach (ushort value in b.Controllers.Latched) w.Write(value);
            foreach (int value in b.Controllers.Position) w.Write(value);
            w.Write(b.Controllers.Strobe);foreach(bool value in b.Controllers.AuxiliaryConnected)w.Write(value);
            w.Write(JsonSerializer.Serialize(machine.Clock.Capture()));w.Write(JsonSerializer.Serialize(machine.CapturePhase()));
            w.Write(JsonSerializer.Serialize(b.CpuIo.State));w.Write(JsonSerializer.Serialize(b.Dma.State));
            w.Write(JsonSerializer.Serialize(c.CaptureContinuation()));w.Write(JsonSerializer.Serialize(c.CaptureInterrupts()));
        }
    }
    public static SnesSystem Load(Cartridge cartridge, string path)
    {
        using var file = File.OpenRead(path);
        return Load(cartridge,file);
    }
    public static SnesSystem Load(Cartridge cartridge,Stream source)
    {
        using var zip = new GZipStream(source, CompressionMode.Decompress,true);
        using var r = new BinaryReader(zip);
        if (r.ReadString() != "SNESSAV" || r.ReadInt32() != 9 || r.ReadString() != cartridge.Hash || r.ReadInt32() != cartridge.Sram.Length)
            throw new InvalidDataException("Savestate requires format9 (continuous WAI phase, pending PPU bus endpoints and field-start display latches). Formats1-8 lack an unambiguous corrected timing state; start the ROM again. The cartridge must also match.");
        var profile=JsonSerializer.Deserialize<HardwareProfile>(r.ReadString())??throw new InvalidDataException("Missing hardware profile");
        bool forced=r.ReadBoolean();var mapping=(CartridgeMapping)r.ReadInt32();
        var options=JsonSerializer.Deserialize<CartridgeOptions>(r.ReadString())??throw new InvalidDataException("Missing cartridge options");
        if(!Enum.IsDefined(mapping)||mapping!=cartridge.Mapping)throw new InvalidDataException("Savestate cartridge mapper does not match.");
        var restoredCart=new Cartridge(cartridge.Rom,cartridge.SourcePath,options);
        if(restoredCart.Mapping!=mapping)throw new InvalidDataException("Invalid saved mapper options");
        var m = new SnesSystem(profile,forced);m.Insert(restoredCart);
        var c = m.Cpu; var b = m.Bus; var p = b.Ppu;
        c.A = r.ReadUInt16(); c.X = r.ReadUInt16(); c.Y = r.ReadUInt16(); c.S = r.ReadUInt16(); c.D = r.ReadUInt16(); c.PC = r.ReadUInt16();
        c.P = r.ReadByte(); c.PBR = r.ReadByte(); c.DBR = r.ReadByte(); c.E = r.ReadBoolean(); c.Waiting = r.ReadBoolean(); c.Stopped = r.ReadBoolean(); c.Instructions = r.ReadInt64();
        long frames=r.ReadInt64();int remainder=r.ReadInt32();
        Read(r, b.Wram); Read(r, b.Io); Read(r, b.DmaRegisters); Read(r, b.Cartridge!.Sram);
        b.OpenBus = r.ReadByte(); b.WramAddress = r.ReadInt32(); b.NmiFlag = r.ReadBoolean(); b.NmiPending = r.ReadBoolean();
        Read(r, p.Vram); Read(r, p.Cgram); Read(r, p.Oam); Read(r, p.Registers);
        p.VramAddress = r.ReadUInt16(); p.CgramAddress = r.ReadUInt16(); p.OamAddress = r.ReadUInt16(); p.ScrollLatch = r.ReadByte(); p.Scanline = r.ReadInt32();
        for (int i = 0; i < p.Scroll.Length; i++) p.Scroll[i] = r.ReadUInt16();
        p.RestoreGeometry(JsonSerializer.Deserialize<PpuGeometry>(r.ReadString())??throw new InvalidDataException("Missing video geometry"));
        if(r.ReadInt32()!=p.Pixels.Length)throw new InvalidDataException("Invalid video array length");
        for (int i = 0; i < p.Pixels.Length; i++) p.Pixels[i] = r.ReadUInt32();
        for(int i=0;i<p.NativePixels.Length;i++)p.NativePixels[i]=r.ReadUInt16();Read(r,p.Brightness);p.RestorePipeline(JsonSerializer.Deserialize<PpuPipelineState>(r.ReadString())??throw new InvalidDataException("Missing PPU pipeline"));
        b.Apu.Restore(JsonSerializer.Deserialize<ApuState>(r.ReadString())??throw new InvalidDataException("Missing APU state"));
        for (int i = 0; i < 4; i++) b.Controllers.Buttons[i] = r.ReadUInt16();
        for (int i = 0; i < 4; i++) b.Controllers.Latched[i] = r.ReadUInt16();
        for (int i = 0; i < 2; i++) b.Controllers.Position[i] = r.ReadInt32();
        b.Controllers.Restore(r.ReadBoolean());for(int i=0;i<2;i++)b.Controllers.AuxiliaryConnected[i]=r.ReadBoolean();
        m.Clock.Restore(JsonSerializer.Deserialize<ClockState>(r.ReadString())??throw new InvalidDataException("Missing clock state"));
        m.RestorePhase(JsonSerializer.Deserialize<ExecutionPhase>(r.ReadString())??throw new InvalidDataException("Missing CPU phase"));
        b.CpuIo.Restore(JsonSerializer.Deserialize<IoState>(r.ReadString())??throw new InvalidDataException("Missing I/O state"));
        b.Dma.Restore(JsonSerializer.Deserialize<DmaState>(r.ReadString())??throw new InvalidDataException("Missing DMA state"));
        c.RestoreContinuation(JsonSerializer.Deserialize<CpuContinuation>(r.ReadString()));
        c.RestoreInterrupts(JsonSerializer.Deserialize<CpuInterruptState>(r.ReadString())??throw new InvalidDataException("Missing interrupt state"));
        p.ValidateClock();
        if(frames!=m.Frames||remainder!=m.MasterRemainder)throw new InvalidDataException("Inconsistent clock state");
        if (b.WramAddress is < 0 or > 0x1FFFF || p.Scanline is < 0 or > 312 || p.CgramAddress>511 || p.OamAddress>1023 || p.Brightness.Any(x=>x>15) || p.NativePixels.Any(x=>x>32767) ||
            b.Controllers.Position.Any(v => v is < 0 or > 16) || m.MasterRemainder is < 0 or > 600000 || zip.ReadByte() != -1)
            throw new InvalidDataException("Invalid savestate values.");
        return m;
    }
    private static void Read(BinaryReader reader, byte[] bytes) => reader.BaseStream.ReadExactly(bytes);
}
