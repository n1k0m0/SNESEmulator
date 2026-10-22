using System.Security.Cryptography;
using System.Text;

namespace SNESEmulator.Core;

public enum CartridgeMapping { LoRom, HiRom }

public sealed class Cartridge
{
    public byte[] Rom { get; }
    public byte[] Sram { get; }
    public string Title { get; }
    public string Hash { get; }
    public string SourcePath { get; }
    public CartridgeMapping Mapping { get; }
    public bool Pal { get; }

    public Cartridge(byte[] image, string sourcePath = "")
    {
        if (image.Length % 1024 == 512) image = image[512..];
        if (image.Length < 32768 || image.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Supported ROM size: 32 KiB to 4 MiB (LoROM/HiROM).");
        int lo = Score(image, 0x7FC0), hi = Score(image, 0xFFC0);
        Mapping = hi > lo ? CartridgeMapping.HiRom : CartridgeMapping.LoRom;
        int header = Mapping == CartridgeMapping.LoRom ? 0x7FC0 : 0xFFC0;
        if (Math.Max(lo, hi) < 0) throw new InvalidDataException("No valid SNES header/reset vector found.");
        byte mode = image[header + 0x15];
        byte chipset = image[header + 0x16];
        if ((mode & 15) > 1 || chipset is not (0 or 1 or 2))
            throw new NotSupportedException($"Cartridge hardware {mode:X2}/{chipset:X2} is not implemented (no enhancement chips yet).");
        Rom = image.ToArray();
        Title = Encoding.ASCII.GetString(image, header, 21).Trim(' ', '\0');
        Hash = Convert.ToHexString(SHA256.HashData(image));
        SourcePath = sourcePath;
        Pal = image[header + 0x19] is >= 2 and <= 12;
        int size = image[header + 0x18];
        if (size > 9) throw new NotSupportedException("SRAM size exceeds 512 KiB.");
        Sram = new byte[size == 0 ? 0 : 1024 << size];
    }

    private static int Score(byte[] image, int h)
    {
        if (h + 64 > image.Length || (image[h + 0x3D] & 0x80) == 0) return -100;
        int checksum = image[h + 0x1C] | image[h + 0x1D] << 8;
        int complement = image[h + 0x1E] | image[h + 0x1F] << 8;
        int score = (checksum ^ complement) == 0xFFFF ? 8 : 0;
        score += (image[h + 0x15] & 15) == (h == 0x7FC0 ? 0 : 1) ? 4 : 0;
        return score;
    }

    public int RomOffset(int address)
    {
        int bank = address >> 16, offset = address & 0xFFFF;
        if (Mapping == CartridgeMapping.LoRom)
            return ((bank & 0x7F) * 0x8000 + (offset & 0x7FFF)) % Rom.Length;
        return (((bank & 0x3F) << 16) | offset) % Rom.Length;
    }

    public int SramOffset(int address)
    {
        if (Sram.Length == 0) return -1;
        int bank = address >> 16, offset = address & 0xFFFF;
        if (Mapping == CartridgeMapping.LoRom && (bank & 0x7F) is >= 0x70 and <= 0x7D && offset < 0x8000)
            return (((bank & 15) << 15) | offset) % Sram.Length;
        if (Mapping == CartridgeMapping.HiRom && (bank & 0x7F) is >= 0x20 and <= 0x3F && offset is >= 0x6000 and < 0x8000)
            return (((bank & 0x1F) << 13) | (offset & 0x1FFF)) % Sram.Length;
        return -1;
    }
}
