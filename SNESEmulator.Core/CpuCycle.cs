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
// Describes CPU bus transactions and their observable electrical phases.
// Trace records are data contracts shared by execution, continuation and tests.
//

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
