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
    public ushort[] Buttons { get; } = new ushort[4];
    public ushort[] Latched { get; } = new ushort[4];
    // Optional fixture/peripheral electrical data-2 lines; standard pads leave them low.
    public bool[] AuxiliaryConnected {get;} = new bool[2];
    public int[] Position { get; } = new int[2];
    public bool Strobe { get; private set; }
    public void Write(byte value)
    {
        bool next = (value & 1) != 0;
        if (Strobe && !next) Latch();
        if(Strobe!=next)Array.Clear(Position);
        Strobe = next;
    }
    public void Latch() { for(int i=0;i<4;i++)Latched[i]=(ushort)(Buttons[i]&0xfff0); Array.Clear(Position); }
    public byte Read(int port)
    {
        if (Strobe) return (byte)(((Buttons[port] >> 15)&1)|(AuxiliaryConnected[port]?((Buttons[port+2] >> 15)&1)<<1:0));
        int bit = Position[port];
        if (!Strobe && bit < 16) Position[port]++;
        return (byte)(bit >= 16 ? (AuxiliaryConnected[port]?3:1) : ((Latched[port] >> (15 - bit)) & 1) | (AuxiliaryConnected[port]?((Latched[port+2] >> (15 - bit)) & 1) << 1:0));
    }
    public void Restore(bool strobe) => Strobe = strobe;
}
