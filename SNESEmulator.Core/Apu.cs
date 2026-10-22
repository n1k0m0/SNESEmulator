namespace SNESEmulator.Core;

// Deliberately no fake SPC700 boot acknowledgement: unsupported software must
// remain diagnosable rather than appear to execute a sound program that never ran.
public sealed class Apu
{
    public byte[] CpuToApu { get; } = new byte[4];
    public byte[] ApuToCpu { get; } = new byte[4];
    public const bool IsImplemented = false;
    public void Reset() { Array.Clear(CpuToApu); Array.Clear(ApuToCpu); }
}
