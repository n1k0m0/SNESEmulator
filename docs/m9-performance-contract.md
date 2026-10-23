# M9a performance contract

Status: **M9a incomplete. The sustained realtime gate is red.**

Scope: preserve the complete own CPU/SPC/PPU/DSP accuracy path while reaching
real time on NILSPC1 (AMD Ryzen9 9900X,12 cores/24 logical processors, Windows
10.0.26200, .NET10.0.12 x64). UI threading, input/menu parity, networking,
persistence, distribution and sustained hours-long A/V operation remain later
M9 work. The pre-M0 `publish/` directory is not a new release.

## Frozen starting point and workload boundaries

The unmodified M8 core is `10164c78e24124dfc613764cffd859409aeed1f8`.
`artifacts/m9a/baseline-core.dll` and the separate baseline runner preserve that
actual binary. The initial baseline precedes core edits. The prior implementation
outputs in `testdata/m9-performance-baseline.json` are explicitly determinism
regressions, never independent hardware oracles. No M0–M8 expected pixel/sample,
known-error contract or acquired fixture changed. M8's18 whole-console audio
rows remain red.

Each process runs ten complete warmup fields, then120 measured fields (game:
320), followed by an exact12345-master-clock partial field. Timing surrounds
`RunFrame` plus draining its actual native DSP queue. Native graphics remain
fully computed, including brightness/ARGB; APU and DSP stay active. Hashing,
artifact I/O and savestate compression are outside the timing boundary. All
native output is consumed; zero presentation drops are mandatory. Each frame's
exact master-clock duration is divided by the profile's rational frequency;
there is no fixed60fps threshold. Thread allocation deltas and per-generation GC
counts are measured alongside time. Host scheduling and JIT effects can affect
short runs; failed/repeated experiments are retained, not silently selected out.

| Complete console workload | Baseline fields/s | Rational realtime factor | Allocated bytes/field |
|---|---:|---:|---:|
| Original Mode0 diagnostic, NTSC |14.95|0.249|41723376|
| Original two-BRR-voice diagnostic, NTSC, forced blank |18.17|0.302|18815265|
| Original Mode5 hires/interlace/239-line ROM, NTSC |16.23|0.271|18175966|
| Same geometry, PAL |15.99|0.320|20435943|
| Unchanged external HiROM techdemo with replay, NTSC |15.07|0.251|41118662|
| Same external techdemo/replay, explicit PAL |11.79|0.236|47974604|

ROM hashes are in each report; the geometry ROM is assembled by
`Performance.GeometryRom` with actual65816 register/DMA effects. The game is the
unchanged M8 MIT/CC techdemo. `m9-performance-replay.json` exactly copies M8's
physical-field input sequence, including Start at150 and room trigger at210.
Neutral performance windows are fields10–149,150–209,210–299 and300 onward;
these labels do not assert instantaneous scene changes. PAL is an explicit
workload profile, not a new hardware certification of that demo.

`.pcm.bin` here contains20-byte little-endian records: signed64 sample index,
signed64 crystal timestamp, signed16 left and right; it is not plain4-byte PCM.
The ordinary `rom` command still emits plain stereo PCM16. Every raw field hashes
geometry JSON, native RGB555LE, brightness bytes and ARGB32LE, in that order.
Savestate/ROM/SRAM files and field0/9/final checkpoints remain available.

## Profiling and changes

`dotnet-trace10.0.750501` collected EventPipe sampled stacks and allocation events;
TraceEvent4.0.0 decoded allocations in an isolated artifact-only tool. These
packages do not enter the product. Trace collection overhead and runtime helper
threads mean stack residency is not a hardware CPU-utilization percentage.
The initial dominant stack was exception dispatch under SMP budget suspension.
Removing only pre-latch exceptions still left port-read hold exceptions; both
must cooperate. The allocation profile then identified unnecessary PPU notices,
CPU cycle/register/interrupt records, pin-string arrays and per-cycle closures.

- SPC `RunInstruction` returns false when its private timing callback cannot
  commit a cycle. It freezes the exact old throw-point registers and journal.
  Remaining finite decoder calculations are speculative: every later bus and
  observer call is suppressed, then all architectural registers are restored.
  Post-latch port holds keep the already journaled read. `Step`/`AdvanceCycle`
  retain their ordinary cycle behavior and exception yield where required.
- PPU phase records are allocated only with a real observer. Notifications keep
  their original committed-tie publication. CPU/DMA elapsed callbacks are bound
  once; pin strings and the four immutable interrupt samples are shared. Last
  unchanged idle/register records are cached only after comparing every field.
- `RunFrame` lets `RunUntil` process internal events continuously, stopping at
  V129 and then the exact field tail. This commits the V128 interlace input
  before predicting the end and permits pending H0 on entry. WAI retains every
  six-master-clock idle phase, refresh, DMA/ALU/IRQ work and CPU counters.
- The APU boundary conversion caches only the pure tick-to-master calculation.
  Clock accumulation uses64-bit arithmetic only when the *whole unreduced sum*
  is proved to fit. Wide profiles use the previous Int128 domain. No remainder
  reduction, oscillator change or timing approximation occurs.

