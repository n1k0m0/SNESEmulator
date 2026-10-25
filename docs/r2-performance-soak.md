# R2: sustained performance and native application soaks

[R5 core overhead](r5-realtime-core.md) records the later allocation and empty-
observer optimization, scoped short-run evidence and rejected alternatives.
It does not clear the independent sustained/native release gates.

[Final integration](final-integration.md) now records the exact final package:
37,000 NTSC and 31,000 PAL fields both retained slow complete 600-field windows.
Both actual strict native hours completed; PAL passed, while NTSC retained
288 post-ready muted fields, 127 remote drops and late probe readiness.
Functional and package checks and exact implementation-head hosted CI passed.
Strict timing release acceptance remains open. The sections below retain the
historical R2 implementation and its bounded evidence; their previous passes
do not override the final measurements.

## Implementation

The SPC700 retains its independently developed C# decoder. An explicit
micro-operation CFG executes the same bus cycles for 23 measured common forms
and preserves decoder locals between timing boundaries. Other opcodes
intentionally use the existing replay decoder. Resume and opcode selection
choose a phase; successful cycle/register transitions use direct edges instead
of repeatedly evaluating a large sparse switch.

`tools/generate-spc-hotpath.py` contains the named microprograms and verifies
the SHA256 of the reviewed legacy decoder. `--check` verifies reproducible
output. Decoder changes require another microprogram review and independent
vector/suspension tests. The generator is not a hardware reference.

The internal electrical adapter passes address/value/direction without
allocating a public cycle record for every internal timing attempt. Capturing
the private value journal still produces immutable public `SpcCycle` records.
Diagnostic counters belong to each CPU and are not public global product state.

State9 is unchanged: additional fields are decoder locals, reconstructed from
Entry and Completed after restore. Completed MMIO effects and observer
notifications are not repeated. In particular, PC++ waits until the read and
its postlatch suspension have resumed.

## Admission, postlatch, and lifetime

A legacy veto runs before electrical APU admission. This prevents a new,
unserialized intermediate state between advanced APU timing and an
unjournaled bus effect. Both completion hooks observe each committed access,
even when one requests suspension. The journal is already committed first.

Seven targeted cases cover hook composition, suppressing premature bus and
observer calls, capture/restore after a veto, a half-held F4 port read, reset,
and explicit replacement of a continuation. The first isolated candidate
failed four cases. The corrected candidate and integrated implementation
passed all seven. The original product did not have these new internal hook
combinations.

## Verification so far

The integrated source passed all 18,952 unchanged frozen suspension/resume
cases and 256,000 SPC vectors through RunInstruction. Exactly 23,000 vector
cases execute the 23 microprograms; 233,000 explicitly use the replay decoder.
The original Step verification remains separate. The existing 7,000 raw bus
differences and their approved adjudication remain separately reported; this
is not a raw-all-pass claim.

The composition suite and regeneration check are part of the normal test lab.
The additional `spc-run-full` command executes every vector through the
integrated continuation entry point and reports actual hot/replay counters.
The first committed integrated core (`10d55df`, Core SHA256 `b0324f50...`)
completed the full Windows gate with exit 0 in 1,097.922 seconds: all 5,080,400
CPU semantics and 5,080,400 bus cases (26,184,846 bus rows), both SPC paths,
frozen states, console/native video/audio, persistence and Windows integration.
Its targeted Ubuntu 24.04.4 check used the actual private 10.0.13 runtime and
passed the generator, seven guards, 256,000 mixed SPC vectors, 18,952 resumes,
128 horizons and 14 console comparisons. This was not a complete Linux CPU
corpus rerun. An initial Ubuntu wrapper incorrectly forced original IPL; its
PCM mismatch remains preserved. Removing only that wrapper override restored
the unchanged own-IPL contract.

The evolving M8/M9 test adapters now have explicit historical source snapshots.
The original integrity manifests, fixtures and golden outputs are unchanged.
Only three M8 and two M9 historical adapter paths map to bundled, Git-blob-
verified snapshots; every other manifest path retains its original check.
Isolated negative controls reject changed historical source and changed real
fixtures. Shallow CI does not require historical Git objects.

