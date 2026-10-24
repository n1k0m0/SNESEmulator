namespace SNESEmulator.Core;

/// <summary>Instruction-oriented 65C816 foundation. External bus cycles are independently observable and restartable.</summary>
public sealed partial class Cpu65816(ICpuBus bus)
{
    public ushort A, X, Y, S, D, PC;
    public byte P, PBR, DBR;
    public bool E = true, Waiting, Stopped;
    public long Instructions;
    private const byte C = 1, Z = 2, I = 4, Decimal = 8, Index8 = 16, Memory8 = 32, V = 64, N = 128;
    private bool M8 => E || (P & Memory8) != 0;
    private bool X8 => E || (P & Index8) != 0;
    private byte Fetch() => CycleRead(PBR << 16 | PC++, "-p-");
    private ushort Fetch16() { int lo = Fetch(); return (ushort)(lo | Fetch() << 8); }
    private int Fetch24() { int lo = Fetch16(); return lo | Fetch() << 16; }
    private int Read16(int a) => Read(a) | Read((a & 0xFF0000) | ((a + 1) & 65535)) << 8;
    private int Read24(int a) => Read16(a) | Read((a & 0xFF0000) | ((a + 2) & 65535)) << 16;
    private void Push(byte v) { Write(S, v); S--; if (E) S = (ushort)(0x100 | (S & 255)); }
    private byte Pull() { S++; if (E) S = (ushort)(0x100 | (S & 255)); return Read(S); }
    private void PushLongStack(byte v) { Write(S,v); S--; }
    private byte PullLongStack() { S++; return Read(S); }
    private void Push16LongStack(int v) { PushLongStack((byte)(v >> 8)); PushLongStack((byte)v); }
    private ushort Pull16LongStack() { int lo=PullLongStack(); return (ushort)(lo | PullLongStack()<<8); }
    private void FixStack() { if(E) S=(ushort)(0x100 | (S & 255)); }
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
    {RequestReset();Step();}
    public void RequestReset()
    {
        E = true; P = 0x34; S = 0x1FF; D = A = X = Y = 0; PBR = DBR = 0;
        Waiting = Stopped = false; Instructions = 0;RestoreContinuation(null);
        nmiLine=irqLine=nmiLatched=pollNmi=pollIrq=sampleNmi=sampleIrq=false;pendingSample=null;entry=CpuEntry.Reset;
    }
    public void Nmi() { if (!Stopped) {entry=CpuEntry.Nmi;Step();} }
    /// <summary>Instruction-boundary IRQ assertion. Even a masked IRQ wakes WAI.</summary>
    public void Irq()
    {
        if (Stopped) return;
        Waiting = false;
        if ((P & I) == 0) {entry=CpuEntry.Irq;Step();}
    }
    /// <summary>Boundary arbitration; pin latching and cycle polling belong to the scheduler.</summary>
    public void ServiceInterrupts(bool nmi, bool irq) { if (nmi) Nmi(); else if (irq) Irq(); }
    private void Interrupt(int vector, bool software)
    {
        Waiting = false;
        if (!E) Push(PBR);
        Push16(PC); Push(E ? (byte)((P & ~0x10) | (software ? 0x10 : 0)) : P);
        P = (byte)((P | I) & ~Decimal); PBR = 0; this.vector=true; PC = (ushort)Read16(vector); this.vector=false;
    }
    private int Direct(byte offset) { if((D&255)!=0) Idle(); return E && (D & 255) == 0 ? (D & 0xFF00) | offset : (D + offset) & 65535; }
    private int DirectIndexed(byte offset, ushort index) { if((D&255)!=0)Idle();Idle();return E && (D & 255) == 0 ? (D & 0xFF00) | ((offset + index) & 255) : (D + offset + index) & 65535; }
    // (direct,X) wraps the pointer's high-byte read in emulation mode even
    // when D is unaligned. Plain (direct) and (direct),Y wrap only for D.low=0.
    // gilyon/snes-tests v1.4, cputest-full test 0027 exercises D=$011A,
    // operand=$F7, X=$EE: low at $02FF, high at $0200 (not $0300).
    private int DirectPointer(int address, bool indexed = false) => E && (indexed || (D & 255) == 0)
        ? Read(address) | Read((address & 0xFF00) | ((address + 1) & 255)) << 8 : Read16(address);
    // ALU group addressing modes shared by ORA/AND/EOR/ADC/STA/LDA/CMP/SBC.
    private int Address(int low) => low switch
    {
        0x01 => DBR << 16 | DirectPointer(DirectIndexed(Fetch(), X), true),
        0x03 => StackAddress(),
        0x05 => Direct(Fetch()),
        0x07 => Read24(Direct(Fetch())),
        0x0D => DBR << 16 | Fetch16(),
        0x0F => Fetch24(),
        0x11 => Indexed(DBR << 16 | DirectPointer(Direct(Fetch())),Y),
        0x12 => DBR << 16 | DirectPointer(Direct(Fetch())),
        0x13 => StackIndirect(),
        0x15 => DirectIndexed(Fetch(), X),
        0x17 => (Read24(Direct(Fetch())) + Y) & 0xFFFFFF,
        0x19 => Indexed(DBR << 16 | Fetch16(),Y),
        0x1D => Indexed(DBR << 16 | Fetch16(),X),
        0x1F => (Fetch24() + X) & 0xFFFFFF,
        _ => throw new InvalidOperationException("Invalid ALU addressing mode")
    };
    private int ReadValue(int address, bool narrow, bool bankWrap = false) {if(IsRmw(opcode))locked=true;return narrow ? Read(address) : bankWrap ? Read16(address) : Read(address) | Read((address + 1) & 0xFFFFFF) << 8;}
    private void WriteValue(int address, int value, bool narrow, bool bankWrap = false)
    {
        if(IsRmw(opcode)) { if(E) InvalidWrite(address,(byte)ReadLatchedValue(address));else Idle(); if(!narrow) {Write(bankWrap ? (address&0xff0000)|((address+1)&65535) : (address+1)&0xffffff,(byte)(value>>8));Write(address,(byte)value);return;} }
        Write(address, (byte)value);
        if (!narrow) Write(bankWrap ? (address & 0xFF0000) | ((address + 1) & 65535) : (address + 1) & 0xFFFFFF, (byte)(value >> 8));
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
                else {
                    if (shift == (M8 ? 4 : 12)) Flag(V, (~(a ^ value) & (a ^ (digit << shift)) & sign) != 0);
                    if (digit > 9) digit += 6; propagation = digit > 15 ? 1 : 0; }
                result |= (digit & 15) << shift;
            }
            Flag(C, subtract ? propagation == 0 : propagation != 0);
        }
        else Flag(C, subtract ? binary >= 0 : binary > mask);
        SetA(result);
    }
    private void Branch(bool condition) { sbyte delta = (sbyte)Fetch(); if (condition) {Idle();int before=PC;PC=(ushort)(PC+delta);if(E&&(before&0xff00)!=(PC&0xff00))Idle();} }
    private int Execute()
    {
        if (Stopped || Waiting) {Idle(PBR<<16|PC);return 1;}
        int instructionAddress = PBR << 16 | PC;
        byte op = CycleRead(PBR << 16 | PC++, "dp-"); opcode=op; Instructions++;
        if (ImpliedIdle(op)) Idle(PBR << 16 | PC);
        int low = op & 31, group = op >> 5;
        if (low is 1 or 3 or 5 or 7 or 9 or 13 or 15 or 17 or 18 or 19 or 21 or 23 or 25 or 29 or 31 && op != 0x89)
        {
            int address = low == 9 ? -1 : Address(low);
            if (group == 4) { WriteValue(address, A, M8, low is 3 or 5 or 21); return 4; }
            int value = address < 0 ? Immediate(M8) : ReadValue(address, M8, low is 3 or 5 or 21);
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
            case 0x0B: Push16LongStack(D); FixStack(); break;
            case 0x18: P &= unchecked((byte)~C); break;
            case 0x1B: S = E ? (ushort)(0x100 | (A & 255)) : A; break;
            case 0x20: { int target = Fetch16(); Idle(); Push16(PC - 1); PC = (ushort)target; break; }
            case 0x22: { int target = Fetch16(); PushLongStack(PBR); Idle();target|=Fetch()<<16; Push16LongStack(PC - 1); FixStack(); PBR = (byte)(target >> 16); PC = (ushort)target; break; }
            case 0x28: Idle(); Status(Pull()); break;
            case 0x2B: Idle(); D = (ushort)Nz(Pull16LongStack(), false); FixStack(); break;
            case 0x38: P |= C; break;
            case 0x3B: A = (ushort)Nz(S, false); break;
            case 0x40: {Idle();byte status=Pull();P=(byte)((P&~I)|(status&I)); PC = Pull16(); if (!E) PBR = Pull();Status(status);break;}
            case 0x42: Idle(PBR<<16|PC);PC++; break; // WDM reserved operand
            case 0x48: if (M8) Push((byte)A); else Push16(A); break;
            case 0x4B: Push(PBR); break;
            case 0x4C: PC = Fetch16(); break;
            case 0x58: P &= unchecked((byte)~I); break;
            case 0x5A: if (X8) Push((byte)Y); else Push16(Y); break;
            case 0x5B: D = (ushort)Nz(A, false); break;
            case 0x5C: { int a = Fetch24(); PC = (ushort)a; PBR = (byte)(a >> 16); break; }
            case 0x60: Idle(); PC = (ushort)(Pull16() + 1);Idle(); break;
            case 0x62: { short delta = (short)Fetch16();Idle(); Push16LongStack(PC + delta); FixStack(); break; }
            case 0x68: Idle();SetA(M8 ? Pull() : Pull16()); break;
            case 0x6B: Idle();PC = (ushort)(Pull16LongStack() + 1); PBR = PullLongStack(); FixStack(); break;
            case 0x6C: PC = (ushort)Read16(Fetch16()); break;
            case 0x78: P |= I; break;
            case 0x7A: Idle();Y = (ushort)Nz(X8 ? Pull() : Pull16(), X8); break;
            case 0x7B: A = (ushort)Nz(D, false); break;
            case 0x7C: { int a = (Fetch16() + X) & 65535; Idle(); PC = (ushort)Read16(PBR << 16 | a); break; }
            case 0x80: Branch(true); break;
            case 0x82: { short d = (short)Fetch16(); Idle(); PC = (ushort)(PC + d); break; }
            case 0x88: Y = (ushort)Nz(Y - 1, X8); break;
            case 0x8A: SetA(X); break;
            case 0x8B: Push(DBR); break;
            case 0x98: SetA(Y); break;
            case 0x9A: S = E ? (ushort)(0x100 | (X & 255)) : X; break;
            case 0x9B: Y = (ushort)Nz(X, X8); break;
            case 0xA8: Y = (ushort)Nz(A, X8); break;
            case 0xAA: X = (ushort)Nz(A, X8); break;
            case 0xAB: Idle();DBR = (byte)Nz(PullLongStack(), true); FixStack(); break;
            case 0xB8: P &= unchecked((byte)~V); break;
            case 0xBA: X = (ushort)Nz(S, X8); break;
            case 0xBB: X = (ushort)Nz(Y, X8); break;
            case 0xC2: {byte operand=Fetch();Idle();Status((byte)(P & ~operand));} break;
            case 0xC8: Y = (ushort)Nz(Y + 1, X8); break;
            case 0xCA: X = (ushort)Nz(X - 1, X8); break;
            case 0xCB: Idle();Cycle(null,null,"--------");Waiting = true; break;
            case 0xD4: Push16LongStack(Read16(Direct(Fetch()))); FixStack(); break;
            case 0xD8: P &= unchecked((byte)~Decimal); break;
            case 0xDA: if (X8) Push((byte)X); else Push16(X); break;
            case 0xDB: Idle();Cycle(null,null,"--------");Stopped = true; break;
            case 0xDC: { int a = Read24(Fetch16()); PC = (ushort)a; PBR = (byte)(a >> 16); break; }
            case 0xE2: {byte operand=Fetch();Idle();Status((byte)(P | operand));} break;
            case 0xE8: X = (ushort)Nz(X + 1, X8); break;
            case 0xEA: break;
            case 0xEB: Idle();A = (ushort)((A >> 8) | (A << 8)); Nz(A & 255, true); break;
            case 0xF4: Push16LongStack(Fetch16()); FixStack(); break;
            case 0xF8: P |= Decimal; break;
            case 0xFA: Idle();X = (ushort)Nz(X8 ? Pull() : Pull16(), X8); break;
            case 0xFB: { bool oldE = E; E = (P & C) != 0; Flag(C, oldE); Status(P); if (E) S = (ushort)(0x100 | (S & 255)); break; }
            case 0xFC: { int lo=Fetch();Push16LongStack(PC);int a=lo|Fetch()<<8;Idle();int target=Read16(PBR<<16|((a+X)&65535)); FixStack(); PC = (ushort)target; break; }
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
                Write(destination << 16 | Y, Read(source << 16 | X));
                Idle();Idle();int delta = op == 0x54 ? 1 : -1; X = (ushort)((X + delta) & Mask(X8)); Y = (ushort)((Y + delta) & Mask(X8));
                if (A-- != 0) PC -= 3; return 7;
            }
            case 0xA0: Y = (ushort)Nz(Immediate(X8), X8); break;
            case 0xA2: X = (ushort)Nz(Immediate(X8), X8); break;
            case 0xA4: case 0xAC: case 0xB4: case 0xBC:
            {
                int a = op switch { 0xA4 => Direct(Fetch()), 0xAC => DBR << 16 | Fetch16(), 0xB4 => DirectIndexed(Fetch(), X), _ => Indexed(DBR << 16 | Fetch16(),X) };
                Y = (ushort)Nz(ReadValue(a, X8, (op & 15) < 8), X8); break;
            }
            case 0xA6: case 0xAE: case 0xB6: case 0xBE:
            {
                int a = op switch { 0xA6 => Direct(Fetch()), 0xAE => DBR << 16 | Fetch16(), 0xB6 => DirectIndexed(Fetch(), Y), _ => Indexed(DBR << 16 | Fetch16(),Y) };
                X = (ushort)Nz(ReadValue(a, X8, (op & 15) < 8), X8); break;
            }
            case 0x84: case 0x8C: case 0x94:
                WriteValue(op == 0x84 ? Direct(Fetch()) : op == 0x8C ? DBR << 16 | Fetch16() : DirectIndexed(Fetch(), X), Y, X8, op != 0x8C); break;
            case 0x86: case 0x8E: case 0x96:
                WriteValue(op == 0x86 ? Direct(Fetch()) : op == 0x8E ? DBR << 16 | Fetch16() : DirectIndexed(Fetch(), Y), X, X8, op != 0x8E); break;
            case 0x64: case 0x74: case 0x9C: case 0x9E:
                WriteValue(op switch { 0x64 => Direct(Fetch()), 0x74 => DirectIndexed(Fetch(), X), 0x9C => DBR << 16 | Fetch16(), _ => Indexed(DBR << 16 | Fetch16(),X) }, 0, M8, op is 0x64 or 0x74); break;
            case 0xC0: case 0xE0: Compare(op == 0xC0 ? Y : X, Immediate(X8), X8); break;
            case 0xC4: case 0xCC: case 0xE4: case 0xEC:
                Compare(op < 0xE0 ? Y : X, ReadValue((op & 8) == 0 ? Direct(Fetch()) : DBR << 16 | Fetch16(), X8, (op & 8) == 0), X8); break;
            case 0x24: case 0x2C: case 0x34: case 0x3C: case 0x89:
            {
                int value = op == 0x89 ? Immediate(M8) : ReadValue(op switch { 0x24 => Direct(Fetch()), 0x2C => DBR << 16 | Fetch16(), 0x34 => DirectIndexed(Fetch(), X), _ => Indexed(DBR << 16 | Fetch16(),X) }, M8, op is 0x24 or 0x34);
                Flag(Z, (A & value & Mask(M8)) == 0);
                if (op != 0x89) { Flag(N, (value & (M8 ? 128 : 32768)) != 0); Flag(V, (value & (M8 ? 64 : 16384)) != 0); }
                break;
            }
            case 0x04: case 0x0C: case 0x14: case 0x1C:
            {
                int a = (op & 8) == 0 ? Direct(Fetch()) : DBR << 16 | Fetch16(), value = ReadValue(a, M8, (op & 15) < 8);
                Flag(Z, (value & A & Mask(M8)) == 0); WriteValue(a, op < 0x10 ? value | A : value & ~A, M8, (op & 8) == 0); break;
            }
            default:
                if ((op & 15) is 6 or 14 || op is 0x0A or 0x2A or 0x4A or 0x6A or 0x1A or 0x3A)
                {
                    bool accumulator = (op & 15) == 10;
                    int address = accumulator ? -1 : (op & 31) switch
                    {
                        6 => Direct(Fetch()), 14 => DBR << 16 | Fetch16(), 22 => DirectIndexed(Fetch(), X),
                        30 => Indexed(DBR << 16 | Fetch16(),X), _ => -1
                    };
                    int value = accumulator ? A & Mask(M8) : ReadValue(address, M8, (op & 15) == 6), carry = P & C;
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
                    if (accumulator) SetA(result); else WriteValue(address, Nz(result, M8), M8, (op & 15) == 6);
                }
                else throw new NotSupportedException($"Opcode {op:X2} at {instructionAddress:X6}");
                break;
        }
        return 4;
    }
}
