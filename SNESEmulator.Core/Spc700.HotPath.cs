namespace SNESEmulator.Core;

public sealed partial class Spc700
{
    // These are decoder locals, not additional emulated hardware state. Restore
    // discards them and reconstructs the exact point from Entry + Completed.
    private int hotPhase = -1, hotOperand0, hotOperand1;
    private bool hotBranchTaken, hotLegacy;
    private byte hotRead;
    private HotRegisters? hotSnapshot;
    private readonly record struct HotRegisters(ushort PC, byte A, byte X, byte Y,
        byte SP, byte PSW, bool Sleeping, bool Stopped, long Instructions);

    // Diagnostic path counters are per CPU, deliberately outside savestates.
    // Attempts include admission retries and reconstruction from a loaded journal.
    internal long HotInstructionsCompleted { get; private set; }
    internal long ReplayRunsCompleted { get; private set; }
    internal long HotCycleAttempts { get; private set; }

    private HotRegisters CaptureHotRegisters() =>
        new(PC, A, X, Y, SP, PSW, Sleeping, Stopped, Instructions);

    private void RestoreHotRegisters(HotRegisters r)
    {
        PC = r.PC; A = r.A; X = r.X; Y = r.Y; SP = r.SP; PSW = r.PSW;
        Sleeping = r.Sleeping; Stopped = r.Stopped; Instructions = r.Instructions;
    }

    private void InvalidateHotContinuation()
    {
        hotPhase = -1;
        hotSnapshot = null;
        hotLegacy = false;
    }

    /// <summary>
    /// Runs to instruction completion or the next admitted timing boundary.
    /// The generated common forms retain decoder locals between bus endpoints;
    /// other opcodes deliberately use the original replay decoder.
    /// </summary>
    public bool RunInstruction()
    {
        if (hotLegacy || (hotPhase < 0 && (Sleeping || Stopped)))
            return RunLegacyContinuation();

        if (hotPhase < 0)
        {
            // A restored public continuation is replayed once to reconstruct
            // locals. Its completed MMIO accesses must never run a second time.
            entry ??= Capture();
            Restore(entry);
            cursor = 0;
            hotPhase = 0;
        }
        else if (hotSnapshot is {} saved)
        {
            // Match replay semantics even if a caller edited public registers
            // while suspended: resume from the previously exposed register point.
            RestoreHotRegisters(saved);
        }

        suspended = null;
        cooperative = true;
        try { return ResumeHotInstruction(); }
        finally { suspended = null; cooperative = false; }
    }

    private bool RunLegacyContinuation()
    {
        bool done = RunInstructionLegacy();
        if (done) { hotLegacy = false; ReplayRunsCompleted++; }
        return done;
    }

    private bool FinishHotInstruction()
    {
        Instructions++;
        HotInstructionsCompleted++;
        InvalidateHotContinuation();
        entry = null;
        completed.Clear();
        return true;
    }

    private bool TryHotCycle(ushort? address, byte? value, string direction, int next)
    {
        HotCycleAttempts++;
        if (cursor < completed.Count)
        {
            var previous = completed[cursor++];
            if (previous.Address != address || previous.Direction != direction ||
                (direction == "write" && previous.Value != value))
                throw new InvalidDataException("SPC hot-path replay diverged");
            hotRead = previous.Value ?? 0;
            hotPhase = next;
            return true;
        }

        SpcCycle? row = null;
        // Legacy admission runs first. A veto cannot leave a newly advanced
        // electrical endpoint that the existing serialized journal cannot express.
        if ((PrepareCycle != null && !PrepareCycle(row = new(address, value, direction))) ||
            (PrepareBusCycle != null && !PrepareBusCycle(address, direction)))
        {
            hotSnapshot = CaptureHotRegisters();
            return false;
        }
        if (BeforeCycle != null) BeforeCycle(row ??= new(address, value, direction));
        if (direction == "read") value = bus.Read(address!.Value);
        if (direction == "write") bus.Write(address!.Value, value!.Value);

        // Journal first, then observers. A postlatch yield retains the real read
        // value but does not execute the following PC/ALU/register assignment yet.
        completed.Add(new JournalCycle(address, value, direction));
        cursor++;
        hotRead = value ?? 0;
        hotPhase = next;
        bool electricalDone = CompleteBusCycle?.Invoke(address, value, direction) ?? true;
        bool legacyDone = EffectCompleted?.Invoke(new(address, value, direction)) ?? true;
        if (!electricalDone || !legacyDone)
        {
            hotSnapshot = CaptureHotRegisters();
            return false;
        }
        CycleObserved?.Invoke(new(address, value, direction));
        return true;
    }
}