The hot-entry integration `97a05a7` subsequently passed its own complete Windows
run in 974.625 seconds and the actual Ubuntu targeted checks. Both use the
private runtime 10.0.13. The new lifecycle suite passed nine cases, the seven
guards passed, and mixed SPC verification recorded 256,000 value entries,
23,000 hot cases, 233,000 replay cases and 233,000 materialized public entries.
The complete frozen resume/horizon/console comparisons also passed.
Nine previously stale Updater-directory outputs were rebuilt during the Windows
run; that explicit before/after delta is preserved. Core, App and Lab identities
were unchanged through the tests. All 1,770 actual postgate outputs were frozen
for the later long and native runs. The existing M8 accuracy inventory remains
red separately; passing regression contracts do not turn it into a hardware
accuracy pass.

## Measurements and limits

Previous M9h sustained tests remain red: 54/61 NTSC and 17/51 PAL complete
600-field windows were below realtime. Both regions actually ran. A good
overall mean does not replace the per-window criterion.

New isolated coroutine, slot, scheduler, and initially sparse flat prototypes
did not deliver a reliably repeated whole-console gain and were not adopted.
Their failed runs and actual module/runtime hashes remain under
`artifacts/r2-performance-soak`. Measurements used the private SDK10.0.402 and
runtime10.0.13. Global toolchains and host priority, affinity, and power
settings were not changed.

The direct-goto prototype passed a bounded balanced comparison: each process
ran 600 identical warmup fields, reloaded the same field6600 state, then
measured 2,400 fields. Controls reached 1.52145x/1.49487x and candidates
1.59719x/1.59662x. All 16 complete windows passed, with a combined relative gain
of 5.89%. Video/native planes, PCM, and full endstates matched exactly. This is
an own before/after regression, not an independent hardware measurement.
The first readable integration had its own balanced comparison: controls
1.07443x/1.51033x, candidates 1.22012x/1.39459x, combined relative gain 3.66%.
The wide control variation is retained. These short comparisons do not replace
sustained gates or actual hour-long application soaks.

The first integrated long gates actually ran both regions. NTSC 37,000 fields
covered 615.653 emulated seconds at 1.08966x overall, but 18/61 complete windows
were below realtime (minimum 0.881345x), so NTSC remained red. PAL 31,000 fields
covered 619.913 seconds at 1.28901x; all 51 complete windows passed (minimum
1.035909x). Managed maxima were 42,606,576/52,813,160 bytes, zero presentation
loss, and largest drains 533/640 below capacity 8,192. Explicit post-run checks
verified identical historical ROM, firmware, replay, profiles and field/clock
ranges, then exact video, PCM, SRAM, compressed and raw end/checkpoint states
against the earlier actual M9h runs. Those historical outputs are own-core
regressions, not hardware oracles. The red NTSC windows are not clipped.

## Measured hot-entry continuation

A fresh profile of the actual `10d55df` core over NTSC fields 3610-4809 retained
exact control/profile video, PCM and raw state. The bounded repeats were faster
than those same windows in the long run; no host cause follows. Exact instance
counters over 1,200 fields measured 6,751,617 register captures, 5,881,137 fresh
dispatcher entries and 870,480 legacy suspensions. Scheduler counters also
recorded 225,057,946 APU calls, 203,786,125 without an endpoint. The new candidate
targeted the repeatedly created instruction-entry records; legacy suspension
snapshots remain unchanged.

The entry now retains original registers as a private value. A public observer
or snapshot request materializes and caches the immutable original Entry.
Instruction-boundary checks include the private value. Legacy handoff after an
already journaled opcode and AdvanceCycle materialize the original entry before
replay; they never use the exposed PC+1 as a new start. Reset and explicit
continuation replacement discard the private value. State9 remains unchanged.

Nine additional public lifecycle cases cover admission, pre-effect/postlatch
and CycleObserved capture before the first yield, record caching, legacy
handoff, reset, abandon, AdvanceCycle replacement and public register edits.
The isolated candidate passed these nine, all seven previous guards, 18,952
frozen resumes, 128 horizons, 14 console comparisons and all 256,000 mixed SPC
vectors. The vectors actually started 256,000 value entries and materialized
233,000 public records for the explicit legacy forms. One horizon attempt
stopped on an actual File.Move UnauthorizedAccessException; a fresh run of the
same unchanged binary passed. Its cause remains unresolved, the failed files
and escalated-workdir execution boundary are preserved, and no generic retry
or persistence change was introduced.

