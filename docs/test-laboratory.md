# Test laboratory: M0–M3 CPU, bus, timing and 5A22 I/O

M0 establishes measurement, not CPU or SNES accuracy. The original17 assertions
are preserved as12 portable core regressions and5 Windows/network integrations.
`SNESEmulator.Core.Tests` references only the core and portable testing library;
it does not load Windows Forms, OpenTK, the frontend, audio devices or TLS.
`SNESEmulator.Tests` retains the existing Windows frontend/transport dependency.
M1 adds12 portable semantic groups; M2 adds21 timing/continuation groups.
M3 adds21 I/O/DMA groups (66 total portable tests); independent M3 acceptance
remains pending. M0–M2 were independently accepted by the parent full gate.
No sibling C64 repository is modified.

## Reproduce locally

Requires .NET10 SDK and Python3 (standard library only). Run from repository root:

```powershell
python tools/test-lab.py --windows
# portable core, fixtures, replay, reporting, watchdog and explicit sample inventory:
python tools/test-lab.py
# sample inventory exits2 because full-suite cases are explicitly unexecuted:
python tools/test-lab.py --inventory-only
# all5,080,400 pinned cases; fetch once, then entirely offline:
python tools/fetch-full-testdata.py
python tools/test-lab.py --windows --full
```

The standard script checks the complete laboratory contract and green sampled
semantics while preserving512 unsampled markers. Its zero exit does not claim
full coverage. `--full` requires all5,080,400 cases,512 verified source files and
zero failures/skips, independently for semantics and bus conformance. Missing data is non-success. The separate full CPU CI job
fetches the pinned data and fails on missing/failed semantics or bus cycles; remote CI has not
been executed because no remote is configured. GUI/window interaction remains
outside these automated cases.

Individual commands:

```powershell
dotnet run --project SNESEmulator.Core.Tests -c Release
dotnet run --project SNESEmulator.Tests -c Release
dotnet run --project SNESEmulator.Lab -c Release -- cpu
dotnet run --project SNESEmulator.Lab -c Release -- cpu artifacts/one testdata/65816 "67 n 3"
dotnet run --project SNESEmulator.Lab -c Release -- mutations
```

Build first, then use the process watchdog for ROMs or arbitrary suites. Use
`dotnet` plus DLL paths instead of `dotnet run` inside the watchdog to keep build
latency out of test limits:

```powershell
dotnet build SNESEmulator.Lab -c Release
dotnet SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll supervise 120 artifacts/demo dotnet SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll rom --frames 8 --masterclocks 10000000 --replay testdata/replay-example.json
```

The supervisor launches a child, captures stdout/stderr, kills its process tree
at the wall-clock deadline and writes its own result even if the child crashes
or hangs in one instruction. Child reports use `SNES_LAB_CHILD_REPORT`; the CLI's
default output respects it. If specifying an explicit child prefix, use exactly
`<supervisor-prefix>-child`. A missing child report is a crash, never success.
Stale reports are removed before launch. Direct `rom`/`cpu` commands are useful
for debugging but have no process isolation; use `supervise` for unattended work.

## Result contract

`<prefix>.json` (`snes-lab-results-v1`) and `<prefix>.junit.xml` are written for
every suite. JSON distinguishes `pass`, `fail`, `skipped`, `timeout`, `crash`.
JUnit maps failures to `failure`, timeouts/crashes to `error type=...`, and missing
data to `skipped`; per-case `status` is also a property. Exit codes are0 for
entirely passing execution,1 for a failure/timeout/crash,2 for skipped/unexecuted
cases without harder failures. Reports record commit AND `git status --porcelain`
so an uncommitted run is distinguishable from a clean commit. Artifact timestamps
and host durations are metadata, not inputs to emulation.

`mutations` checks exact first-divergence text for an incorrect accumulator and
an incorrect2x2 pixel array, and confirms that the persisted rejected-oracle
report contains failures. It also checks an unexpected changed-memory write.
The deliberately rejected report is expected red; the mutation-check suite is
green only when it detected those errors. `tools/test-lab.py` independently
asserts all five process classifications, including a real killed infinite wait
and a child exiting23 without producing a report.

## Pinned external baseline

`testdata/manifest.json` pins SingleStepTests/65816 at commit
`307c63f5673c8c6b861c52f71ddb89212a227480`, version`v1`, MIT license
(`testdata/65816/upstream-LICENCE`). Source README is retained verbatim. Source:
https://github.com/SingleStepTests/65816/tree/307c63f5673c8c6b861c52f71ddb89212a227480

