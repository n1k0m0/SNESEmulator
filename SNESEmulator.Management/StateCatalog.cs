using SNESEmulator.Core;
using SNESEmulator.Contracts;
using SNESEmulator.SaveStates;

namespace SNESEmulator.Management;
public static class StateCatalog
{
    public static string[] List(string directory) => !Directory.Exists(directory) ? [] : BoundedFiles.Walk(directory).Where(p => p.EndsWith(".snessav", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".snessav.bak", StringComparison.OrdinalIgnoreCase)).Select(p => p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p).Distinct(StringComparer.OrdinalIgnoreCase).Take(1000).ToArray();
    public static string ReadIdentity(string path)
    {
        byte[] encoded = AtomicFile.ReadBytes(path, StateSerializer.MaximumCompressedBytes);
        using var input = new MemoryStream(encoded, false);
        byte[] raw = StateSerializer.Decode(input);
        using var reader = new BinaryReader(new MemoryStream(raw, false));
        if (reader.ReadByte() != 7 || System.Text.Encoding.ASCII.GetString(reader.ReadBytes(7)) != "SNESSAV" || reader.ReadInt32() is not (9 or 10) || reader.ReadByte() != 64)
            throw new InvalidDataException("Unsupported state identity");
        string hash = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(64));
        if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Invalid state hash");
        return hash;
    }

    public static StateInfo Preview(string path, Cartridge cart, bool recover = false)
    {
        try
        {
            byte[] bytes = recover ? StateSerializer.ReadForLoad(cart, path, out _) : AtomicFile.ReadBytes(path, StateSerializer.MaximumCompressedBytes);
            using var input = new MemoryStream(bytes, false);
            var state = StateSerializer.Load(cart, input);
            var frame = state.Bus.Ppu.CompletedFrame ?? state.Bus.Ppu.Frame;
            return new(path, Path.GetFileNameWithoutExtension(path), cart.Hash, cart.Hardware==null?9:10, frame.Geometry.Width, frame.Geometry.Height, frame.Argb.ToArray(), null, File.Exists(path + ".bak"));
        }
        catch (Exception e)when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new(path, Path.GetFileNameWithoutExtension(path), cart.Hash, cart.Hardware==null?9:10, 0, 0, null, e.Message, File.Exists(path + ".bak"));
        }
    }

    public static void Import(string source, string destination, Cartridge cart)
    {
        byte[] bytes = AtomicFile.ReadBytes(source, StateSerializer.MaximumCompressedBytes);
        using (var input = new MemoryStream(bytes, false))
            _ = StateSerializer.Load(cart, input);
        AtomicFile.Write(destination, s => s.Write(bytes), p => Preview(p, cart).Error == null);
    }

    public static void Recover(string path, Cartridge cart)
    {
        byte[] bytes = StateSerializer.ReadForLoad(cart, path, out bool recovered);
        if (!recovered)
            throw new InvalidOperationException("Primary is valid; no recovery required");
        AtomicFile.Write(path, s => s.Write(bytes), p => Preview(p, cart).Error == null);
    }
}
