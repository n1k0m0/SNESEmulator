# M0 test laboratory

M0 establishes measurement, not CPU or SNES accuracy. The original17 assertions
are preserved as12 portable core regressions and5 Windows/network integrations.
`SNESEmulator.Core.Tests` references only the core and portable testing library;
it does not load Windows Forms, OpenTK, the frontend, audio devices or TLS.
`SNESEmulator.Tests` retains the existing Windows frontend/transport dependency.
No sibling C64 repository is modified.

## Reproduce locally

Requires .NET10 SDK and Python3 (standard library only). Run from repository root:

```powershell
python tools/test-lab.py --windows
# portable core, fixtures, replay, reporting, watchdog and red accuracy inventory:
python tools/test-lab.py
# strict conformance exit, currently1 because known accuracy failures:
python tools/test-lab.py --inventory-only
```

The full laboratory script checks laboratory contracts and baseline regression.
Its zero exit code DOES NOT mean external conformance passes. Conformance always
has its own JSON/JUnit reports and nonzero exit status. CI has a separate visible
`external-conformance-inventory` job with `continue-on-error` during M0; remove
that setting at M1 acceptance. Workflow definition is local; no remote or CI run
was created. GUI/window interaction remains outside these automated cases.

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
opcodes until PC advances, with a65537-iteration ceiling. No CPU semantics were
changed. Logical read/write traces are diagnostic only: dummy cycles, pins and
hardware timing are deferred to M2. These vectors are independently generated
software expectations, not an original-SNES hardware measurement.

Frozen sampled results and first differences are in`docs/baselines/m0.json`.
No expected values or frames are derived from the current emulator and promoted
into correctness oracles. Replay equality is explicitly an internal determinism
check. The synthetic black pixel oracle checks the comparator, not the PPU.

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
