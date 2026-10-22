# M4 SPC700 / S-SMP contract and evidence

Own C# implementation, pending independent parent acceptance. M0–M3 were
independently accepted, most recently on `56fb6d49571875bb90f1b6b8efed49af5af2b6e7`
by `python tools/test-lab.py --windows --full`, exit0,
`artifacts/root-m3-validation.log`. This document distinguishes generated
software vectors, published hardware research and this project's analytical
tests. We own no SNES console and claim no newly acquired console measurement.

## CPU and independent corpus

`Spc700` implements all256 opcodes, flags, direct-page and stack wrapping,
word arithmetic, DIV corner cases, decimal adjust and read/write/internal
cycles. The standalone flat64KiB bus is separate from real S-SMP MMIO.
Every addressed read, including a discarded operand fetch, reaches the bus.
An upstream null read datum means unspecified data, not an invalid access.
Internal rows have no MMIO effect even where external address pins might
retain an earlier address. Raw external pin addresses for internal cycles
are not claimed; `wait` is the software-visible internal-operation category.

SingleStepTests/spc700 revision
`67d15f492b2740964abd4efd1229e0ec9c342228` supplies256 files,
1000 vectors each: **256000 semantic cases and256000 cycle cases**.
Full inputs total83786798 JSON bytes; committed
SHA-256 manifests pin the individual JSON files. MIT license is included and
hash-pinned. Four unmodified first cases per opcode are redistributed as
1024 offline samples. Seven permanent case0000 regressions are also retained;
no expected result comes from this implementation. Every sampled vector runs
through the single-cycle continuation as well as the full direct runner.

There is one explicit source-informed adjudication, `spc-indirect-y-2017-v1`.
For opcodes17/37/57/77/97/B7/F7 only, the immutable raw corpus orders
opcode,operand,**wait**,pointer-low,pointer-high,data. Published research and
the later reference implementation order opcode,operand,pointer-low,
pointer-high,**wait**,data. The comparator uses raw row order[0,1,3,4,2,5]
for precisely these seven opcodes, with exact addresses, specified values,
directions and cycle count. The hardware implementation uses the latter order.
The store D7 already has the latter order in the corpus and is not altered.

Reports retain both results: **249000 raw bus passes,7000 raw mismatches,
7000 source-adjudicated cases,256000 approved bus passes**. Each affected
opcode report retains the first raw difference, unchanged vector and actual
trace. A separate literal worksheet verifies the chosen ordering; mutations
of address, value, direction, wait placement, missing and extra cycles are
rejected by the same approved comparator. Register and unexpected RAM-write
mutations are independently rejected by the semantic comparator. No opcode
group is ignored and no raw fixture is rewritten.

The adjudication follows AWJ's June25,2017 report that Overload used a logic
analyzer, the discussed indirect-Y discrepancy and subsequent corrected
implementation. We have the public discussion, not the original capture
files. This supports a research-informed decision, not new hardware
certification. The original blargg opcode ROM discussed there was reported
lost, so we do not pretend to have executed it.

## Memory, timers and DSP interface

64KiB RAM underlies both the F0–FF register window and FFC0–FFFF IPL overlay.
Permitted writes also update underlying RAM, including I/O and IPL addresses.
TEST ram-writable and ram-disabled bits gate RAM accesses; disabled RAM reads
return5A for the selected separate-APU target. F8/F9 have auxiliary storage
which remains writable with RAM writes disabled. TEST writes are inhibited
when the SPC P flag is set. F0/F1/FA–FC read zero.

F4–F7 have four separate input and output latches. CONTROL bits4/5 clear only
CPU→APU input pairs, on every asserted write; output latches stay intact.
CPU2140–217F mirrors use those directional latches at the existing CPU/DMA bus
sample/write phases. There are no host-side AA/BB handshake handlers.

Timer0/1 share nominal128-SPC-cycle scaler periods; timer2 has16.
The scaler keeps running when a timer is disabled. A rising enable edge resets
the8bit target counter and4bit output, retaining scaler phase. Rewriting an
already enabled CONTROL bit does not reset any of the three timers. Output
increments on post-increment equality, with target0 meaning256, wraps at16
and clears on read. Target changes below the current counter wait for byte
wrap. TEST timer enable/disable gates the scaler line; falling edges count.

There is a source conflict: the pinned bsnes I/O reference has a negation in
its timer2 enable-edge reset condition. We follow Anomie's TRAC-attributed
description consistently for all three timers, corroborated by the public
research discussion. We do not copy that apparent reference regression.
The older Superfamicom SPC reference incorrectly says every CONTROL write
restarts timers; that statement is not the oracle.