All256 opcodes and both e/n source files are represented:512 files, first4
vectors in each,2048 of5,080,400 upstream cases. This is a small fixed sample,
NOT complete opcode semantics coverage. Four block-move source files contain100
cases each; other source files contain10,000 each. Each file reports its exact
unselected count as a separate skipped inventory item. Missing files or missing
manifest entries are also explicit skips and non-success; hash/count mismatches
are failures. A nonexistent name filter returns skipped instead of a vacuous pass.

`python tools/fetch-testdata.py --verify` verifies offline fixture hashes.
`python tools/fetch-testdata.py` deliberately reconstructs the same sample from
immutable source URLs; review changes before accepting them. It streams only
enough prefix bytes for four complete vectors, records prefix byte count/hash,
extracts objects without changing values and normalizes JSON formatting. Fixture
SHA-256, immutable source URL/commit, mode, selection, license, initialization,
seed policy, success signal and timeout are recorded. Prefix SHA is NOT the
SHA of a complete upstream file. No multi-GB upstream checkout is redistributed.

The adapter assigns arbitrary raw initial CPU registers without reset/normalizing
them, fills specified flat24-bit RAM and zeros unspecified RAM. It checks exact
final registers/status and listed RAM; any unexpected write that changes another
address is also rejected. For MVN/MVP, upstream expects a complete block move,
while this core exposes one byte per `Step`: the adapter repeats only those
opcodes until PC advances, with a65537-iteration ceiling. M1 corrects CPU semantics separately from the adapter conventions described below.
M2 compares complete cycle rows independently, including dummy cycles, address, nullable data, direction and all pins. SNES clock durations are separately checked against analytical profiles. These vectors are independently generated
software expectations, not an original-SNES hardware measurement.

Frozen sampled results and first differences are in`docs/baselines/m0.json`.
No expected values or frames are derived from the current emulator and promoted
into correctness oracles. Replay equality is explicitly an internal determinism
check. The synthetic black pixel oracle checks the comparator, not the PPU.

## M1 full semantic gate and provenance

All5,080,400 vectors from all512 pinned files pass, covering every opcode in
both modes. The acceptance result is recorded compactly in
[baselines/m1.json](baselines/m1.json). M0's historical baseline is unchanged.
No expected values were altered and no cases/opcode groups were excluded.

`tools/fetch-full-testdata.py` downloads unchanged files at the immutable commit
into ignored `testdata/65816-full/`; it reuses existing files offline and checks
all hashes against reviewed `testdata/65816-full-manifest.json`. The runner
requires that reviewed manifest, verifies complete file hashes and case counts,
streams individual vectors, and reuses flat RAM with sparse clearing. Full reports
store per-opcode/mode pass/fail counts and first failure evidence, instead of
millions of passing records. Each group has a30-second deadline; `test-lab.py`
adds a240-second process watchdog. Failure evidence includes raw and effective
initial registers, E/M/X, expected/actual registers and first difference. A
single full-file group can be reproduced using
`cpu-full artifacts/one testdata/65816-full ab.e`.

The upstream generator supplies arbitrary high S bytes for E=1, including
instructions that do not access the stack, but its final values consistently
canonicalize S to page1. These initial states cannot exist on hardware. The
adapter explicitly maps only E-mode initial S to `0x100 | (S & 255)` before
execution; raw inputs remain unmodified in evidence and fixtures. This is a
corpus-to-valid-state convention, not a side effect attributed to LDA. No global
P/X/Y normalization occurs. WDC documents page1 stack addressing in emulation
mode and the S-high change on XCE (sections2.8 and7.10 of the
[W65C816S datasheet](https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf)).
The corpus README's illustrative older example does not canonicalize S; the
pinned actual vector files are the test inputs and their convention is reported
explicitly here.

Core fixes include width-limited block-move index updates, ADC decimal overflow
from the intermediate high-digit sum, full24-bit data second-byte carry while
Direct/stack operands remain in bank0, and extended emulation stack accesses
before restoring the stack page. WDC's stack caveats explain the extended
PHD/PLD/JSL/RTL/PEA/PEI/PER/JSR(a,x) behavior. PLB at S=$01FF is also covered by
the independent pinned vectors (`ab e 75` reads $0200 then leaves S=$0100);
this is software-reference evidence, not a new original-SNES hardware claim.
The8 reduced source failures in `testdata/m1-regressions.json` retain verbatim
initial/final values and are permanently exercised offline.

The12 added portable groups cover E/M/X transitions, hidden A,8/16-bit BCD,
Direct/indexed/long and bank boundaries, normal/extended stack wrapping, RMW,
both block moves, BRK/COP/RTI, Reset, IRQ/NMI priority, masked IRQ waking WAI,
and STP requiring Reset. A saved seed `0x65816` drives20,000 valid-BCD ADC/SBC
checks against an independent integer-decimal oracle. The mutation suite now
also changes an LDA instruction into LDX and rejects it against an independently
specified register oracle. The original12 portable plus5 Windows regressions
remain green.

