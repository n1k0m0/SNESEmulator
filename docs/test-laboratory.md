# Test laboratory: M0–M7 CPU, bus, I/O, SPC700, native PPU and stereo DSP

M0 establishes measurement, not CPU or SNES accuracy. The original17 assertions
are preserved as12 portable core regressions and5 Windows/network integrations.
`SNESEmulator.Core.Tests` references only the core and portable testing library;
it does not load Windows Forms, OpenTK, the frontend, audio devices or TLS.
`SNESEmulator.Tests` retains the existing Windows frontend/transport dependency.
M1 adds12 portable semantic groups; M2 adds21 timing/continuation groups.
M3 adds21 I/O/DMA groups (66 total through M3); M0–M3 were independently
accepted (`artifacts/root-m3-validation.log`). M4 adds21 groups (87 total
portable groups); independently accepted on `0b1139436727c835c4fdbf944c74e3d0067c3944`
(`artifacts/root-m4-validation.log`). M5 adds42 PPU groups (129 total), independently accepted on
`2dc8cf021ef7394f38df1db9932b443a23543b30`, full gate exit0. M6 adds17
separately supervised DSP groups,1 AV group and3 Windows groups (8 total).
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
python tools/fetch-spc700.py --full
# Supply local original IPL input as documented in m4-apu-contract.md
python tools/test-lab.py --windows --full
```

The standard script checks the complete laboratory contract and green sampled
semantics while preserving512 unsampled markers. Its zero exit does not claim
full coverage. `--full` requires all5,080,400 cases,512 verified source files and
zero failures/skips, independently for semantics and bus conformance. Missing data is non-success. The separate full CPU CI job
fetches the pinned data and fails on missing/failed semantics or bus cycles.
Remote run [39997641997](https://github.com/n1k0m0/SNESEmulator/actions/runs/39997641997)
passed the external CPU/SPC and package jobs but failed the Windows portable
persistence assertion; the Linux matrix job was cancelled, not passed. See
[r1-persistence-ci.md](r1-persistence-ci.md) for the revised persistence proof
and current validation status. GUI/window interaction remains
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
`cycle-complete`. Valid reads latch after accessClocksâˆ’4 active clocks, followed
by four active hold clocks; writes latch at completion. The earlier all-effects-
at-endpoint model was materially incorrect and is superseded by this correction.
The exact budget can end inside a pre-latch phase, trailing read hold, refresh
stall or instruction, with serialized remaining time and captured read data.
Events at the same timestamp execute before latch/completion transitions in
priority/creation order. CPU architectural registers commit at
instruction boundaries; cycle rows carry the in-flight pin values. Partial
frames remain partial and completed-field count is separate. M5 replaces whole-line PPU output with explicit fetch/pixel phases and native RGB555. M3 replaces immediate DMA/stall debt with
serializable DMA/HDMA bus ownership and phase machines; see the M3 contract. M4 adds the own SPC700, real IPL execution and clocked MMIO; see its contract.
S-DSP and native audio samples remain absent. These limitations remain visible in hardware/graphics/
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


## M4 SPC700 and real IPL gate

See [M4 contract](m4-apu-contract.md) for timing, primary research and limits.
`fetch-spc700.py --full` downloads the immutable MIT corpus: 256 files,
256000 vectors, 83786798 JSON bytes. `--verify` checks hashes/counts. Samples
contain1024 cases and256 explicit unsampled markers. Full manifests are
source-controlled; full data and firmware stay ignored. Firmware is never
downloaded by the gate.

`spc-full artifacts/spc testdata/spc700-full` verifies semantic states and
all electrical rows, reporting rawBusPassed249000, rawBusMismatches7000,
sourceAdjudicatedCases7000 and approvedBusPassed256000. Seven indirect-Y read
opcodes have a documented source-informed ordering adjudication. Raw vectors
remain unchanged; first raw differences are retained. This does not claim
that all raw JSMoo bus rows match.

`spc-boot-original artifacts/original [local-ipl-path]` verifies the known
original firmware hash, actual65816 upload/SPC execution, same-firmware state
continuation, foreign-firmware rejection and1048-byte multiblock transfers.
Missing original IPL returns skipped/exit2. Full mode requires this evidence.
The own replacement boot routine has its own real-execution/source tests.
Both boot paths exercise8-bit index wrap, destination page crossing, multiple
blocks and command-counter zero avoidance. Neither has host handshake code.

Continuations are tested at every crystal/master clock in bounded windows
against an untouched continuous run. Full-system byte comparisons include
CPU, ports/RAM, timers, DMA, PPU and events. Public-observer tests force exact
event ties using an explicitly SYNTHETIC oscillator ratio, with20 asserted
capture points around DMA, IRQ, refresh, line end and VBlank/NMI. These are
analytical scheduling fixtures, not console frequencies. Normal NTSC/PAL
rational clock and one-clock chunking run separately. Eight comparator
mutations use the real verifiers. M3 constant APU-pin fixtures explicitly
freeze the APU so their independent expected values stay valid.


The clean M4 implementation gate passed on
`bb1aaa9c2dc1dac7850ea2d12a71b73c9d05c079` with exit0:
`artifacts/m4-full-validation.log`. All87 portable and5 Windows groups passed;
all5080400 CPU semantic cases,5080400 CPU bus cases,256000 SPC semantic cases
and256000 source-adjudicated SPC bus cases ran. Original IPL checks4/4 passed.
The raw SPC split remains249000/7000. Structured clean reports are preserved
under `artifacts/m4-implementation-evidence/`; counts/hashes and limits are
recorded in [M4 baseline](baselines/m4.json). Independent parent acceptance completed on `0b1139436727c835c4fdbf944c74e3d0067c3944`,
`artifacts/root-m4-validation.log`, full gate exit0.


## M5 native PPU gate

The [PPU contract](m5-ppu-contract.md) documents the source-informed two-chip
Mode0/1 target, MMIO/latches, all128 OBJ, main/sub windows/math, mosaic, analytical
fetch deadlines and the unverified absolute hardware epoch. Format6 saves the
in-flight graphics pipeline and rejects formats1–5. 334 every-master-clock
continuations compare to untouched uninterrupted reference runs, including actual
DMA/HDMA/IRQ/active APU and timed transfer traces. Public fetch/pixel/APU observer
snapshots exercise committed-state capture. Literal worksheet expectations and
fresh negative native-pixel mutations use the same comparator. Lab files include
RGB555LE, separate brightness bytes and ARGB. `rom --expect-native` reports the
first wrong pixel/hardware line and register/bus context.

Original Apache-2.0 CPU test-ROM variants are assembled during the tests; executed
unique hashes appear in Metadata.M5. Generator, worksheets and one unmodified
Zlib external VRAM mid-scanline ROM/license/source are LF/hash-pinned by
`testdata/m5-ppu-manifest.json`. The external program runs24 actual fields and
repeated active VRAM write loops, with an independent source-derived full-white
native frame oracle. `tools/rebuild-ppu-fixture.py` reproduced the ROM exactly.
This is one external software test, not broad M8 hardware certification. No own
console is available. Modes2–7 and variable geometry remain M7; audio remains M6.

The clean M5 full gate passed on `9e5a7817af00428915e36f1171d748577e28953e`,
exit0, `artifacts/m5-full-validation.log`:129 portable groups,5 Windows groups,
11 lab mutations,5,080,400 CPU semantic cases and5,080,400 CPU bus cases
(26,184,846 rows),256,000 SPC semantic/approved bus cases (1,182,940 rows).
Raw SPC results remain249,000 pass/7,000 explicitly source-adjudicated mismatches.
Original local IPL4/4 passed. All corresponding reports have an empty working-tree
status; unmodified copies are under `artifacts/m5-implementation-evidence/`.
Counts, hashes, all129 group durations and114 executed original PPU ROM hashes
are in [M5 baseline](baselines/m5.json). The external ROM executed86,016 active
VRAM writes and its full57344pixel independent white oracle passed; the standalone
native CLI oracle also passed. All five UI smoke scenes were awaited sequentially,
inspected and saved under `artifacts/m5-ui-*.png`, using isolated save data.

The original120second core watchdog correctly timed out; this red result remains
in `artifacts/m5-first-timeout/`. An intentional reporter-instrumentation restart
is separately archived. The completed core suite took126.56seconds with a240second
watchdog; no test was skipped to make the gate pass. The active APU+M5 PPU benchmark
measured21.62 diagnostic fields/s (7.72million master clocks/s) on NILSPC1,
.NET10.0.12, all accuracy features enabled. M9 must improve this below-realtime
workload while preserving digital results. Independent parent M5 acceptance passed on2dc8cf, full gate exit0,
`artifacts/root-m5-validation.log`; the evidence remains analytical/software-derived, not a console capture.


## M6 native DSP and stereo gate

The [M6 contract](m6-dsp-contract.md) specifies the own32-phase DSP, rational
crystal integration, native output, format7 and the lossless stereo protocol2.
`tools/dsp-reference.py --verify` checks71 independently rendered fixture pairs
against the pinned external LGPL accurate DSP. `--rebuild` alone acquires/builds
the separate reference; the product has no reference-core dependency. Hardware
coefficient facts also match the pinned ISC formula via
`tools/verify-dsp-gaussian.py`; no host floating point enters the hardware core.
Source generator/adapter identities and compressed binary fixtures use fixed
bytes across checkouts. Extending the own M4 uploader preserves both default
ROM images; its source pin and the format-rejection test source pin advance,
without changing previous correctness expectations.

`Core.Tests --dsp-only` runs17 targeted groups, including453726 independent
stereo pairs, final RAM/registers,262144 per-clock register observations and2048
ARAM digests. It separately exercises768 every-crystal and96 every-masterclock
continuations plus106 full-console DSP observer snapshots at real/synthetic
DMA/IRQ/PPU/event collisions. Six digital-output and four phase-bus mutations
are rejected. `--dsp-long` independently runs ten emulated seconds each of
NTSC/PAL with exact samples/crystals/fields, bounded queues, pause and reset.
These suites have their own120/180second supervision; the129 older core groups
keep their240second watchdog. Windows now has8 groups, with lossless stereo
bounds, resampler/volume/history-reset, two TLS clients and relay PCM delivery.
The full gate executes all of them and still requires the original local IPL.

Headless `rom` always streams `PREFIX.stereo-pcm16le.bin` (interleaved signed16
little endian) while draining the bounded presentation queue at event boundaries.
The report records32000Hz/two channels, first/last index/crystal, pair count,
drops and SHA256. `PREFIX.dsp.jsonl` records up to `--trace-limit` committed
DSP phases with voice/stage and RAM context. `--expect-audio FILE` compares exact
raw stereo and reports the first sample/channel/crystal. Supply independent
oracle provenance; replay equality is a determinism check, never a golden.

```powershell
dotnet run --project SNESEmulator.Lab -c Release -- rom --diagnostic audio --frames 8 --output artifacts/audio-diagnostic
# Other original/local ROMs use --rom path, with the same native dump format.
python tools/test-lab.py --windows --full
```

`benchmark-apu` remains the active Mode0/1 graphics workload used in M5 and now
also advances the DSP (default silent voices). `benchmark-dsp` separately
measures actual two-voice BRR playback with forced-blank PPU. Their scores are
not interchangeable. Both retain all digital accuracy features. Published
hardware research and the external digital oracle support this implementation;
original console audio captures, rapid DAC mute/reset pin behaviour and
revision-specific electrical measurements remain unperformed evidence.


The clean M6 full gate passed on `feb978b22ed73ecef9237146bbfad760ddec3a92`,
exit0, `artifacts/m6-full-validation.log`. All147 portable groups and8 Windows
groups passed, as did the full CPU/SPC corpora and original local IPL4/4.
Raw SPC results remain249000 pass/7000 explicit source adjudications.208
unmodified artifacts are frozen in `artifacts/m6-implementation-evidence/`;
all reports that record provenance have that commit and an empty working tree.
The frozen manifest hashes the logs, native dumps, traces and structured results.
Full counts, group durations and both distinct benchmark workloads are in
[the M6 baseline](baselines/m6.json). Independent M6 parent acceptance passed on `e4645770cea052885808ca5b27a45d60de470157`, full gate exit0, `artifacts/root-m6-validation.log`; unchanged reports in `artifacts/root-m6-evidence/`.


## M7 native video and variable raw fields

The separate180-second video supervisor runs35 original-ROM/worksheet groups,
106 unique ROM variants and164 full-console continuations. Together with129
existing core,17 DSP and1 AV group, this is182 portable groups; Windows now
has10 groups. Full native fields compare every RGB555/brightness sample and
geometry, including parity, display/timing interlace flags and rational domains.
No image is rescaled for native comparison. Published source and immutable
comparison identities: `testdata/m7-video-manifest.json`; [contract](m7-video-contract.md).

`tools/video-mutations.py` actually builds four modified PPU implementations
and requires targeted native ROM assertions to fail, then restores exact bytes
and rebuilds. These intentional dirty/red reports are negative evidence, not
claimed clean product passes. The first ineffective start-height mutation is
retained separately and does not count as detection. `tools/test-video-cli.py`
checks actual `--expect-geometry` and `--expect-brightness` CLI paths using
literal reset metadata/zero brightness at1000 clocks, plus parity/brightness
mutations. Both scripts are permanent parts of `tools/test-lab.py`.

The clean full gate on7cf7cc1 passed with exit0, including the complete CPU/SPC
corpora, original local IPL and unchanged DSP references. Two post-gate adapter
corrections change only the format8 error message and Lab option whitelist;
42 existing PPU/format groups and the four actual CLI controls passed separately.
Final product/tool commit isadef880; parent independently validates that final
stand. The9 extra VID3 rational controls reject zero/negative/overflowing clock
metadata while preserving22680000000/44 exactly. All raw reports, including
earlier genuine pin/format regressions, are unchanged in
`artifacts/m7-implementation-evidence/`; [baseline](baselines/m7.json).

```powershell
python tools/verify-video-testdata.py
dotnet run --project SNESEmulator.Core.Tests -c Release -- --video-only
python tools/test-video-cli.py
python tools/video-mutations.py
python tools/test-lab.py --windows --full
```

Raw-field metadata is emitted alongside `.rgb555le.bin`, `.brightness.bin` and
`.argb32le.bin`. `--expect-geometry <json>` compares exact geometry/time metadata;
`--expect-brightness <file>` reports the first brightness pixel difference.
`--expect-native` retains first native pixel/line and register context. These
are independent oracles only when their inputs are independently derived.
Hardware inventory remains explicitly incomplete: absolute PPU origin, Mode7
VRAM subslots, the first hires half-pixel and published physical INIDISP glitches
have no new original-console measurement. M7 independently accepted on `439a655199647f325bef6b6b3074e67948a105dc`, full Windows gate exit0, `artifacts/root-m7-validation.log`; unchanged evidence in `artifacts/root-m7-evidence/`.


## M8 offline console comparisons and states

The [M8 contract](m8-compatibility-contract.md) describes 20 external test programs built without changes, an external HiROM tech demo with actual audio output, separate own/original IPL datasets and the complete independent Cycle-PPU console adapters. `tools/test-lab.py` runs `verify-m8-testdata.py`, `Core.Tests --compatibility-only` under its own 360 s watchdog and both `m8-console-suite.py --verify` variants. Each variant has an overall limit of 600 s and a child-process limit of 120 s. The own-IPL dataset runs in CI without firmware, WSL or network access. If the local original IPL is missing, its gate remains visibly skipped; full mode requires it to run.

27 additional groups check external snapshot continuations, title music with actual audio output, gameplay/input/SRAM, remaining WAI phases, MMIO endpoints, pending-H0 observers and six published frequency parameters. Large game-state cases do not extend the earlier 240 s core watchdog. Image comparisons check native RGB555/brightness and parity at fixed, justified coordinates. Audio differences within the shared sample sequence remain failures even when recording endpoints differ. The [title matrix](m8-title-matrix.md), [hardware ledger](../testdata/m8-hardware-evidence.json) and separate `accuracy.json` reports document the limits.

`--accuracy-only` preserves the failing accuracy exit status. `--verify` only checks that manually reviewed differences are unchanged; it does not turn known errors into passing accuracy cases. Negative controls trigger actual pixel/channel/coordinate/sample errors as well as missing data, crashes and timeouts. Reference programs and overlays are fully pinned; freely distributable fixtures have their hashes checked offline. No current output from this emulator serves as an independent expected image. Historical failing development findings remain in the M8 evidence.

M8 corrects the earlier M2 test that expected the WAI bug as zero-idle behavior, as well as M7's interpretation of live SETINI. The corrected expectations come from both sources and actually executed ROM/bus/field observers. Format 9 rejects older saves that cannot be migrated completely. M6's isolated DSP conformance remains a different type of evidence from whole-console audio.


M8 implementation validation is complete on clean `47f91cb`: full Windows gate
Exit0,209 portable groups and10 Windows groups. The114 actual whole-console
checkpoints generate228 exact native image comparisons and20 exact SRAM
comparisons.18 audio rows remain explicitly red; verified difference contracts
are separate from accuracy. Four82/168-field repros and eight isolated scheduler
parameter runs are frozen with the rest of the evidence. Runtime thread values
differ from nominal reference frequencies; the tested rounding variants did not
change the first divergence, and ares's SMP/DSP scalar tie separation is undecided.
See [M8 baseline](baselines/m8.json) and its7724-file/12-archive hashmanifest.
Only documentation follows that code/data commit; independent parent acceptance
remains pending. No current release package or new hardware certificate is implied.


## M9a performance regression

`tools/test-lab.py` additionally invokes `tools/test-performance.py`:18952 frozen
pre-M9 SPC suspension cases,128 field-horizon comparisons,8 derived-cache cases,
40 BigInteger clock cases and14 whole-console untraced/traced/device-observer
comparisons. These prior-core regressions do not change independent hardware or
software accuracy evidence. Exact benchmark boundaries, extended/sustained flags,
20-byte timestamped PCM records and limitations: [contract](m9-performance-contract.md).

M9a measured result on clean `2dbeb5f`: the full gate passes, including14 exact
output cases. The extra realtime gate fails:37000 NTSC game fields average
1.025350x but38/62 consecutive600-field windows fall below1x (minimum0.882206x).
PAL sustained did not run after that assertion. This is an open performance
failure; see [the contract](m9-performance-contract.md) and frozen partial
[baseline](baselines/m9a.json). No failed report or expected value was replaced.

## M9b portable host/runtime and actual native app

`Core.Tests --runtime-only` runs the separate deterministic worker/input/clock/
mailbox/presentation regression. `test-lab.py --windows` also invokes the native
`tools/test-ui-runtime.py` probe in a fresh isolated output directory. Its app
key callbacks are synthetic; actual GL windows and state I/O are exercised.
`--seconds60` adds periodic real app masterclock/wall/lateness/queue/audio rows.
The initial development run caught up initial JIT debt with five device buffer
overflows; raw findings are retained. Catchup audio is now explicitly muted
above100ms without skipping hardware. Full contracts and open physical OS-focus/
gamepad, sustained NTSC/PAL and hours-long soak gates: [M9b](m9-worker-contract.md).


M9e adds the separate WPF Manager, portable bounded user-local IPC, SQLite library metadata and State9 management. The final implementation gate and independent acceptance remain separate. See [Manager contract](m9-manager-contract.md); distribution/CI, sustained performance and prolonged soak remain open.

## M9g input and presentation checks

[Baseline](baselines/m9g.json): pre-UI full Windows gate7effa47 passed285 portable/31 Windows groups. Final WPF-only source2ab409d passed Manager12 portable, complete32 native Windows groups and151 separate interaction stages with1194 exact before/after identities; the subsequent independent Root gate failed the software A/V bound (followup below). Ubuntu WSL59 is targeted portability, not another full Linux corpus gate. Linux settings busy/ENOENT observations remain unresolved. Input snapshots/callbacks are synthetic through actual native windows; physical controllers and OS focus remain unmeasured.

M9g independent Root result on8daf478:285 portable and31/32 Windows groups plus151 interaction stages passed; cumulative49.942ms software A/V distance failed the unchanged40ms bound. No overall pass. [Followup](m9g-followup.md): label151 and15 portable network groups underWindows, two isolated30s readiness failures, one new instrumented13-group pass with separately retained cold/measurement data. No new Linux or full-corpus run; no selection/metric/threshold change and no inferred cause or fix for the Root failure.
