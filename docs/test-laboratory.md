# Test laboratory: M0 infrastructure and M1 CPU semantics

M0 establishes measurement, not CPU or SNES accuracy. The original17 assertions
are preserved as12 portable core regressions and5 Windows/network integrations.
`SNESEmulator.Core.Tests` references only the core and portable testing library;
it does not load Windows Forms, OpenTK, the frontend, audio devices or TLS.
`SNESEmulator.Tests` retains the existing Windows frontend/transport dependency.
M1 adds12 portable semantic regression groups (24 total portable tests).
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
zero failures/skips. Missing data is non-success. The separate full CPU CI job
fetches the pinned data and fails on missing/failed semantics; remote CI has not
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
Logical read/write traces are diagnostic only: dummy cycles, pins and
hardware timing are deferred to M2. These vectors are independently generated
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
M2 must still validate exact bus/dummy-cycle address order and values, pins,
internal cycles, clock durations, interrupt polling and DMA/raster interactions.
M1 establishes register/memory semantics for a generic65816, with deterministic
test initialization; power-on register randomness, original5A22 measurements and
commercial-game compatibility remain outside this acceptance.

## Headless limits, replay and artifacts

`rom` defaults to the original diagnostic ROM. Options require key/value pairs:

- `--rom path` selects a local cartridge; commercial ROMs remain local.
- `--frames N` and `--masterclocks N` are positive execution ceilings; the first
  reached wins. Default limits are8 frames and100,000,000 approximate clocks.
- `--replay path` loads JSON events `{ "Frame":0,"Port":0,"Buttons":256 }`.
  Events are applied before the first instruction of that frame, sorted by frame;
  ports are0/1 and Buttons uses active-high `SnesButtons` bits. Values persist
  until changed. Unreached events are counted. There is no sub-frame replay yet.
- `--trace-limit N` limits CPU/bus trace records (default4096). Truncation and
  total attempted events are reported; raster transitions are recorded separately.
- `--expect-wram path` compares JSON pairs `[offset,value]` in128KiB WRAM.
- `--expect-frame path` compares full little-endian ARGB32 words, reporting the
  first wrong pixel. Supply independently acquired reference provenance separately.
- `--output prefix` selects the artifact prefix (normally set by supervisor).

Artifacts: WRAM binary dump,256x224 ARGB32LE frame, CPU JSONL, logical bus JSONL,
raster JSONL and JSON/JUnit results. Hashes include ROM, replay, WRAM, image and
any explicit oracle files. Replay checks compare CPU state, clocks, frame/WRAM
hashes AND byte-identical CPU/bus/raster traces across two runs.

**Clock limitation:** the current core remains`Step()*6` with immediate DMA debt
and1364 approximate clocks per line. The limit can overshoot by one complete
instruction and its immediate DMA charge. A partial frame is dumped as partial;
completed-frame count is separate. Trace clock labels denote instruction starts,
not individual bus-cycle timestamps. This preserves existing approximate
execution and is not M2 timing correctness. Frames use the existing whole-line
renderer and output host ARGB pixels, not native15-bit PPU output. No native audio
samples exist while SPC700/DSP are absent.

## Evidence limitations and hardware profiles

`testdata/hardware-profiles.json` explicitly marks both NTSC/PAL profiles proposed
and unverified. The current core reports CPU/PPU1/PPU2 versions2/1/3. The
proposed digital target uses those IDs with a separate CPU and two PPU ICs, for
both region models. [SNESdev revision documentation](https://snes.nesdev.org/wiki/Version_differences)
maps 5A22-02,5C77-01 and5C78-03 to those values, while explaining that version
numbers stopped changing after2/1/3. Those bits also appear on1-CHIP hardware
and do not identify an architecture. CPU A/B and PPU2 B/C distinctions and named
regional motherboard evidence remain unresolved. This is target selection, not
a verified hardware profile or proof of implemented errata. The scanline constants
describe current implementation only, without measured oscillator, short-line,
interlace or refresh evidence. Candidate revision/specification sources are
listed as research sources, not as evidence proving the implementation.

`testdata/local-manifest.json` pins original diagnostic source, replay and profile
configuration and describes analytical oracle provenance. Separate
`audio-conformance`, `graphics-conformance`, and `hardware-evidence` runs produce
explicit skipped results because no independent native samples, frame corpus or
console measurements have been acquired. These gaps block accuracy claims, not
M0's measurement infrastructure. M1/M2/M5/M6/M8 must fill their corresponding
reference and hardware evidence requirements. The original diagnostic-ROM tests
remain useful internal regressions, not proof of commercial-game compatibility.