`Irq()` and `ServiceInterrupts()` provide instruction-boundary entry/arbitration.
They do not implement interrupt-pin sampling/latches or polling-cycle timing.
The historical M1 acceptance did not validate bus/dummy-cycle order, pins or clock durations; the M2 section below records the additional checks. Full DMA/raster interactions remain M3.
M1 establishes register/memory semantics for a generic65816, with deterministic
test initialization; power-on register randomness, original5A22 measurements and
commercial-game compatibility remain outside this acceptance.

## M2 bus and timing gate

`cpu-bus` and `cpu-bus-full` run the immutable corpus through the same independent
C# CPU, comparing every upstream `cycles` row. The full gate checks5,080,400
vectors in512 hash-verified files, separately from the unchanged semantic gate.
All addresses, order, direction, values (including null idle data), VDA/VPA/VPB,
E/M/X and MLB pins must match. No expected cycle drives CPU execution. The all-low
terminal WAI/STP row is a quiescent marker with zero hardware-clock duration.
These are external software-generated generic65816 expectations, not a measured
5A22 trace. Source pin semantics are specified in the retained upstream README.

The45 portable cases include21 M2 groups: exact mapped access speeds; NTSC/PAL
fields/interlace/short/long lines and dots; refresh phase; one emulated hour per
profile with exact rational APU accumulation; stable event ordering; refresh at
and within a CPU cycle; one-master-clock versus block versus frame complete
state equality; save/load at192 instruction/refresh/frame boundary clock points;
side-effectful MMIO and every-cycle continuation; old-I IRQ polling and NMI/WAI
priority; RTI restored-I polling in both modes; WAI/STP clock suspension; every
cycle of reset/IRQ/NMI entry; negative cycle/address/value/direction/pin mutations;
and the first independent vector in all512 opcode/mode files through the
single-cycle API. The mutation command separately reports five bus mutations in
addition to its six existing checks (11 total). Version3 savestates include all
clock events, rational remainder, partial CPU-cycle time, continuation and
interrupt latches, captured read data and four-clock trailing hold. Versions1/2
states are explicitly rejected. Additional tests change MMIO after read latch,
save/load during each hold clock, and collide interrupts/refresh at both phases.

The original pending measurement is retained in [baselines/m2.json](baselines/m2.json).
The corrected read-phase gate supersedes its timing model and is recorded in
[baselines/m2-read-phases.json](baselines/m2-read-phases.json).
Read [the timing contract](m2-timing-contract.md) for the execution model,
reference sources, profile epoch and remaining device limitations. The concrete
2/1/3 separate-chip target and nominal oscillators remain unverified hardware.
Physical reset-release alignment and warm-reset/power-on distinctions have not
been measured. The M2 profile epoch is H0/V0/field0 after convenience reset.

```powershell
dotnet run --project SNESEmulator.Lab -c Release -- cpu-bus
dotnet run --project SNESEmulator.Lab -c Release -- cpu-bus-full artifacts/bus testdata/65816-full
dotnet run --project SNESEmulator.Lab -c Release -- benchmark
```

## M3 I/O and transfer gate

Read [the M3 contract](m3-io-contract.md) for the exact analytical behavior,
source revisions and remaining PPU/electrical/hardware evidence limits.
`IoDmaTests` adds21 groups to the existing45: normal extended mapper metadata,
manual region/profile choice, recursive mirroring, SRAM retention, CPU MDR and
MMIO masks, all DMA patterns/directions and65536-byte counter, invalid A/B ranges,
WRAM conflicts, HDMA setup/direct/indirect/repeat/two-pass order and preemption,
ALU intermediate states and real CPU edge placement, H/V/HV IRQ and NMI holds,
counter latches, serial signatures and all four automatic JOY result lines.

The original `IoDmaDiagnosticRom` generator executes80 CPU cases (8 modes ×2
bus directions ×5 raster start classes), with WRAM signatures and protocol
oracles independent of the incomplete PPU. Refresh integration asserts a real
transfer straddling refresh, HBlank/VBlank use actual polling, and timer cases
execute/acknowledge the IRQ vector. This is original test software, not a
hardware-certified third-party suite. No licensed external M3 ROM was acquired.

Every-clock save/load comparisons cover1260 bus-idle DMA,181 running-CPU DMA,
and301 HDMA setup/line checkpoints against uninterrupted end states. Another200
checkpoints cover E-RMW internal/valid-write ALU edges and colliding refresh. Public phase
observer snapshots verify no repeated2180/table/indirect effects. ALU and auto-joy
states have additional edge checkpoints. Three negative clock/channel/target
mutations use the same positive protocol verifier, in addition to the unchanged11
laboratory mutations. Old immediate-result Foundation tests now advance the real
ALU/DMA/JOY state before the unchanged final expectations. M2 system IRQ injection
uses the external bus IRQ wire because SnesSystem now owns CPU line wiring.

