# M2 timing contract and evidence limits

The own C# CPU emits independently generated electrical cycle records. The full
external 65816 suite passes semantics and exact bus rows separately. Hardware
profile tests are analytical conformance tests, not measurements of a console.
The selected separate CPU/two-PPU 2/1/3 revision target remains unverified.

## Execution and state

`Cpu65816.Step()` remains the fast standalone instruction adapter. `AdvanceCycle()`
performs at most one real external effect; a final false return commits the
instruction without adding a cycle. `RunInstruction()` uses the same helpers and
can suspend when the scheduler reaches a clock budget. No instruction executes
ahead of a real read, and no MMIO effect is performed speculatively.

A continuation consists of instruction-entry registers, entry kind, completed
cycle values/pins and interrupt samples. Resumption recomputes the deterministic
local calculations from that entry, consuming latched values for prior cycles.
Prior reads/writes do not touch the bus again. The first uncompleted access calls
the real bus at its actual time. Architectural registers commit at instruction
completion; traces carry the in-flight pin values, including width changes.
Reset/IRQ/NMI/wake entry and partial-cycle interrupt samples are serializable.
This is a data-only continuation, with no suspended iterator or unserializable
closure. Current instructions have a bounded per-byte continuation; block moves
remain interruptible per byte.

`SnesSystem.RunUntil(t)` ends at exactly master clock `t`. A partial cycle retains
its remaining active clocks. Refresh and external typed stalls advance the
independent clock but pause the CPU cycle. Valid reads latch after `accessClocks - 4` active clocks, followed by four active
clocks of trailing hold. Writes latch at cycle completion. Read data and MMIO
side effects are captured once, before trailing time; a suspended hold stores
the actual read value and cannot repeat the access. Simultaneous stalls pause
the active pre-latch or hold phase. At a shared timestamp,
typed events run first, ordered by priority and then creation sequence; CPU bus
latches and completion transitions run after them. Raster state and interrupt requests synchronize before
and after each bus effect. WAI/STP generate no ongoing CPU accesses while the
clock, raster and APU accumulator continue. The all-low upstream terminal row is
a zero-duration quiescent output marker.

`RunFrame()` ends at the next emulated field transition using that exact-budget
API. One-clock, block and field requests produce byte-identical complete states.
There is no Windows timer or rounded50/60-Hz value in hardware progression.
Savestate format3 stores profile, event queue/stable sequence, raster/field,
APU rational remainder, partial CPU time, pending continuation, interrupt latches
and legacy DMA stall debt. It also stores the latched read, trailing hold time,
cycle start and latch timestamp. Formats1 and2 are explicitly rejected.

## Analytical profiles

NTSC uses945000000/44 master clocks per second; PAL uses21281370/1. Both use
nominal independent APU oscillator24576000/1 Hz, accumulated with integer128-bit
intermediates. This provides scheduling ticks only; no SPC700 or DSP executes.
Actual crystal tolerance has not been measured.

Normal lines contain1364 clocks. NTSC non-interlace field1 line240 contains1360;
PAL interlace field1 line311 contains1368. Interlace field0 has one extra line.
Normal dots323 and327 have six clocks; other dots have four. The short line uses
340 four-clock dots. Refresh reserves40 clocks, first at H538, then at the
free-running global phase2 mod8 nearest H536 (tie toward earlier time). CPU
internal cycles cost6 clocks; selected memory access ranges cost6/8/12 clocks,
including MEMSEL-controlled FastROM in high ROM banks and always-slow WRAM.

The epoch is explicitly H0/V0/field0 **after deterministic convenience reset**.
Reset register seeds retain M1's deterministic convention. This does not claim
measured physical reset-release alignment, power-on randomness or warm-reset
behavior. Those distinctions remain open for the later I/O/revision/product
and hardware-evidence work (M3/M7/M9/M8).

Interrupt inputs latch NMI edges and IRQ levels. The sample just before the last
instruction cycle determines the next boundary entry. CLI/SEI/PLP/REP/SEP use old
I for that sample. RTI restores I after the status pull while M/X output pins
retain the corpus-observed transition timing. NMI wins arbitration; masked IRQ
wakes WAI; wake completion consumes two internal cycles. Detailed 5A22 timer
register IRQ generation, DMA overlap and related I/O edge behavior remain M3.

## Read-phase correction and tracing