Two opposite balanced comparisons used fresh processes, 600 identical warm
fields, reload of field3610 and 2,400 measured fields. A/B/B/A reached controls
0.99672x/1.21698x and candidates 1.25520x/1.23010x (+13.38% combined). B/A/A/B
reached candidates 1.18388x/1.16580x and controls 1.06984x/1.14394x (+6.25%).
All eight video/PCM/raw-state signatures matched. All 16 candidate windows
passed, minimum 1.04814x. Combined relative gain was 9.72%, with the full control
variation retained; allocation fell about 16.55%. This justified integration,
not sustained acceptance. The integrated `97a05a7` core then completed its own
37,000-field NTSC and 31,000-field PAL gates:

| Region | Emulated seconds | Overall realtime | Complete 600-field windows | Slowest complete window | Managed maximum |
| --- | ---: | ---: | ---: | ---: | ---: |
| NTSC | 615.653 | 1.337844x | 61/61 pass | 1.006682x | 45,856,240 bytes |
| PAL | 619.913 | 1.445368x | 51/51 pass | 1.182354x | 60,952,064 bytes |

The NTSC minimum leaves only 0.668% reserve; no larger margin is claimed.
Both had zero producer output losses and drains strictly below capacity.
The preserved historical input/profile/ROM/IPL/replay identities matched before
comparing exact video, PCM, SRAM and compressed/raw end/checkpoint states with
actual earlier M9h outputs. Both digital comparisons passed. This establishes
own-core continuity, not hardware correctness.

EventPipe sample attribution and actual GC suspension duration are different
quantities. The unusual PollGC sample attribution is unresolved and proves no
GC cause. Sampling every 1,024th call may alias with emulation periods. The
later call-index hash selection is still an instrumented observation with
timer/wrapper overhead, not a realtime acceptance test.

## Actual historical hours and the observation gap

Two native `97a05a7` sessions actually ran sequentially with normal pacing,
synthetic input, localhost TLS/WinMM, an actual WPF Manager, twelve library scans
and twelve verified rotating checkpoints each. Source and all 1,770 binaries
remained unchanged; actual first/last App and Manager CoreCLR/JIT hashes were
verified, and the four owned processes ended normally.

| Region | Actual application wall seconds | Hardware seconds | Fields | Cold ready seconds | Cumulative software A/V maximum |
| --- | ---: | ---: | ---: | ---: | ---: |
| NTSC | 3,600.928 | 3,599.505 | 216,326 | 5.178 | 33.302 ms |
| PAL | 3,600.389 | 3,599.478 | 179,999 | 6.519 | 32.351 ms |

These are historical actual two-hour runs, not strict acceptance of the later
runtime. The original ten-second `PacingLateSeconds` samples missed short
catchup episodes. Earliest post-ready samples were 10.514/10.536 seconds;
subsequent cumulative counters prove at least 19/68 additional muted fields.
NTSC changed in the 80.527-90.531-second observation window. PAL changed in
260.593-270.600 and 300.603-310.603 seconds. Those windows bound observations,
not exact mutation times or a host cause.

Remote discards were 39/68 during cold start, 11/22 during measurement and 21/17
during normal cleanup. Producer drops, device overflows, observed ARGB
mismatches and driver-observation gaps were zero. Unmatched ARGB was 4,173/83;
matching covers observed same-epoch fields, not every generated frame.
Maximum UI intervals were 130.392/126.757 ms. Peak managed memory was
49,943,520/43,636,944 bytes. Actual Manager requests were 3,599/3,598.
Received source PCM before gain contained 16,053/3,074 nonzero pairs among
115,046,264/114,970,832 pairs; this does not prove continuous music or physical
output. The old `Success` only reflects its original criteria, not a claim of
continuous zero catchup or zero remote discards.

## Actual field-end lateness and continuous counters

The owner previously classified completion against the field's scheduled
start; the rational pacer was advanced afterwards. A normal field duration was
therefore counted as additional lateness. Both NTSC and PAL incorrectly marked
90 ms and exactly 100 ms of actual end lateness as catchup.

