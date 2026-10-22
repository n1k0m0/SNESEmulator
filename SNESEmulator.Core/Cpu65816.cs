namespace SNESEmulator.Core;

/// <summary>Instruction-oriented 65C816 foundation. Timing is approximate, not cycle accurate.</summary>
public sealed class Cpu65816(ICpuBus bus)
{
    public ushort A, X, Y, S, D, PC;
    public byte P, PBR, DBR;
    public bool E = true, Waiting, Stopped;
    public long Instructions;
    private const byte C = 1, Z = 2, I = 4, Decimal = 8, Index8 = 16, Memory8 = 32, V = 64, N = 128;
    private bool M8 => E || (P & Memory8) != 0;
    private bool X8 => E || (P & Index8) != 0;
    private byte Fetch() => bus.Read(PBR << 16 | PC++);
    private ushort Fetch16() { int lo = Fetch(); return (ushort)(lo | Fetch() << 8); }
    private int Fetch24() { int lo = Fetch16(); return lo | Fetch() << 16; }
    private int Read16(int a) => bus.Read(a) | bus.Read((a & 0xFF0000) | ((a + 1) & 65535)) << 8;
    private int Read24(int a) => Read16(a) | bus.Read((a & 0xFF0000) | ((a + 2) & 65535)) << 16;
    private void Push(byte v) { bus.Write(S, v); S--; if (E) S = (ushort)(0x100 | (S & 255)); }
    private byte Pull() { S++; if (E) S = (ushort)(0x100 | (S & 255)); return bus.Read(S); }
    private void Push16(int v) { Push((byte)(v >> 8)); Push((byte)v); }
    private ushort Pull16() { int lo = Pull(); return (ushort)(lo | Pull() << 8); }
    private int Mask(bool narrow) => narrow ? 255 : 65535;
    private void Flag(byte flag, bool value) => P = (byte)(value ? P | flag : P & ~flag);
    private int Nz(int v, bool narrow)
    {
        v &= Mask(narrow); Flag(Z, v == 0); Flag(N, (v & (narrow ? 128 : 32768)) != 0); return v;
    }
    private void SetA(int v) => A = (ushort)(M8 ? (A & 0xFF00) | Nz(v, true) : Nz(v, false));
    private void Status(byte value)
    {
        P = E ? (byte)(value | 0x30) : value;
        if (X8) { X &= 255; Y &= 255; }
    }
    public void Reset()
    {
        E = true; P = 0x34; S = 0x1FF; D = A = X = Y = 0; PBR = DBR = 0;
        PC = (ushort)Read16(0xFFFC); Waiting = Stopped = false; Instructions = 0;
    }
    public void Nmi() { if (!Stopped) Interrupt(E ? 0xFFFA : 0xFFEA, false); }
    private void Interrupt(int vector, bool software)
    {
        Waiting = false;
        if (!E) Push(PBR);
        Push16(PC); Push(E ? (byte)((P & ~0x10) | (software ? 0x10 : 0)) : P);
        P = (byte)((P | I) & ~Decimal); PBR = 0; PC = (ushort)Read16(vector);
    }
    private int Direct(byte offset) => E && (D & 255) == 0 ? (D & 0xFF00) | offset : (D + offset) & 65535;
    private int DirectIndexed(byte offset, ushort index) => E && (D & 255) == 0 ? (D & 0xFF00) | ((offset + index) & 255) : (D + offset + index) & 65535;
    private int DirectPointer(int address) => E && (D & 255) == 0
        ? bus.Read(address) | bus.Read((address & 0xFF00) | ((address + 1) & 255)) << 8 : Read16(address);
    // ALU group addressing modes shared by ORA/AND/EOR/ADC/STA/LDA/CMP/SBC.
    private int Address(int low) => low switch
    {
        0x01 => DBR << 16 | DirectPointer(DirectIndexed(Fetch(), X)),
        0x03 => (S + Fetch()) & 65535,
        0x05 => Direct(Fetch()),
        0x07 => Read24(Direct(Fetch())),
        0x0D => DBR << 16 | Fetch16(),
        0x0F => Fetch24(),
        0x11 => ((DBR << 16 | DirectPointer(Direct(Fetch()))) + Y) & 0xFFFFFF,
        0x12 => DBR << 16 | DirectPointer(Direct(Fetch())),
        0x13 => ((DBR << 16 | Read16((S + Fetch()) & 65535)) + Y) & 0xFFFFFF,
        0x15 => DirectIndexed(Fetch(), X),
        0x17 => (Read24(Direct(Fetch())) + Y) & 0xFFFFFF,
        0x19 => ((DBR << 16 | Fetch16()) + Y) & 0xFFFFFF,
        0x1D => ((DBR << 16 | Fetch16()) + X) & 0xFFFFFF,
        0x1F => (Fetch24() + X) & 0xFFFFFF,
        _ => throw new InvalidOperationException("Invalid ALU addressing mode")
    };
    private int ReadValue(int address, bool narrow) => narrow ? bus.Read(address) : Read16(address);
    private void WriteValue(int address, int value, bool narrow)
    {
        bus.Write(address, (byte)value);
        if (!narrow) bus.Write((address & 0xFF0000) | ((address + 1) & 65535), (byte)(value >> 8));
    }
    private int Immediate(bool narrow) => narrow ? Fetch() : Fetch16();
    private void Compare(int left, int right, bool narrow) { int mask = Mask(narrow); Flag(C, (left & mask) >= right); Nz(left - right, narrow); }
    private void Add(int value, bool subtract)
    {
        int mask = Mask(M8), sign = M8 ? 128 : 32768, a = A & mask, carry = P & C;
        int binary = subtract ? a - value - (1 - carry) : a + value + carry;
        Flag(V, subtract ? ((a ^ value) & (a ^ binary) & sign) != 0 : (~(a ^ value) & (a ^ binary) & sign) != 0);
        int result = binary;
        if ((P & Decimal) != 0)
        {
            result = 0;
            int propagation = subtract ? 1 - carry : carry;
            for (int shift = 0; shift < (M8 ? 8 : 16); shift += 4)
            {
                int digit = subtract ? ((a >> shift) & 15) - ((value >> shift) & 15) - propagation
                    : ((a >> shift) & 15) + ((value >> shift) & 15) + propagation;
                if (subtract) { propagation = digit < 0 ? 1 : 0; if (digit < 0) digit -= 6; }
                else { if (digit > 9) digit += 6; propagation = digit > 15 ? 1 : 0; }
                result |= (digit & 15) << shift;
            }
            Flag(C, subtract ? propagation == 0 : propagation != 0);
        }
        else Flag(C, subtract ? binary >= 0 : binary > mask);
        SetA(result);
    }
    private void Branch(bool condition) { sbyte delta = (sbyte)Fetch(); if (condition) PC = (ushort)(PC + delta); }
    public int Step()
    {
        if (Stopped || Waiting) return 2;
        int instructionAddress = PBR << 16 | PC;
        byte op = Fetch(); Instructions++;
        int low = op & 31, group = op >> 5;
        if (low is 1 or 3 or 5 or 7 or 9 or 13 or 15 or 17 or 18 or 19 or 21 or 23 or 25 or 29 or 31 && op != 0x89)
        {
            int address = low == 9 ? -1 : Address(low);
            if (group == 4) { WriteValue(address, A, M8); return 4; }
            int value = address < 0 ? Immediate(M8) : ReadValue(address, M8);
            switch (group)
            {
                case 0: SetA(A | value); break;
                case 1: SetA(A & value); break;
                case 2: SetA(A ^ value); break;
                case 3: Add(value, false); break;
                case 5: SetA(value); break;
                case 6: Compare(A, value, M8); break;
                case 7: Add(value, true); break;
            }
            return address < 0 ? 3 : 5;
        }
        switch (op)
        {
            case 0x00: Fetch(); Interrupt(E ? 0xFFFE : 0xFFE6, true); break;
            case 0x02: Fetch(); Interrupt(E ? 0xFFF4 : 0xFFE4, true); break;
            case 0x08: Push(P); break;
            case 0x0B: Push16(D); break;
            case 0x18: P &= unchecked((byte)~C); break;
            case 0x1B: S = E ? (ushort)(0x100 | (A & 255)) : A; break;
            case 0x20: { int target = Fetch16(); Push16(PC - 1); PC = (ushort)target; break; }
            case 0x22: { int target = Fetch24(); Push(PBR); Push16(PC - 1); PBR = (byte)(target >> 16); PC = (ushort)target; break; }
            case 0x28: Status(Pull()); break;
            case 0x2B: D = (ushort)Nz(Pull16(), false); break;
            case 0x38: P |= C; break;
            case 0x3B: A = (ushort)Nz(S, false); break;
            case 0x40: Status(Pull()); PC = Pull16(); if (!E) PBR = Pull(); break;
            case 0x42: Fetch(); break; // WDM reserved operand
            case 0x48: if (M8) Push((byte)A); else Push16(A); break;
            case 0x4B: Push(PBR); break;
            case 0x4C: PC = Fetch16(); break;
            case 0x58: P &= unchecked((byte)~I); break;
            case 0x5A: if (X8) Push((byte)Y); else Push16(Y); break;
            case 0x5B: D = (ushort)Nz(A, false); break;
            case 0x5C: { int a = Fetch24(); PC = (ushort)a; PBR = (byte)(a >> 16); break; }
            case 0x60: PC = (ushort)(Pull16() + 1); break;
            case 0x62: { short delta = (short)Fetch16(); Push16(PC + delta); break; }
            case 0x68: SetA(M8 ? Pull() : Pull16()); break;
            case 0x6B: PC = (ushort)(Pull16() + 1); PBR = Pull(); break;
            case 0x6C: PC = (ushort)Read16(Fetch16()); break;
            case 0x78: P |= I; break;
            case 0x7A: Y = (ushort)Nz(X8 ? Pull() : Pull16(), X8); break;
            case 0x7B: A = (ushort)Nz(D, false); break;
            case 0x7C: { int a = (Fetch16() + X) & 65535; PC = (ushort)Read16(PBR << 16 | a); break; }
            case 0x80: Branch(true); break;
            case 0x82: { short d = (short)Fetch16(); PC = (ushort)(PC + d); break; }
            case 0x88: Y = (ushort)Nz(Y - 1, X8); break;
            case 0x8A: SetA(X); break;
            case 0x8B: Push(DBR); break;
            case 0x98: SetA(Y); break;
            case 0x9A: S = E ? (ushort)(0x100 | (X & 255)) : X; break;
            case 0x9B: Y = (ushort)Nz(X, X8); break;
            case 0xA8: Y = (ushort)Nz(A, X8); break;
            case 0xAA: X = (ushort)Nz(A, X8); break;
            case 0xAB: DBR = (byte)Nz(Pull(), true); break;
            case 0xB8: P &= unchecked((byte)~V); break;
            case 0xBA: X = (ushort)Nz(S, X8); break;
            case 0xBB: X = (ushort)Nz(Y, X8); break;
            case 0xC2: Status((byte)(P & ~Fetch())); break;
            case 0xC8: Y = (ushort)Nz(Y + 1, X8); break;
            case 0xCA: X = (ushort)Nz(X - 1, X8); break;
            case 0xCB: Waiting = true; break;
            case 0xD4: Push16(Read16(Direct(Fetch()))); break;
            case 0xD8: P &= unchecked((byte)~Decimal); break;
            case 0xDA: if (X8) Push((byte)X); else Push16(X); break;
            case 0xDB: Stopped = true; break;
            case 0xDC: { int a = Read24(Fetch16()); PC = (ushort)a; PBR = (byte)(a >> 16); break; }
            case 0xE2: Status((byte)(P | Fetch())); break;
            case 0xE8: X = (ushort)Nz(X + 1, X8); break;
            case 0xEA: break;
            case 0xEB: A = (ushort)((A >> 8) | (A << 8)); Nz(A & 255, true); break;
            case 0xF4: Push16(Fetch16()); break;
            case 0xF8: P |= Decimal; break;
            case 0xFA: X = (ushort)Nz(X8 ? Pull() : Pull16(), X8); break;
            case 0xFB: { bool oldE = E; E = (P & C) != 0; Flag(C, oldE); Status(P); if (E) S = (ushort)(0x100 | (S & 255)); break; }
            case 0xFC: { int target = Read16(PBR << 16 | ((Fetch16() + X) & 65535)); Push16(PC - 1); PC = (ushort)target; break; }
            case 0x10: Branch((P & N) == 0); break;
            case 0x30: Branch((P & N) != 0); break;
            case 0x50: Branch((P & V) == 0); break;
            case 0x70: Branch((P & V) != 0); break;
            case 0x90: Branch((P & C) == 0); break;
            case 0xB0: Branch((P & C) != 0); break;
            case 0xD0: Branch((P & Z) == 0); break;
            case 0xF0: Branch((P & Z) != 0); break;
            case 0x44: case 0x54:
            {
                byte destination = Fetch(), source = Fetch(); DBR = destination;
                bus.Write(destination << 16 | Y, bus.Read(source << 16 | X));
                int delta = op == 0x54 ? 1 : -1; X = (ushort)(X + delta); Y = (ushort)(Y + delta);
                if (A-- != 0) PC -= 3; return 7;
            }
            case 0xA0: Y = (ushort)Nz(Immediate(X8), X8); break;
            case 0xA2: X = (ushort)Nz(Immediate(X8), X8); break;
            case 0xA4: case 0xAC: case 0xB4: case 0xBC:
            {
                int a = op switch { 0xA4 => Direct(Fetch()), 0xAC => DBR << 16 | Fetch16(), 0xB4 => DirectIndexed(Fetch(), X), _ => ((DBR << 16 | Fetch16()) + X) & 0xFFFFFF };
                Y = (ushort)Nz(ReadValue(a, X8), X8); break;
            }
            case 0xA6: case 0xAE: case 0xB6: case 0xBE:
            {
                int a = op switch { 0xA6 => Direct(Fetch()), 0xAE => DBR << 16 | Fetch16(), 0xB6 => DirectIndexed(Fetch(), Y), _ => ((DBR << 16 | Fetch16()) + Y) & 0xFFFFFF };
                X = (ushort)Nz(ReadValue(a, X8), X8); break;
            }
            case 0x84: case 0x8C: case 0x94:
                WriteValue(op == 0x84 ? Direct(Fetch()) : op == 0x8C ? DBR << 16 | Fetch16() : DirectIndexed(Fetch(), X), Y, X8); break;
            case 0x86: case 0x8E: case 0x96:
                WriteValue(op == 0x86 ? Direct(Fetch()) : op == 0x8E ? DBR << 16 | Fetch16() : DirectIndexed(Fetch(), Y), X, X8); break;
            case 0x64: case 0x74: case 0x9C: case 0x9E:
                WriteValue(op switch { 0x64 => Direct(Fetch()), 0x74 => DirectIndexed(Fetch(), X), 0x9C => DBR << 16 | Fetch16(), _ => ((DBR << 16 | Fetch16()) + X) & 0xFFFFFF }, 0, M8); break;
            case 0xC0: case 0xE0: Compare(op == 0xC0 ? Y : X, Immediate(X8), X8); break;
            case 0xC4: case 0xCC: case 0xE4: case 0xEC:
                Compare(op < 0xE0 ? Y : X, ReadValue((op & 8) == 0 ? Direct(Fetch()) : DBR << 16 | Fetch16(), X8), X8); break;
            case 0x24: case 0x2C: case 0x34: case 0x3C: case 0x89:
            {
                int value = op == 0x89 ? Immediate(M8) : ReadValue(op switch { 0x24 => Direct(Fetch()), 0x2C => DBR << 16 | Fetch16(), 0x34 => DirectIndexed(Fetch(), X), _ => ((DBR << 16 | Fetch16()) + X) & 0xFFFFFF }, M8);
                Flag(Z, (A & value & Mask(M8)) == 0);
                if (op != 0x89) { Flag(N, (value & (M8 ? 128 : 32768)) != 0); Flag(V, (value & (M8 ? 64 : 16384)) != 0); }
                break;
            }
            case 0x04: case 0x0C: case 0x14: case 0x1C:
            {
                int a = (op & 8) == 0 ? Direct(Fetch()) : DBR << 16 | Fetch16(), value = ReadValue(a, M8);
                Flag(Z, (value & A & Mask(M8)) == 0); WriteValue(a, op < 0x10 ? value | A : value & ~A, M8); break;
            }
            default:
                if ((op & 15) is 6 or 14 || op is 0x0A or 0x2A or 0x4A or 0x6A or 0x1A or 0x3A)
                {
                    bool accumulator = (op & 15) == 10;
                    int address = accumulator ? -1 : (op & 31) switch
                    {
                        6 => Direct(Fetch()), 14 => DBR << 16 | Fetch16(), 22 => DirectIndexed(Fetch(), X),
                        30 => ((DBR << 16 | Fetch16()) + X) & 0xFFFFFF, _ => -1
                    };
                    int value = accumulator ? A & Mask(M8) : ReadValue(address, M8), carry = P & C;
                    int operation = accumulator && op == 0x1A ? 7 : accumulator && op == 0x3A ? 6 : op >> 5;
                    int result;
                    switch (operation)
                    {
                        case 0: Flag(C, (value & (M8 ? 128 : 32768)) != 0); result = value << 1; break;
                        case 1: Flag(C, (value & (M8 ? 128 : 32768)) != 0); result = value << 1 | carry; break;
                        case 2: Flag(C, (value & 1) != 0); result = value >> 1; break;
                        case 3: Flag(C, (value & 1) != 0); result = value >> 1 | carry * (M8 ? 128 : 32768); break;
                        case 6: result = value - 1; break;
                        case 7: result = value + 1; break;
                        default: throw new NotSupportedException($"Opcode {op:X2} at {instructionAddress:X6}");
                    }
                    if (accumulator) SetA(result); else WriteValue(address, Nz(result, M8), M8);
                }
                else throw new NotSupportedException($"Opcode {op:X2} at {instructionAddress:X6}");
                break;
        }
        return 4;
    }
}
