namespace SNESEmulator.Core;

// Bits match JOY1/JOY2: B,Y,Select,Start,Up,Down,Left,Right,A,X,L,R.
[Flags]
public enum SnesButtons : ushort
{
    None = 0, R = 0x10, L = 0x20, X = 0x40, A = 0x80,
    Right = 0x100, Left = 0x200, Down = 0x400, Up = 0x800,
    Start = 0x1000, Select = 0x2000, Y = 0x4000, B = 0x8000
}

public sealed class ControllerPorts
{
    public ushort[] Buttons { get; } = new ushort[2];
    public ushort[] Latched { get; } = new ushort[2];
    public int[] Position { get; } = new int[2];
    public bool Strobe { get; private set; }
    public void Write(byte value)
    {
        Strobe = (value & 1) != 0;
        if (Strobe) Latch();
    }
    public void Latch() { Array.Copy(Buttons, Latched, 2); Array.Clear(Position); }
    public byte Read(int port)
    {
        if (Strobe) Latch();
        int bit = Position[port];
        if (!Strobe && bit < 16) Position[port]++;
        return (byte)(bit >= 16 ? 1 : (Latched[port] >> (15 - bit)) & 1);
    }
    public void Restore(bool strobe) => Strobe = strobe;
}