Eight deterministic cases exercise the actual worker with an injected clock,
using its first normal wait as a barrier. Zero, 90 ms and exactly 100 ms remain
unmuted; 100.001 ms remains catchup. Before the correction the runtime suite
reported 20 pass/4 fail; afterwards all 24 passed on actual Windows and Ubuntu
with private runtime 10.0.13. Every tested field's RGB555, brightness, ARGB,
timestamped PCM and complete hardware endstate matches direct execution.
The same rational `FieldPacer.Add` now executes once before publishing the
immutable normally paced output. Paused deterministic stepping still makes no
Add call. The 100-ms threshold, pacer arithmetic, hardware ticks, sample rate,
source timestamps, state format and hardware core are unchanged.

The native observer reads actual cumulative presentation counters at every
render callback and again after the closing screenshot at measurement cutoff.
The readiness boundary is immutable. A post-ready muted-counter increase now
fails the diagnostic even if the latest field and sampled lateness are normal.
Individual Interlocked reads are not one joint atomic field-boundary snapshot.
A ring retains the latest 256 counter changes and explicitly counts evicted
detail; cumulative totals continue without loss. Ten deterministic observer/
drop tests include last-window changes, cold-baseline immutability, overflow,
regression, stale-generation rejection, concurrent enqueue/reset snapshots,
and shutdown after the concurrent tasks have joined.

Network drop reasons are disjoint under the existing commit lock. Explicit
public timeline/disconnect resets, capacity resets, source drift, invalid
generation, missing driver clock, stale audio/video, delivery failure and direct
shutdown resets are distinguishable; their sum equals the original total.
Normal freshness or cleanup discards are not automatically producer losses.
Queue, generation, short-circuit decision order and time thresholds are unchanged.
The complete Windows network suite passed 23 groups, portable network 16,
Windows integration 10, native interaction 151 stages, and native runtime and
persistence checks passed. No unmodified multi-million-vector rerun is attributed
to this host-only correction; the previously verified Core DLL stayed byte-exact.

The first improved observer reproduced four real post-ready muted fields in a
360-second NTSC diagnostic and correctly returned failure. Its initially
first-only detail buffer overflowed visibly; the later ring fixes that loss of
recent context. Original failed reports remain preserved. A separate late
WinError299 was observed during owned normal close while querying Manager
modules. The close overlap is established; the precise Windows error mechanism
is not. Later wrappers stop new module reads at the owned native close signal
and retain the actual successful near-end module identities and timestamps.

## Bounded corrected-runtime evidence and remaining release gate

The corrected runtime then ran separate 360-second NTSC and PAL native counter
probes. These are not hour tests and do not replace the historical runs.
Actual application durations were 360.280/360.268 seconds, with 21,606/17,977
fields. Ready times were 7.360/6.991 seconds; cumulative software A/V maxima
were 7.936/7.937 ms, including cold start and postcallback observations.
Both had zero actual post-ready muted fields and zero post-ready remote drops.
Cold discards were 84/83 (68/70 source-drift and 16/13 explicit timeline resets),
with 21/17 normal explicit-reset cleanup discards. All reason sums balanced.

Both probes passed with unchanged source/binaries, verified actual private
App/Manager runtime modules, normal owned cleanup and no remaining owned
processes. Managed peaks were 47,926,616/36,467,432 bytes, remote pending maxima
23/20, two library scans and two checkpoints each. Observed mismatches and
driver gaps were zero; unmatched fields were 486/48. The NTSC ring explicitly
evicted 22 cold detail rows; cumulative counters remained complete.

All 11,307,777/11,320,659 received source PCM pairs in these bounded probes were
zero before gain. They therefore do not establish nonzero amplitude or audible
stereo. The separately passing native audio/network tests provide different,
explicitly scoped nonzero evidence. None of these tests measures physical
DAC/display latency or establishes correctness against original hardware.

**Historical R2 remaining gate (now executed in [final integration](final-integration.md)):** after the subsequent accuracy,
documentation and enhancement-chip work, run the final actual binaries for
3,600 wall seconds NTSC and then 3,600 PAL with the strict cumulative counter
observer, unchanged cold-readiness rule and cumulative 40-ms A/V limit. Both
must run fully even after a quality failure. The old `97a05a7` hours are
historical, and that R2 candidate had only the bounded probes.
The final integrated hour/CI results are reported separately above.

`docs/r2-evidence-manifest.json` records the local report identities, scopes and
remaining gate. Paths under ignored `artifacts/` refer to preserved local actual
runs; the manifest is provenance, not a golden fixture or a substitute for CI.