F2 retains all8 address bits; F3 reads mask to7bits and writes with bit7 set
are ignored. The128-byte DSP interface retains register storage, ignores
writes to voice ENVX/OUTX and write-clears ENDX. These are interface semantics,
not an implemented S-DSP. No BRR, envelopes, DSP RAM arbitration or audio is
generated. `DspCrystalPhase` exposes the768-crystal-clock sample-period
position for M6:32 nominal SPC cycles per sample, independent of TEST waits.

## Physical time and continuation

The existing rational oscillator accumulates native24576000Hz crystal ticks
against NTSC945000000/44 or PAL21281370 master clocks, with integer128-bit
intermediates. Normal SPC cycles cost24 crystal ticks. No rounded frame rate
or host timer participates. The phase is the documented deterministic reset
epoch, not a measured physical reset-release alignment or crystal tolerance.

TEST selects external wait states for RAM and internal wait states for idle,
I/O and enabled IPL accesses. Nominal crystal lengths are[24,48,120,240]; timer
input lengths at2.048MHz are[2,4,8,16]. The two slowest settings approximate
the published glitchy behavior and cannot guarantee unpredictable real-chip
deadlock/slowdown. Separate literal tests cover all four selectors and internal
versus external accesses.

RAM, IPL and non-port I/O reads sample at the full cycle endpoint; writes
also latch at the endpoint. F4–F7 reads sample halfway, followed by trailing
hold. The read returns to instruction semantics only after hold, even when
it is the instruction's final cycle. Timers advance their selected input
clocks at each completed wait segment, before its bus effect. At arbitrary
clock budgets the uncompleted segment and remaining hold are saved; MMIO is
never executed early or replayed. Timer subcycle evolution between those
segment boundaries is not claimed as a separate acquired hardware trace.

At a shared console timestamp all typed events execute first (priority,
stable sequence), then APU effects in crystal order, then CPU/DMA bus effects.
The clock splits advances at APU boundaries. CPU/DMA active-time bookkeeping
commits each split before observation. Future typed events remain queued until
their execution time. Restoring dispatches due-now events and synchronizes the
APU before allowing a CPU bus latch.

SPC continuations contain entry registers and already committed cycles with
latched read data. Replay consumes these records, never the real bus. Public
`Apu.PhaseObserved` executes after the effect, timer/phase commit and cycle-log
append. A snapshot there can immediately resume without repeated MMIO. Private
timing hooks are independent of the public SPC trace callback. Architectural
instruction calculations are recomputed from the saved entry, and read
semantics cannot pass an unfinished port hold.

SLEEP/STOP are uninterruptible without reset in this target. They continue the
source-informed discarded-PC-read/internal-cycle pattern and timer progression;
the standalone corpus's three read/internal pairs define its bounded terminal
instruction observation. Their physical undocumented wake/reset details are
not certified. PowerOn defines reproducible RAM zero, timer target0 and outputF;
warm Reset retains APU RAM and targets, clears outputs, enables IPL and restores
the convenience scaler epoch. Undefined power-up register randomness and
physical warm-reset divider phase remain unmeasured.

Savestate **format5** stores RAM, both port directions, SPC registers and cycle
continuation, every timer stage/line, TEST/CONTROL/IPL/DSP interface, partial
wait/hold, native crystal position and firmware identity. Clock state retains
the rational remainder/profile. Formats1–4 are explicitly rejected. Loading
with a different configured IPL SHA-256 is rejected. No firmware bytes are
embedded in a savestate or automatically resolved from it.

## Executed boot code and firmware policy

The default own64-byte IPL is assembled from the independently written
`tools/own-ipl.py`; `python tools/own-ipl.py` verifies the generated bytes.
SHA-256: `18aff3c4054c3976a663520956d992011efe210c148dcc0a875f8154c0c39f05`.
It clears direct page1..EF, initializes SP, advertises AA/BB and implements the
upload/start protocol using actual SPC instructions. It has different code
layout and timing from Nintendo IPL and is identified as an open replacement.
Reentry with dirty page0 and arbitrary A/X/SP/P is tested.

The original64-byte IPL is a **local, gitignored test input**. No distribution
license has been established, so it is neither checked in nor copied to
publish output. The known original SHA-256 is
`c95f88b299030d5afa55b1031e2b5ef2dff650c4b4e6bb6f8b1359436521278f`,
matching bytes published in Anomie's SPC700 Doc revision1126, April21,2007.
This is provenance of the local input, not a distribution-rights assertion.
The local test input here is `testdata/local-firmware/ipl.bin`; its ignored
`provenance.json` records that source and hash. The test gate never downloads
firmware. Supply a lawfully obtained image explicitly:

