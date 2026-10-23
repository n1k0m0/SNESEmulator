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
        AtomicFile.Write(path,stream=>Write(machine,stream),candidate=>Valid(cart,candidate));
    }
    private static bool Valid(Cartridge cartridge,string path)
    {
        try{using var file=File.OpenRead(path);_ = Load(cartridge,file);return true;}
        catch(Exception e) when(e is IOException or InvalidDataException or JsonException or ArgumentException or OverflowException or InvalidOperationException){return false;}
    }
    // Format9: <=512 KiB SRAM, <=512*239 pixels (7 bytes/pixel), fixed
    // RAM, plus bounded JSON pipelines. 32 MiB leaves >10x normal-state headroom.
    // Increase these explicit versioned budgets when adding hardware, never from input.
    public const int MaximumCompressedBytes=16*1024*1024, MaximumRawBytes=32*1024*1024, MaximumStringBytes=8*1024*1024;
    private static readonly JsonSerializerOptions JsonOptions=new(){MaxDepth=32};
    // The host captures uncompressed hardware bytes on its owner thread, then
    // compresses/writes those owned bytes outside the emulation loop.
    public static byte[] Capture(SnesSystem machine)
    {
        using var stream=new MemoryStream();Write(machine,stream,false);return stream.ToArray();
    }
    public static void SaveCaptured(byte[] bytes,string path,Cartridge cartridge)
    {
        ArgumentNullException.ThrowIfNull(cartridge);
        if(bytes.Length>MaximumRawBytes)throw new InvalidDataException("Captured state exceeds format9 size limit.");
        AtomicFile.Write(path,file=>{using var zip=new GZipStream(file,CompressionLevel.Fastest,true);zip.Write(bytes);},candidate=>Valid(cartridge,candidate));
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
        using var source=File.OpenRead(path);return Load(cartridge,source);
    }
    public static byte[] ReadForLoad(Cartridge cartridge,string path,out bool recovered)
        =>AtomicFile.Read(path,MaximumCompressedBytes,bytes=>{
            try{using var stream=new MemoryStream(bytes,false);_ = Load(cartridge,stream);return true;}
            catch(Exception e) when(e is IOException or InvalidDataException){return false;}
        },out recovered);
    private static readonly uint[] CrcTable=BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var table=new uint[256];for(uint i=0;i<256;i++){uint value=i;for(int bit=0;bit<8;bit++)value=(value&1)!=0?0xedb88320^(value>>1):value>>1;table[i]=value;}return table;
    }
    public static byte[] Decode(Stream source)
    {
        using var compressed=new MemoryStream();byte[] block=new byte[8192];int count;
        while((count=source.Read(block))!=0){if(compressed.Length+count>MaximumCompressedBytes)throw new InvalidDataException("Compressed savestate exceeds 16 MiB limit.");compressed.Write(block,0,count);}
        if(compressed.Length<18)throw new InvalidDataException("Truncated gzip savestate.");
        byte[] encoded=compressed.ToArray();
        compressed.Position=0;using var zip=new GZipStream(compressed,CompressionMode.Decompress);using var raw=new MemoryStream();
        while((count=zip.Read(block))!=0){if(raw.Length+count>MaximumRawBytes)throw new InvalidDataException("Decompressed savestate exceeds 32 MiB limit.");raw.Write(block,0,count);}
        var decoded=raw.ToArray();uint crc=uint.MaxValue;
        foreach(byte value in decoded)crc=CrcTable[(crc^value)&255]^(crc>>8);
        if(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(encoded.Length-8))!=~crc||System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(encoded.Length-4))!=(uint)decoded.Length)
            throw new InvalidDataException("Truncated or corrupt gzip savestate checksum/length.");
        return decoded;
    }
    private sealed class LimitedReader(Stream stream):BinaryReader(stream)
    {
        public override string ReadString()
        {
            int length=Read7BitEncodedInt();if(length<0||length>MaximumStringBytes||length>BaseStream.Length-BaseStream.Position)throw new InvalidDataException("Invalid or oversized savestate string.");
            return new System.Text.UTF8Encoding(false,true).GetString(ReadBytes(length));
        }
    }
    public static SnesSystem Load(Cartridge cartridge,Stream source)
    {
        try{return LoadCore(cartridge,source);}
        catch(Exception e) when(e is JsonException or ArgumentException or OverflowException or InvalidOperationException or NullReferenceException or IndexOutOfRangeException or FormatException)
        {throw new InvalidDataException("Invalid savestate contents: "+e.Message,e);}
    }
    private static T? Json<T>(string text)
    {
        using var document=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=32});
        int nodes=0;
        void Inspect(JsonElement element)
        {
            if(++nodes>1000000)throw new InvalidDataException("Savestate JSON node budget exceeded.");
            if(element.ValueKind==JsonValueKind.Array)
            {
                if(element.GetArrayLength()>512*239)throw new InvalidDataException("Savestate JSON array exceeds native field capacity.");
                foreach(var item in element.EnumerateArray())Inspect(item);
            }
            else if(element.ValueKind==JsonValueKind.Object)foreach(var property in element.EnumerateObject())Inspect(property.Value);
        }
        Inspect(document.RootElement);return document.RootElement.Deserialize<T>(JsonOptions);
    }
    private static SnesSystem LoadCore(Cartridge cartridge,Stream source)
    {
        using var raw=new MemoryStream(Decode(source),false);
        using var r = new LimitedReader(raw);
        if (r.ReadString() != "SNESSAV" || r.ReadInt32() != 9 || r.ReadString() != cartridge.Hash || r.ReadInt32() != cartridge.Sram.Length)
            throw new InvalidDataException("Savestate requires format9 (continuous WAI phase, pending PPU bus endpoints and field-start display latches). Formats1-8 lack an unambiguous corrected timing state; start the ROM again. The cartridge must also match.");
        var profile=Json<HardwareProfile>(r.ReadString())??throw new InvalidDataException("Missing hardware profile");
        bool forced=r.ReadBoolean();var mapping=(CartridgeMapping)r.ReadInt32();
        var options=Json<CartridgeOptions>(r.ReadString())??throw new InvalidDataException("Missing cartridge options");
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
        p.RestoreGeometry(Json<PpuGeometry>(r.ReadString())??throw new InvalidDataException("Missing video geometry"));
        if(r.ReadInt32()!=p.Pixels.Length)throw new InvalidDataException("Invalid video array length");
        for (int i = 0; i < p.Pixels.Length; i++) p.Pixels[i] = r.ReadUInt32();
        for(int i=0;i<p.NativePixels.Length;i++)p.NativePixels[i]=r.ReadUInt16();Read(r,p.Brightness);p.RestorePipeline(Json<PpuPipelineState>(r.ReadString())??throw new InvalidDataException("Missing PPU pipeline"));
        b.Apu.Restore(Json<ApuState>(r.ReadString())??throw new InvalidDataException("Missing APU state"));
        for (int i = 0; i < 4; i++) b.Controllers.Buttons[i] = r.ReadUInt16();
        for (int i = 0; i < 4; i++) b.Controllers.Latched[i] = r.ReadUInt16();
        for (int i = 0; i < 2; i++) b.Controllers.Position[i] = r.ReadInt32();
        b.Controllers.Restore(r.ReadBoolean());for(int i=0;i<2;i++)b.Controllers.AuxiliaryConnected[i]=r.ReadBoolean();
        m.Clock.Restore(Json<ClockState>(r.ReadString())??throw new InvalidDataException("Missing clock state"));
        m.RestorePhase(Json<ExecutionPhase>(r.ReadString())??throw new InvalidDataException("Missing CPU phase"));
        b.CpuIo.Restore(Json<IoState>(r.ReadString())??throw new InvalidDataException("Missing I/O state"));
        b.Dma.Restore(Json<DmaState>(r.ReadString())??throw new InvalidDataException("Missing DMA state"));
        c.RestoreContinuation(Json<CpuContinuation>(r.ReadString()));
        c.RestoreInterrupts(Json<CpuInterruptState>(r.ReadString())??throw new InvalidDataException("Missing interrupt state"));
        p.ValidateClock();
        if(frames!=m.Frames||remainder!=m.MasterRemainder)throw new InvalidDataException("Inconsistent clock state");
        if (b.WramAddress is < 0 or > 0x1FFFF || p.Scanline is < 0 or > 312 || p.CgramAddress>511 || p.OamAddress>1023 || p.Brightness.Any(x=>x>15) || p.NativePixels.Any(x=>x>32767) ||
            b.Controllers.Position.Any(v => v is < 0 or > 16) || m.MasterRemainder is < 0 or > 600000 || raw.ReadByte() != -1)
            throw new InvalidDataException("Invalid savestate values.");
        return m;
    }
    private static void Read(BinaryReader reader, byte[] bytes) => reader.BaseStream.ReadExactly(bytes);
}