All caches contain derived immutable data; format9 and its serialized bytes stay
unchanged. Reset, PowerOn, profile replacement and earlier-tick restoration are
explicitly compared against cold caches.

## Permanent checks and limits

`SuspendProof` was executed against the frozen old DLL before using it to check
the new kernel:18952 before/after suspend points across all256 opcodes and eight
register/memory seeds, including X=0 DIV, stack wrap, dummy/branch/SLEEP/STOP.
Each row freezes public registers, entry/completed journal, exact bus/observer
sequence and RAM hash; continuation/resume must match the untouched instruction.
This strengthens, and does not replace, the independent256000 SPC vector suite.

`FrameProof` compares128 complete field results from64 NTSC/PAL, initial
interlace, pre/post-V128/midfield/H0 and observer configurations against the old
per-event RunFrame horizon. Exact savestate bytes, stereo and CPU/SMP/DSP/PPU/
event/DMA observer records must agree. Eight cache-reset/reinsert comparisons
and40 independent BigInteger quotient/remainder/restore cases cover ordinary,
unreduced and wide synthetic clock profiles.

`tools/test-performance.py` checks14 frozen whole-console workloads: six truly
untraced, six CPU/bus/event/DMA traced and two full SMP/DSP/PPU observer runs.
The observer path is intentionally allowed to be slower; its digital behavior
must be identical. `--long` additionally executes1200 fields per workload,
approximately20–24 emulated seconds; this is an extended workload, not an
hours-long soak. `--sustained` separately runs37000 NTSC and31000 PAL game fields,
more than ten emulated minutes each, with bounded per-field PCM streaming hashes,
600-field timing/allocation windows, GC, managed-memory and queue measurements.
`--realtime` requires all measured untraced workloads/windows to meet their
actual rational hardware duration on the measured host.

The M8 Lab Program.cs hash pin changes solely because its command dispatch now
adds these three test commands; its existing ROM/accuracy paths are unchanged.
The test-lab.py source pin changes only to invoke the additional M9 gate. All other M8 immutable entries and independent accuracy expectations remain fixed.
There is no automatic expected-output or reviewed-error update command.

```powershell
python tools/test-lab.py --windows --full
python tools/test-performance.py --output artifacts/m9a-final-extra --long --sustained --realtime
```

Final clean-commit results and frozen archive/file identities are recorded in
[the M9a baseline](baselines/m9a.json). Parent acceptance remains separate.

## Measured implementation result and open gate

Clean implementation commit `2dbeb5ff61511c0cdf279f7849c5456802953f61`
produced core DLL SHA256
`0ea2e86d2f3a0de5c16cedd40276150cd7cf42ecb34ff8d1cc050200140cf83c`.
The full Windows/portable gate passed with Exit0 (209 portable and10 Windows
groups), including all independent earlier milestone checks and the14 frozen
M9 output comparisons. This is accuracy regression evidence for that commit;
it does not accept M9 performance or turn known audio differences green.

| Workload | Short realtime factor | 1200-field extended factor |
|---|---:|---:|
| Diagnostic NTSC |1.500|1.448|
| Two-voice audio NTSC |2.206|2.116|
| Hires/interlace/239 NTSC |1.748|1.654|
| Hires/interlace/239 PAL |1.943|1.827|
| Game/replay NTSC |1.363|1.467|
| Game/replay PAL |1.482|1.583|

These numbers describe this head and host with the stated boundaries. They do
not establish general game compatibility. Short throughput is insufficient:
the37000-field NTSC run covers615.652749 emulated seconds, averages1.025350x,
and retains zero drops and a35,838,864-byte maximum observed managed heap, but
**38 of62 consecutive600-field windows fail realtime**. The minimum is0.882206x
starting at field6610. Its GC collections are10225/4397/3568 (Gen0/1/2).
The extended command exits1 at this assertion; the PAL sustained case therefore
has not run. No failed report was replaced, and the threshold is unchanged.

Loaded-state diagnostic repros include process CPU/wall time, allocations, GC,
output hashes, exact final states and DLL identities. One original-core OS
probe reads Normal priority, base priority8, and power-throttling control/state
masks0/0. No priority, affinity, QoS, power setting or foreign process changed.
An EventPipe sample reports approximately0.497 seconds of GC suspension in
16.920 seconds; this does not establish GC as the cause of the deficit. CPU/wall
ratios near one likewise do not prove host contention.

The packed-bitplane PPU candidate was measured between original controls and
was slower, despite exact video/stereo/state outputs over2400 fields. Other
isolated clock/JIT/SPC allocation candidates also failed to demonstrate a
reliable gain. None was adopted. Their source, binaries and raw diagnostics
remain in the evidence, with limitations in `diagnostic-disposition.json`.
Allocation reduction alone was not treated as a performance success.

Following the parent review's sequencing decision, worker/UI work proceeds
next. Sustained NTSC/PAL performance remains an explicit prerequisite before
M9 or release acceptance; hours-long A/V/threading soak remains later work.
The frozen evidence preserves this **partial result and failed gate**, not a
performance acceptance. See `artifacts/m9a-implementation-evidence/acceptance.json`
for archive identities, every-file hashes and reread verification. The final
commit after implementation changes documentation only.
