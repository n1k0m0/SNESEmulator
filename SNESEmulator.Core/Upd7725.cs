namespace SNESEmulator.Core;

// Independently authored instruction engine. Encoding/ordering was checked
// against the pinned uPD7725 software reference described in docs/m10a-dsp1.md.
// Serial pins and external INT are not connected on the supported DSP1 boards.
public sealed class Upd7725
{
    private readonly uint[] program;
    private readonly ushort[] data;
    // A,B,TR,DP,RP,DR,SO,K,L,M,N,TRB,SI. Arrays keep the state schema explicit.
    private readonly ushort[] r = new ushort[13], ram = new ushort[256], stack = new ushort[4];
    private byte fa, fb, sp;
    private ushort pc, sr;
    public long Instructions { get; private set; }
    public int ProgramCounter => pc;
    public ushort Status => (ushort)((sr & 0x400) != 0 ? sr & ~0x1000 : sr);
    public Upd7725(ReadOnlySpan<byte> firmware)
    {
        if (firmware.Length != 8192) throw new InvalidDataException("uPD7725 requires 6144 program bytes followed by 2048 data bytes (little endian).");
        program = new uint[2048]; data = new ushort[1024];
        for (int i = 0; i < program.Length; i++) program[i] = (uint)(firmware[i * 3] | firmware[i * 3 + 1] << 8 | firmware[i * 3 + 2] << 16);
        for (int i = 0; i < data.Length; i++) data[i] = (ushort)(firmware[6144 + i * 2] | firmware[6145 + i * 2] << 8);
    }
    public void Reset(bool powerOn)
    {
        // Deterministic cold seed is not a measured power-on RAM pattern.
        // Warm-reset retention follows the MAME datasheet3.6.1 interpretation;
        // exact DSP1 board reset wiring/oscillator phase is not certified.
        if (powerOn) { Array.Clear(r); Array.Clear(ram); Array.Clear(stack); sp = 0; }
        pc = sr = 0; fa = fb = 0; Instructions = 0;
    }
    public byte ReadStatus() => (byte)(Status >> 8);
    public byte ReadData()
    {
        byte value = (byte)(r[5] >> (((sr & 0x400) == 0 && (sr & 0x1000) != 0) ? 8 : 0));
        HostTransfer(); return value;
    }
    public void WriteData(byte value)
    {
        bool high = (sr & 0x400) == 0 && (sr & 0x1000) != 0;
        r[5] = (ushort)(high ? (r[5] & 255) | (value << 8) : (r[5] & 0xff00) | value);
        HostTransfer();
    }
    private void HostTransfer()
    {
        // A low-byte access cannot acknowledge a 16-bit request. DRC hides
        // rather than destroys DRS, so changing transfer width preserves it.
        if ((sr & 0x400) != 0) sr &= 0x7fff;
        else if ((sr & 0x1000) == 0) sr |= 0x1000;
        else sr &= 0x6fff;
    }
    public void Step()
    {
        uint word = program[pc]; pc = (ushort)((pc + 1) & 2047);
        switch (word >> 22)
        {
            case 0: Parallel(word); break;
            case 1: Parallel(word); sp = (byte)((sp - 1) & 3); pc = (ushort)(stack[sp] & 2047); break;
            case 2: Branch(word); break;
            case 3: Load((int)(word & 15), (ushort)(word >> 6)); break;
        }
        // K/L writes take effect before the multiplier latches this result;
        // the ALU in this same instruction consumed the preceding M/N latch.
        int product = (short)r[7] * (short)r[8]; r[9] = (ushort)(product >> 15); r[10] = (ushort)(product << 1); Instructions++;
    }
    private ushort Source(int source) => source switch
    {
        0 => r[11],
        1 => r[0],
        2 => r[1],
        3 => r[2],
        4 => r[3],
        5 => r[4],
        6 => data[r[4]],
        7 => (ushort)(0x8000 - ((fa >> 5) & 1)),
        8 => RequestData(),
        9 => r[5],
        10 => Status,
        11 or 12 => r[12],
        13 => r[7],
        14 => r[8],
        15 => ram[r[3]],
        _ => 0
    };
    private ushort RequestData() { sr |= 0x8000; return r[5]; }
    private void Parallel(uint word)
    {
        int source = (int)(word >> 4) & 15, destination = (int)word & 15;
        ushort bus = Source(source); int operation = (int)(word >> 16) & 15;
        if (operation != 0 && destination != 1 + ((word >> 15) & 1))
        {
            int acc = (int)(word >> 15) & 1;
            ushort input = ((word >> 20) & 3) switch { 0 => ram[r[3]], 1 => bus, 2 => r[9], _ => r[10] };
            Arithmetic(acc, operation, input);
        }
        Load(destination, bus);
        // The parallel move sees the old source and pointer. Explicit pointer
        // destinations suppress their post-modification, including RP decrement.
        if (destination != 4)
        {
            int low = r[3] & 15;
            low = ((word >> 13) & 3) switch { 1 => (low + 1) & 15, 2 => (low - 1) & 15, 3 => 0, _ => low };
            r[3] = (ushort)(((r[3] & 0xf0) | low) ^ ((word >> 5) & 0xf0));
        }
        if (destination != 5 && (word & 0x100) != 0) r[4] = (ushort)((r[4] - 1) & 1023);
    }
    private void Arithmetic(int acc, int operation, ushort input)
    {
        int q = r[acc], p = input, carry = ((acc == 0 ? fb : fa) >> 3) & 1;
        int full = operation switch { 1 => q | p, 2 => q & p, 3 => q ^ p, 4 => q - p, 5 => q + p, 6 => q - p - carry, 7 => q + p + carry, 8 => q - 1, 9 => q + 1, 10 => ~q, 11 => (short)q >> 1, 12 => (q << 1) | carry, 13 => (q << 2) | 3, 14 => (q << 4) | 15, 15 => (q << 8) | (q >> 8), _ => q };
        ushort result = (ushort)full; byte old = acc == 0 ? fa : fb;
        bool sign = (result & 0x8000) != 0, signLatch = (old & 2) == 0 ? sign : (old & 32) != 0;
        bool overflow = false, sticky = false, outputCarry = false;
        if (operation is >= 4 and <= 9)
        {
            if (operation is 8 or 9) p = 1;
            overflow = (((q ^ result) & ((operation & 1) != 0 ? ~(q ^ p) : (q ^ p))) & 0x8000) != 0;
            // Truncated carry/borrow comparison follows the pinned software
            // reference. ADC/SBB equality ties lack a verified hardware vector.
            outputCarry = (operation & 1) != 0 ? result < q : result > q;
            sticky = overflow && (old & 2) != 0 ? sign == signLatch : overflow || (old & 2) != 0;
        }
        else if (operation == 11) outputCarry = (q & 1) != 0;
        else if (operation == 12) outputCarry = (q & 0x8000) != 0;
        byte flags = (byte)((overflow ? 1 : 0) | (sticky ? 2 : 0) | (result == 0 ? 4 : 0) | (outputCarry ? 8 : 0) | (sign ? 16 : 0) | (signLatch ? 32 : 0));
        r[acc] = result; if (acc == 0) fa = flags; else fb = flags;
    }
    private void Load(int destination, ushort value)
    {
        switch (destination)
        {
            case 1: r[0] = value; break;
            case 2: r[1] = value; break;
            case 3: r[2] = value; break;
            case 4: r[3] = (ushort)(value & 255); break;
            case 5: r[4] = (ushort)(value & 1023); break;
            case 6: r[5] = value; sr |= 0x8000; break;
            case 7: sr = (ushort)((sr & 0x907c) | (value & ~0x907c)); break;
            case 8: case 9: r[6] = value; break;
            case 10: r[7] = value; break;
            case 11: r[7] = value; r[8] = data[r[4]]; break;
            case 12: r[8] = value; r[7] = ram[r[3] | 0x40]; break;
            case 13: r[8] = value; break;
            case 14: r[11] = value; break;
            case 15: ram[r[3]] = value; break;
        }
    }
    private void Branch(uint word)
    {
        int condition = (int)(word >> 13) & 511; ushort address = (ushort)((word >> 2) & 2047);
        if (condition == 0) { pc = (ushort)(r[6] & 2047); return; }
        if (condition is 0x100 or 0x101) { pc = address; return; }
        if (condition is 0x140 or 0x141) { stack[sp] = pc; sp = (byte)((sp + 1) & 3); pc = address; return; }
        bool take = false;
        if (condition is >= 0x80 and <= 0xae && (condition & 1) == 0)
        {
            int group = (condition - 0x80) >> 3; int bit = group switch { 0 => 3, 1 => 2, 2 => 0, 3 => 1, 4 => 4, _ => 5 };
            byte flags = (condition & 4) == 0 ? fa : fb;
            take = (((flags >> bit) & 1) != 0) == ((condition & 2) != 0);
        }
        else take = condition switch
        {
            0xb0 => (r[3] & 15) == 0,
            0xb1 => (r[3] & 15) != 0,
            0xb2 => (r[3] & 15) == 15,
            0xb3 => (r[3] & 15) != 15,
            0xb4 or 0xb8 => true,
            0xb6 or 0xba => false,
            0xbc => (sr & 0x8000) == 0,
            0xbe => (sr & 0x8000) != 0,
            _ => false
        };
        if (take) pc = address;
    }
    public Upd7725State Capture() => new(1, pc, sp, sr, fa, fb, Instructions, r.ToArray(), ram.ToArray(), stack.ToArray());
    public void Restore(Upd7725State s)
    {
        if (s.Version != 1 || s.Pc > 2047 || s.Sp > 3 || s.Fa > 63 || s.Fb > 63 || s.Instructions < 0 || s.Registers is not { Length: 13 } || s.Ram is not { Length: 256 } || s.Stack is not { Length: 4 } || s.Registers[3] > 255 || s.Registers[4] > 1023 || s.Stack.Any(x => x > 2047) || (s.Sr & 0x7c) != 0)
            throw new InvalidDataException("Invalid uPD7725 state.");
        pc = s.Pc; sp = s.Sp; sr = s.Sr; fa = s.Fa; fb = s.Fb; Instructions = s.Instructions; s.Registers.CopyTo(r, 0); s.Ram.CopyTo(ram, 0); s.Stack.CopyTo(stack, 0);
    }
}
public sealed record Upd7725State(int Version, ushort Pc, byte Sp, ushort Sr, byte Fa, byte Fb, long Instructions, ushort[] Registers, ushort[] Ram, ushort[] Stack);