Savestate4 stores all M3 intermediate state, cartridge options/mapper and the
auto/forced profile choice. Same-ROM wrong-mapper loads and versions1–3 are
rejected. PPU/APU convenience reset and electrical effects explicitly deferred
in the M3 contract are not claimed physically accurate.

The clean implementation gate and report hashes are recorded in
[baselines/m3.json](baselines/m3.json); independent parent review is pending.

## Headless limits, replay and artifacts

`rom` defaults to the original diagnostic ROM. Options require key/value pairs:

- `--rom path` selects a local cartridge; commercial ROMs remain local.
- `--frames N` and `--masterclocks N` are positive execution ceilings; the first
  reached wins. Default limits are8 fields and100,000,000 exact master clocks.
- `--replay path` loads JSON events `{ "Frame":0,"Port":0,"Buttons":256 }`.
  Events are applied at the requested field boundary, sorted by frame;
  ports are0/1 and Buttons uses active-high `SnesButtons` bits. Values persist
  until changed. Unreached events are counted. There is no sub-frame replay yet.
- `--trace-limit N` limits CPU/bus trace records (default4096). Truncation and
  total attempted events are reported; raster transitions are recorded separately.
- `--expect-wram path` compares JSON pairs `[offset,value]` in128KiB WRAM.
- `--expect-frame path` compares full little-endian ARGB32 words, reporting the
  first wrong pixel. Supply independently acquired reference provenance separately.
- `--output prefix` selects the artifact prefix (normally set by supervisor).

Artifacts: WRAM binary dump,256x224 ARGB32LE frame, CPU JSONL, timed CPU cycle JSONL,
raster JSONL and JSON/JUnit results. Hashes include ROM, replay, WRAM, image and
any explicit oracle files. Replay checks compare CPU state, clocks, frame/WRAM
hashes AND byte-identical CPU/bus/raster traces across two runs.

**Device limitation:** timed traces distinguish `read-latch`, `write-latch` and
`cycle-complete`. Valid reads latch after accessClocks−4 active clocks, followed
by four active hold clocks; writes latch at completion. The earlier all-effects-
at-endpoint model was materially incorrect and is superseded by this correction.
The exact budget can end inside a pre-latch phase, trailing read hold, refresh
stall or instruction, with serialized remaining time and captured read data.
Events at the same timestamp execute before latch/completion transitions in
priority/creation order. CPU architectural registers commit at
instruction boundaries; cycle rows carry the in-flight pin values. Partial
frames remain partial and completed-field count is separate. PPU rendering is
still whole-line host ARGB output. M3 replaces immediate DMA/stall debt with
serializable DMA/HDMA bus ownership and phase machines; see the M3 contract. No SPC700/DSP or
native audio samples exist. The rational oscillator counter is only the APU
scheduling foundation. These limitations remain visible in hardware/graphics/
audio evidence reports as skipped, never passing accuracy claims.

## Evidence limitations and hardware profiles

`testdata/hardware-profiles.json` explicitly marks both NTSC/PAL profiles proposed
and unverified. The current core reports CPU/PPU1/PPU2 versions2/1/3. The
proposed digital target uses those IDs with a separate CPU and two PPU ICs, for
both region models. [SNESdev revision documentation](https://snes.nesdev.org/wiki/Version_differences)
maps 5A22-02,5C77-01 and5C78-03 to those values, while explaining that version
numbers stopped changing after2/1/3. Those bits also appear on1-CHIP hardware
and do not identify an architecture. CPU A/B and PPU2 B/C distinctions and named
regional motherboard evidence remain unresolved. This is target selection, not
a verified hardware profile or proof of implemented errata. The timing constants now drive explicit analytical profiles and tests for oscillators, short/long lines, interlace and refresh. None constitutes original-console measurement evidence. Candidate revision/specification sources are
listed as research sources, not as evidence proving the implementation.

`testdata/local-manifest.json` pins original diagnostic source, replay and profile
configuration and describes analytical oracle provenance. Separate
`audio-conformance`, `graphics-conformance`, and `hardware-evidence` runs produce
explicit skipped results because no independent native samples, frame corpus or
console measurements have been acquired. These gaps block accuracy claims, not
M0's measurement infrastructure. M1/M2/M5/M6/M8 must fill their corresponding
reference and hardware evidence requirements. The original diagnostic-ROM tests
remain useful internal regressions, not proof of commercial-game compatibility.