```powershell
$env:SNESEMULATOR_IPL = (Resolve-Path testdata/local-firmware/ipl.bin)
dotnet run --project SNESEmulator.Lab -c Release -- spc-boot-original artifacts/original-ipl
Remove-Item Env:SNESEMULATOR_IPL
```

`SpcUploadRom` is an original SNES ROM whose actual65816 instructions perform
the upload protocol, start SPC RAM code, wait for its reply and complete a
port pingpong. The uploaded program writes5A to APU RAM0300, replies A5 on
port0 and returns the inverted CPU command3C as C3 on port1. Tests run this
against both the open IPL and the known original local IPL. Original IPL
instruction reads and exactly16 upload writes are observed in the short case.
A1048-byte four-block fixture additionally exercises indexFF->00, destination
page crossing, repeated blocks and command-counter zero avoidance on both IPLs. Same-firmware
state continuation succeeds; original→replacement and replacement→foreign
identity loads fail. This proves code execution and communication, not sound.

## Reproduction and evidence limits

```powershell
python tools/fetch-spc700.py --full
python tools/test-lab.py --windows --full
# Standalone independent SPC report, explicit raw/adjudicated counts:
dotnet run --project SNESEmulator.Lab -c Release -- spc-full artifacts/spc testdata/spc700-full
```

The full gate requires locally supplied original IPL; absence remains an
explicit unexecuted result and prevents full acceptance. Sample mode reports
all unsampled vectors and missing firmware as skipped. M3's constant-port
electrical fixtures now explicitly isolate/freeze the APU; coupled M4 ROMs
always execute the SPC. Expectations of M0–M3 were preserved.

Tests include fixed timer boundaries, CONTROL rewriting, read-clear/overflow,
TEST gating, boot/underlay, DSP interface, actual CPU/SPC pingpong, every-crystal
and every-master-clock save/load against an untouched continuous reference,
and full-system byte comparisons from public APU phase snapshots. Targeted
snapshots cover before/on/after refresh, line end, DMA, timer IRQ and VBlank/NMI;
the tests assert that the requested boundary snapshots actually occurred.
Synthetic exact-ratio profiles force event ties independently of the ordinary
irrational clock phase. Local measurement records are in `baselines/m4.json`.

Still open for M6/M8/M9: actual DSP/audio and RAM contention; measured physical
power/reset phase; analog or bitwise-OR CPU/SMP port collision windows; the
published TnTARGET-write race/glitch; unpredictable high-TEST-divider/revision
behavior; original logic-analyzer capture acquisition; commercial-game matrix
and speed optimization. These are explicit limits, not silently passing tests.

## Primary research and pinned comparison sources

- [SingleStepTests/spc700 immutable revision](https://github.com/SingleStepTests/spc700/tree/67d15f492b2740964abd4efd1229e0ec9c342228):
  MIT generated state/bus vectors, not independent hardware measurements.
- [Anomie's SPC700 Doc revision1126](https://github.com/gilligan/snesdev/blob/master/docs/spc700.txt):
  published research, TRAC timer stages, CONTROL rising enable, discarded reads,
  local IPL identity; the document explicitly lists uncertain TEST effects.
- [SPC700 cycle breakdown, June–July2017](https://forums.nesdev.org/viewtopic.php?t=16140):
  original AWJ/Near research discussion and Overload logic-analyzer provenance.
  [Page5](https://forums.nesdev.org/viewtopic.php?p=199437) distinguishes discarded
  operand fetches from internal operations and corroborates (X)+ read timing;
  [page6](https://forums.nesdev.org/viewtopic.php?start=75&t=16140) discusses the
  published blargg/Revenant tests. Those original ROMs were not acquired here.
- [Pinned S-SMP memory reference](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/smp/memory.cpp),
  [timing](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/smp/timing.cpp),
  [I/O](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/smp/io.cpp)
  and [SPC instruction reference](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/processor/spc700/instructions.cpp):
  separately inspected comparison implementation, not code incorporated in this
  project. Its timer2 inconsistency is explicitly rejected above.

No external emulator core or GPL code transplant is included.


## Local performance observation

`dotnet run --project SNESEmulator.Lab -c Release -- benchmark-apu` runs120
original diagnostic fields with the APU active and the own IPL. The local
pre-freeze observation on NILSPC1 / Windows10.0.26200 / .NET10.0.12 was24.04
fields/s,8.59 million master clocks/s, with2.061671 million committed SPC bus
cycles including the initial warmup field. This workload is below nominal
NTSC realtime on that host. It is not a commercial-game benchmark or a speed
guarantee. The explicit clock boundaries and exception-based budget
continuation need later profiling/optimization in M9 while preserving digital
results. The clean implementation benchmark record is stored alongside the
full-gate baseline.
