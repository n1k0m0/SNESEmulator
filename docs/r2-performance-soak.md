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
The CPU/bus full suites, remaining console/horizon/A/V regressions, and final
sustained acceptance are still pending.

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
The integrated readable implementation requires its own fresh measurement;
prototype results do not replace its acceptance, the NTSC37,000/PAL31,000-field
sustained gates, or the actual hour-long application soaks.

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
