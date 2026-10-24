# R2: sustained performance and native application soaks

This step is in progress. It does not release M9 or claim complete hardware
accuracy. The authorized order remains persistence/CI, sustained performance
and soaks, accuracy, readability/documentation, then enhancement chips.

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

The later hot-entry integration below requires its own final committed-source
regression and sustained acceptance. The existing M8 accuracy inventory remains
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
not sustained acceptance. The integrated final source must run its own gates.

EventPipe sample attribution and actual GC suspension duration are different
quantities. The unusual PollGC sample attribution is unresolved and proves no
GC cause. Sampling every 1,024th call may alias with emulation periods. The
later call-index hash selection is still an instrumented observation with
timer/wrapper overhead, not a realtime acceptance test.

The remaining native tests must each run 3,600 actual wall seconds of NTSC and
PAL with normal pacing, input, TLS/WinMM, the actual WPF Manager, and library
load. Both regions run even after a readiness failure. Cold observations still
count toward the cumulative 40-ms A/V limit. Source PCM before gain is reported
separately from physical audio/display latency. Passing does not imply an
original-hardware comparison.
