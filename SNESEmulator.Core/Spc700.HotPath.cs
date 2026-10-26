// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE and NOTICE for permissions and attribution.
//
// Executes resumable SPC700 microphases for selected frequent instructions.
// The journal preserves compatibility with generic-decoder continuation and replay.
//

namespace SNESEmulator.Core;
public sealed partial class Spc700
{
    // These are decoder locals, not additional emulated hardware state. Restore
    // discards them and reconstructs the exact point from Entry + Completed.
    private int hotPhase = -1, hotOperand0, hotOperand1;
    private bool hotBranchTaken, hotLegacy;
    private byte hotRead;
    private HotRegisters? hotSnapshot, hotEntry;
    internal long HotValueEntriesStarted { get; private set; }
    internal long HotEntryRecordsMaterialized { get; private set; }

    // Keep the original entry immutable even if an observer edits exposed registers.
    // Public snapshots cache the same record after the first request; State9 still
    // contains the original Entry plus the existing completed-cycle journal.
    private void MaterializeHotEntry()
    {
        if (entry == null && hotEntry is { } saved)
        {
            entry = new(saved.PC, saved.A, saved.X, saved.Y, saved.SP, saved.PSW, saved.Sleeping, saved.Stopped, saved.Instructions);
            HotEntryRecordsMaterialized++;
        }
    }

    private readonly record struct HotRegisters(ushort PC, byte A, byte X, byte Y, byte SP, byte PSW, bool Sleeping, bool Stopped, long Instructions);
    // Diagnostic path counters are per CPU, deliberately outside savestates.
    // Attempts include admission retries and reconstruction from a loaded journal.
    internal long HotInstructionsCompleted { get; private set; }
    internal long ReplayRunsCompleted { get; private set; }
    internal long HotCycleAttempts { get; private set; }

    private HotRegisters CaptureHotRegisters() => new(PC, A, X, Y, SP, PSW, Sleeping, Stopped, Instructions);
    private void RestoreHotRegisters(HotRegisters r)
    {
        PC = r.PC;
        A = r.A;
        X = r.X;
        Y = r.Y;
        SP = r.SP;
        PSW = r.PSW;
        Sleeping = r.Sleeping;
        Stopped = r.Stopped;
        Instructions = r.Instructions;
    }

    private void InvalidateHotContinuation()
    {
        hotPhase = -1;
        hotEntry = null;
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
            if (entry != null)
                Restore(entry);
            else if (hotEntry is { } original)
                RestoreHotRegisters(original);
            else
            {
                hotEntry = CaptureHotRegisters();
                HotValueEntriesStarted++;
            }

            cursor = 0;
            hotPhase = 0;
        }
        else if (hotSnapshot is { } saved)
        {
            // Match replay semantics even if a caller edited public registers
            // while suspended: resume from the previously exposed register point.
            RestoreHotRegisters(saved);
        }

        suspended = null;
        cooperative = true;
        try
        {
            return ResumeHotInstruction();
        }
        finally
        {
            suspended = null;
            cooperative = false;
        }
    }

    private bool RunLegacyContinuation()
    {
        // An opcode already fetched by the hot dispatcher is journaled. The replay
        // decoder must start at the ORIGINAL entry, not the exposed PC+1 point.
        MaterializeHotEntry();
        bool done = RunInstructionLegacy();
        if (done)
        {
            hotEntry = null;
            hotLegacy = false;
            ReplayRunsCompleted++;
        }

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

    // The compact private journal and generated decoder are allocation choices,
    // not a second timing model. Both decoders must admit the same bus effect;
    // replay reconstructs locals without repeating committed reads or writes.
    private bool TryHotCycle(ushort? address, byte? value, string direction, int next)
    {
        HotCycleAttempts++;
        if (cursor < completed.Count)
        {
            var previous = completed[cursor++];
            if (previous.Address != address || previous.Direction != direction || (direction == "write" && previous.Value != value))
                throw new InvalidDataException("SPC hot-path replay diverged");
            hotRead = previous.Value ?? 0;
            hotPhase = next;
            return true;
        }

        SpcCycle? row = null;
        // Legacy admission runs first. A veto cannot leave a newly advanced
        // electrical endpoint that the existing serialized journal cannot express.
        if ((PrepareCycle != null && !PrepareCycle(row = new(address, value, direction))) || (PrepareBusCycle != null && !PrepareBusCycle(address, direction)))
        {
            hotSnapshot = CaptureHotRegisters();
            return false;
        }

        if (BeforeCycle != null)
            BeforeCycle(row ??= new(address, value, direction));
        if (direction == "read")
            value = bus.Read(address!.Value);
        if (direction == "write")
            bus.Write(address!.Value, value!.Value);
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
