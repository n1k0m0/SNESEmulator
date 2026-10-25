# R6 Core admission profiling: candidates rejected

No new Core performance change was accepted. All 42 Core files are restored to
`b6ba6ad650426943dd7d83af000b074196e5c762`, including their original Windows
bytes and Git-LF content. The rebuilt Core is byte-identical to this phase's
baseline (`6b8581f0c1151ea16107d8717ec253589e6b00e1342937e2524d69d7b6225e30`).
Five public CPU callback/continuation regression groups are retained. The
remaining native PAL owner catchup is **unresolved**, not fixed by these tests.

The preceding [runtime phase](r6-realtime-runtime.md) fixes independently
reproduced presentation-clock errors. Its normal NTSC 300-second run passed,
while PAL retained six observed owner-muted fields and eight TimelineReset
drops in its 191.0666..201.0672-second counter window. Its final screenshot
failed, so no missing end maximum is inferred. Historical native-hour failures,
all prior packages and every rejected prototype remain intact. No canonical
package was activated, no new native run or release package was made here,
and no push was performed.

## Allocation measurement and scope

The private SDK/runtime 10.0.402/10.0.13 loaded the real native PAL checkpoint
at Frames=703. After 600 warmup fields, an explicitly controlled original
performance replay ran 1,200 fields from 1,303 to 2,503. Exact measured owner
allocation was 945,591,280 bytes over 23.99665 emulated seconds and 15.77705
measured Core seconds. That is about 39.4 MB per emulated second, or 59.9 MB
per measured Core second. The earlier native application's approximately
946.8 MB per ten wall seconds measures a different, process-wide scope.
These rates must not be treated as the same workload or allocation source.

An owner-TID/time-filtered GC allocation trace attributed sampled weights of
203.89 MB to CpuCycle, 43.05 MB to CpuRegisters and 75.15 MB to HardwareEvent.
These are sampled attribution weights, not exact per-type byte or object
counts. Immutable PPU arrays remain the largest category and were not pooled
or recycled. GC suspension summed to 28.529 ms over the measured trace window;
this does not demonstrate a particular native owner stall or GC cause. No
new CPU-percentage claim is derived from EventPipe stacks.

## Rejected approaches

All four prototypes retain the public Action properties and completed actual
cycle records. The first three replace only the exact built-in scheduler's
pending admission argument with private value pins. Replaced/composed hooks
still receive public records. Prototype 1 also routed cached WAI idles through
that value adapter; prototype 2 restored the original idle/class paths;
prototype 3 additionally moved non-idle admission into a separate method.
None establishes robust throughput improvement across independent workloads.

Prototype 4 instead caches at most 2,048 immutable pending read pin records,
only for the exact built-in delegate selected after interrupt polling. It
caches no read data, performs every bus read, and always creates a fresh
committed read record. Public admission replacements receive fresh pending
records. An independent observation-only locality fixture counted 2,156,400
completed reads: the earlier R5 low-address 1,024-slot design hit 812,030;
full-address/pin hashing hit 850,400 with 1,024 slots and 1,232,888 with 2,048.
Those are locality observations, not a speed claim. The old R5 prototype's
broader public-hook/pre-poll caching is separate historical evidence and was
not adopted. The bounded R6 cache also failed performance qualification.

Each comparison uses two AssemblyLoadContexts in the same process, equal
600-field warmups and 24 alternating 50-field AB/BA pairs. State restore,
setup and output hashing are outside timed RunFrame plus DSP drain. Every
pair checks equal fields/clocks/instruction counts and exact final state,
video and PCM. Actual private CLR/JIT module paths and hashes are retained.

| Prototype / workload | Total timed change | Median paired change | Faster pairs | Exact owner allocation change |
| --- | ---: | ---: | ---: | ---: |
| 1 / PAL checkpoint703 | -6.31% | -2.62% | 11/24 | -9.13% |
| 2 / PAL checkpoint703 | +10.86% | +7.05% | 22/24 | -9.14% |
| 2 / NTSC exact tail | +1.68% | +0.77% | 16/24 | -9.44% |
| 2 / PAL checkpoint1215s | -8.62% | -2.99% | 5/24 | -9.13% |
| 3 / PAL checkpoint1215s | +1.77% | +2.26% | 16/24 | -9.13% |
| 3 / NTSC exact tail | -6.98% | -5.79% | 6/24 | -9.43% |
| 3 / PAL checkpoint703 | +3.72% | +6.51% | 16/24 | -9.14% |
| 4 / PAL checkpoint1215s | -5.71% | -3.08% | 8/24 | -5.22% |

Thread-cycle and thread-CPU measurements are retained alongside wall time.
For example, prototype 3's NTSC thread cycles increased 62.330B to 66.053B;
prototype 4's late PAL increased 64.040B to 67.446B. Allocation reductions do
not justify shipping those throughput regressions. A positive first series
was not relabeled universal improvement. Foreign user workloads continued;
no process, priority, affinity, power policy or global timer was changed,
and no external-load cause is inferred from these results.

The native checkpoint plus controlled replay proves variant equivalence for
that experiment. Hardware snapshots do not contain native audio history or
asynchronous render-input ordering. Later native state hashes are therefore
not asserted as same-input end goldens for these controlled suffixes.

## Retained contract tests and next work

Five new groups pass on the original Core: repeated public read admissions
and fresh latched records; bus-installed late Effect/Observed/After callbacks
sharing the same actual object/journal/read hold; +=/-=/null/original delegate
restoration and write/idle identity; admission replacement/removal during
interrupt polling; and every one-master-clock budget from 1 through 120 with
full State9 byte equivalence and restored continuations. The write coverage
counter initially counted WAI's all-low quiescent marker as an invalid write;
it now requires driven data. This corrected classification changed no hardware
code or reference expectation. Tests requiring a discarded private value type
were discarded with that prototype.

The complete restored Core suite passes 144 groups. All 14 frozen whole-console
outputs, 18,952 suspension cases and 128 horizon checks pass unchanged.
No full CPU/SPC corpus or native-hour rerun is claimed in this investigation:
the product Core is unchanged and its previous full corpus evidence remains
historical evidence. No new strict regional throughput acceptance is claimed
for a rejected binary. Raw accuracy differences and acceptance thresholds
remain unchanged.

The next sequential runtime phase must measure requested waits, actual wake
coordinates, owner lateness and GC observations before choosing a scheduling
change. A local high-resolution waitable timer is only an unproven lead here.
No runtime pacing code was changed in this Core phase.

[r6-realtime-core-evidence-manifest.json](r6-realtime-core-evidence-manifest.json)
binds the original source, all four rejected source snapshots, eight paired
series, helper binaries, private runtime identity, tests and local evidence.
Ignored files under `artifacts/r6-realtime-core/` remain local and are not
bundled commercial assets or downloadable CI artifacts.
