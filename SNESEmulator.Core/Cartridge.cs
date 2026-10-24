using System.Security.Cryptography;
using System.Text;

namespace SNESEmulator.Core;

public enum CartridgeMapping { LoRom, HiRom, ExLoRom, ExHiRom }
public sealed record CartridgeOptions(CartridgeMapping? Mapping = null, bool? Pal = null);
public sealed record CartridgeMetadata(int HeaderOffset, byte MapMode, byte Chipset, byte Region, int DeclaredRomBytes, bool ChecksumValid, bool CopierHeader, bool ManualMapping, bool ManualRegion, int[] HeaderScores);

public sealed class Cartridge
{
    public byte[] Rom { get; }
    public byte[] Sram { get; }
    public string Title { get; }
    public string Hash { get; }
    public string SourcePath { get; }
    public CartridgeMapping Mapping { get; }
    public bool Pal { get; }
    public CartridgeMetadata Metadata { get; }
    public CartridgeOptions Options { get; }
    public Dsp1Configuration? HardwareConfiguration {get;}
    public ICartridgeHardware? Hardware {get;}

    public Cartridge(byte[] image, string sourcePath = "", CartridgeOptions? options = null, Dsp1Configuration? hardware = null)
    {
        Options = options ?? new();
        bool copier = image.Length % 1024 == 512;
        if (copier) image = image[512..];
        if (image.Length < 32768 || image.Length > 8 * 1024 * 1024)
            throw new InvalidDataException("Supported normal mapper ROM size: 32 KiB to 8 MiB.");
        int[] headers = [0x7fc0, 0xffc0, 0x407fc0, 0x40ffc0];
        int[] scores = headers.Select(h => Score(image, h)).ToArray();
        int selected = Array.IndexOf(scores, scores.Max());
        Mapping = Options.Mapping ?? (CartridgeMapping)selected;
        if(!Enum.IsDefined(Mapping))throw new InvalidDataException("Unknown cartridge mapping override.");
        if(image.Length>0x400000&&Mapping is CartridgeMapping.LoRom or CartridgeMapping.HiRom)
            throw new NotSupportedException("LoROM/HiROM wire at most4 MiB; select a documented extended mapper for larger normal boards.");
        int header = headers[(int)Mapping];
        if (header + 64 > image.Length || (Options.Mapping == null && scores.Max() < 0))
            throw new InvalidDataException("No valid SNES header/reset vector found for selected mapping.");
        byte mode = image[header + 0x15], chipset = image[header + 0x16];
        if (chipset is not (0 or 1 or 2 or 3 or 5) || (Options.Mapping == null && (mode & 15) is not (0 or 1 or 2 or 5)))
            throw new NotSupportedException($"Cartridge hardware {mode:X2}/{chipset:X2} is not implemented (normal discrete mappers only).");
        if(chipset is 3 or 5)
        {
            HardwareConfiguration=hardware??Dsp1Configuration.Resolve(sourcePath);
            bool low=HardwareConfiguration.Board is Dsp1Board.LoRom1MiB or Dsp1Board.LoRom2MiB;
            int maximum=HardwareConfiguration.Board is Dsp1Board.LoRom1MiB or Dsp1Board.HiRom1MiB?0x100000:0x200000;
            if(Mapping!=(low?CartridgeMapping.LoRom:CartridgeMapping.HiRom)||image.Length>maximum)
                throw new InvalidDataException("DSP1 board ROM size/mapper does not match configuration.");
            Hardware=new Dsp1CartridgeHardware(HardwareConfiguration);
        }
        else if(hardware!=null)throw new InvalidDataException("DSP1 hardware supplied for a normal cartridge.");
        Rom = image.ToArray();
        Title = Encoding.ASCII.GetString(image, header, 21).Trim(' ', '\0');
        Hash = Convert.ToHexString(SHA256.HashData(image)); SourcePath = sourcePath;
        Pal = Options.Pal ?? (image[header + 0x19] is >= 2 and <= 12);
        int size = image[header + 0x18];
        if (size > 9) throw new NotSupportedException("SRAM size exceeds 512 KiB.");
        Sram = new byte[size == 0 ? 0 : 1024 << size];
        int checksum = image[header+0x1c] | image[header+0x1d]<<8;
        int complement = image[header+0x1e] | image[header+0x1f]<<8;
        int declared = image[header+0x17];
        Metadata = new(header,mode,chipset,image[header+0x19],declared<=20?1024<<declared:0,
            (checksum^complement)==65535,copier,Options.Mapping.HasValue,Options.Pal.HasValue,scores);
    }

    private static int Score(byte[] image, int h)
    {
        if (h + 64 > image.Length || (image[h + 0x3D] & 0x80) == 0) return -100;
        int checksum = image[h + 0x1C] | image[h + 0x1D] << 8;
        int complement = image[h + 0x1E] | image[h + 0x1F] << 8;
        int score = (checksum ^ complement) == 0xFFFF ? 8 : 0;
        score += (image[h + 0x15] & 15) == ((h & 0xffff) == 0x7FC0 ? (h > 0x400000 ? 2 : 0) : (h > 0x400000 ? 5 : 1)) ? 4 : 0;
        return score;
    }

    // Decode absent address lines recursively. A 3-MiB image mirrors its final
    // 1-MiB chip into 3..4 MiB; modulo would incorrectly map that window to chip 0.
    public static int Mirror(int address, int size)
    {
        if(size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        int baseOffset=0, mask=1<<23;
        while(address>=size) {
            while((address & mask)==0) mask>>=1;
            address-=mask;
            if(size>mask) {size-=mask;baseOffset+=mask;}
            mask>>=1;
        }
        return baseOffset+address;
    }
    public int RomOffset(int address)
    {
        int bank=(address>>16)&255, offset=address&65535;
        int raw=Mapping switch {
            CartridgeMapping.LoRom => ((bank&127)<<15)|(offset&32767),
            CartridgeMapping.HiRom => ((bank&63)<<16)|offset,
            CartridgeMapping.ExHiRom => (((bank&0x80)==0?0x400000:0)|((bank&63)<<16)|offset),
            // Extended LoROM: low-bank half at 4 MiB, high-bank half at 0 MiB.
            _ => (((bank&0x80)==0?0x400000:0)|((bank&127)<<15)|(offset&32767))
        };
        return Mirror(raw,Rom.Length);
    }
    public int SramOffset(int address)
    {
        if(Sram.Length==0)return -1;
        int bank=(address>>16)&255,offset=address&65535;
        if(Mapping is CartridgeMapping.LoRom or CartridgeMapping.ExLoRom && (bank&127) is >=0x70 and <=0x7f && offset<0x8000)
            return Mirror(((bank&15)<<15)|offset,Sram.Length);
        if(Mapping is CartridgeMapping.HiRom or CartridgeMapping.ExHiRom && (bank&127) is >=0x20 and <=0x3f && offset is >=0x6000 and <0x8000)
            return Mirror(((bank&31)<<13)|(offset&8191),Sram.Length);
        return -1;
    }
}
