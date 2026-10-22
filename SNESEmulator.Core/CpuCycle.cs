namespace SNESEmulator.Core;

/// <summary>Electrical CPU bus cycle. Null data means the memory select pins are inactive.</summary>
public sealed record CpuCycle(int? Address, byte? Value, string Pins)
{
    public bool Read => Pins[3] == 'r';
    public bool Valid => Pins[0] != '-' || Pins[1] != '-' || Pins[2] != '-';
}

public sealed record CpuRegisters(ushort A, ushort X, ushort Y, ushort S, ushort D, ushort PC,
    byte P, byte PBR, byte DBR, bool E, bool Waiting, bool Stopped, long Instructions);

/// <summary>Restartable continuation: completed external effects are latched, never repeated.</summary>
public enum CpuEntry { Instruction, Reset, Nmi, Irq, WakeNmi, WakeIrq, WakeOnly }
public sealed record CpuInterruptState(bool NmiLine,bool IrqLine,bool NmiLatched,bool PollNmi,bool PollIrq,CpuEntry Entry,InterruptSample? PendingSample);
public sealed record InterruptSample(bool Nmi,bool Irq);
public sealed record CpuContinuation(CpuRegisters Initial, CpuCycle[] Completed,CpuEntry Entry,InterruptSample[] Samples);