The initial M2 implementation incorrectly latched every read at cycle completion.
The full generic65816 corpus could not detect this because its rows describe bus
cycles without SNES master-clock sample phases. That model is superseded by the
read/hold split above; earlier timing acceptance was pending and did not establish
these phases. The correction preserves the independent generic cycle sequences.

`SnesSystem.CyclePhaseObserved` emits `read-latch`, `write-latch` and
`cycle-complete` records. Each carries a master timestamp, cycle start, nullable
latch timestamp and the actual cycle value/pins. Headless `.bus.jsonl` uses these
explicit phase records. A clock budget ending during hold includes its latch
record; continuation later emits completion without another latch. Generic
`Cpu65816.CycleObserved` remains one electrical row per access (at the effect),
with no SNES time implied when the standalone adapter is used.

The absolute split follows the independently inspected bsnes memory-access
reference at revision `78474e5d39f58872dc21aca18cf9eb583b147fe7`: read sampling after2/4/8
clocks and four trailing clocks, versus writes after6/8/12 clocks. Anomie's
read/write counter-latch discussion independently describes a one-dot (four
master clocks) offset, but does not by itself specify every absolute bus phase.
This is a source-informed analytical target, not an acquired hardware trace.
No implementation source was copied.

Precise MDR/Open Bus remains M3: the reference preserves MDR on internal CPU
reads in banks00–3F/80–BF at4000–43FF, while the current bus still updates its
legacy OpenBus there. The read/hold correction does not claim to resolve MDR.

## Independent sources

- [WDC W65C816S datasheet](https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf),
  March13,2024: generic instruction cycles, valid-address pins, stack/reset and
  interrupt behavior. Generic WDC behavior is not itself 5A22 hardware evidence.
- [Pinned SingleStepTests README and vectors](https://github.com/SingleStepTests/65816/tree/307c63f5673c8c6b861c52f71ddb89212a227480):
  independent generated register/RAM expectations and complete bus rows. A null
  read value has no selected memory, and must not cause a real read.
- [Anomie's timing document](https://wiki.superfamicom.org/timing), CPU clocks,
  refresh and interrupt sections: variable access clocks, refresh divider,
  last-cycle IRQ sampling, old-I versus RTI behavior and wake cycles. Published
  research informs the model; this project has acquired no original-console
  measurements. Some source sections explicitly describe conjectures.
- [Pinned bsnes memory access reference](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/cpu/memory.cpp):
  independently inspected read latch/hold versus write endpoint phases; comparison
  implementation, with precise MDR exceptions deferred to M3.
- [SNESdev timing](https://snes.nesdev.org/wiki/Timing): rational master clocks,
  PAL/NTSC short/long scanlines and interlace geometry.

No external emulator implementation was transplanted. Source analysis and
external expected data remain separate from the independently written C# core.

## Measured scope and remaining work

The gate runs all5,080,400 vectors in512 immutable full files twice: semantic
state and exact electrical bus rows. The sample's first vector in every opcode/
mode file also runs through `AdvanceCycle()` against the independent expectations.
Twenty-one timing regression groups exercise the profile map, two emulated hours
(one per profile), event boundaries, refresh endpoints, every-master-clock
save/load near instructions/refresh/field boundaries, side-effectful MMIO,
interrupt entry/polling, sleeping CPU and negative comparator mutations. Six
additional phase groups check all6/8/12-clock read sample positions and endpoint
writes, dynamic MMIO changes between read latch/completion, every-clock hold
save/load with read side effects, refresh at both phases, IRQ/NMI and refresh
collisions, and rejection of both older savestate versions.

The locally observed benchmark on NILSPC1 / Windows10.0.26200 / .NET10.0.12 was
498.15 diagnostic fields/s (178.02 million master clocks/s) with the resumable
instruction path. Standalone NOP execution reached4.86 million instructions/s;
the exception-per-cycle diagnostic API reached0.293 million instructions/s.
The diagnostic spends time in WAI; this is an idle-heavy workload. Single-cycle stepping is substantially slower and intended for bounded stepping
and tests; normal exact-budget execution does not throw per cycle. This is one
host/workload observation, not a commercial-game performance guarantee.

PPU rendering remains one whole line at a time, with host ARGB output. DMA is
still the legacy immediate transfer followed by deterministic serialized stall
debt; this is explicitly not accurate DMA timing. Full DMA/HDMA state machines,
I/O latency, auto-joy timing and timer IRQ registers are M3. There is no fake APU
sound core. Native graphics/audio and physical hardware evidence remain explicit
skipped reports. M2 does not establish complete5A22 behavior or SNES compatibility.
