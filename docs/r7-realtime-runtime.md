# R7 owner timing investigation and deadline arithmetic

The native PAL owner deficit remains unresolved. This phase fixes an independent
extreme-deadline overflow and adds bounded opt-in evidence that separates owner
execution, commands and waits. It does not claim that the overflow fix cures the
recorded native failures. The R6 presentation-clock corrections remain intact.
No Core implementation, shipping JIT setting, scheduling policy, acceptance
boundary or canonical `publish/` package changes in this phase.

## Retained implementation

`MonotonicClock.WaitUntil(long.MinValue, ...)` previously subtracted the current
positive timestamp in `long`, wrapped, and waited for the 50 ms cap even though
the deadline was already past. An `Int128` difference now returns immediately for
past deadlines and preserves the original inclusive deadline, ceil-to-millisecond,
1â€“50 ms cap, changed-event and cancellation semantics. The observed reproduction
changed from 54.9466 ms to 0.7200 ms. These are observations, not sub-millisecond
unit-test thresholds. Tests cover both extreme endpoints, equality, ceiling,
changed/cancel wake reasons and concurrent callers sharing one clock.

`EnableTimingDiagnostics()` explicitly publishes one fully constructed recorder
with `Volatile.Write`; normal workers do not read allocation, GC or OS CPU
counters. When enabled, the owner records value snapshots into a fixed 1,024-record
ring. Its first cold catchup, first catchup starting after readiness and its
64-record context survive overwrite. Total/overwritten counts remain visible;
reader arrays are detached. Wait observations return directly to their caller,
without shared mutable last-wait state. Fields contain scheduled and actual
start/end, RunFrame end, input sequence, hardware work deltas, thread allocation,
process-wide GC pause deltas and own-thread CPU/cycle counters. Commands and
command batches have separate spans. The owner performs no JSON or file I/O.

Only explicit `--ui-soak` with `SNESEMULATOR_OWNER_TIMING=1` exports these snapshots
on the render side: the first steady event, periodic ten-second observations and
a final file. Existing complete cumulative mute/drop gates remain authoritative.
Snapshots are bounded observations, not a complete per-field input trace.
[GetThreadTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadtimes)
reports 100 ns units, not guaranteed 100 ns measurement resolution.
[QueryThreadCycleTime](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-querythreadcycletime)
is not an instruction counter and must not be converted to elapsed time using an
assumed CPU frequency. Non-Windows counters report unavailable.

## What the native measurements establish

Two complete diagnostic PAL300 runs remain red. The first retained 12 post-ready
muted fields and eight drops. Its first actual steady owner catchup was field
4,177: 52.547 ms total, 52.544 ms inside RunFrame, ending 101.622 ms late. The
preceding lag accumulated while the owner performed no wait or command. Global
maximum observed wait overshoot was only 2.247 ms. This falsifies the proposed
wait-precision explanation for that episode; no high-resolution timer or input
wake optimization was adopted.

The second diagnostic run retained 799 post-ready muted fields and 121 drops.
Its first catchup group of 16 fields consumed 399.132 ms active wall time and
343.750 ms own-thread CPU for about 320 ms hardware time. Separate healthy and
slow groups shared 6,809,088 master clocks, 11,216 SCPU instructions, 94,237 SPC
instructions, 10,239 DSP samples and 256Ã—224 geometry. Healthy wall/CPU totals
were 213.954/203.125 ms; slow totals were 505.289/390.625 ms. GC pause deltas were
0.913/2.488 ms. Matching these aggregate counters does not prove identical every
microarchitectural operation. Pure wait/dispatch improvements cannot alone cover
the measured CPU requirement. These observations establish neither a GC cause,
a JIT defect, a frequency cause nor responsibility of another application.

The user's concurrent applications remained running. Read-only process/window
and explicit power-throttling flags are retained only as context; they do not
measure dynamic QoS or historical audibility/minimization. No foreign process,
host power setting, affinity, global timer, registry or OS priority was changed.

## Rejected bounded alternatives

A separately authorized prototype registered only the active interactive owner
with the standard Windows MMCSS `Games` task and reverted on that same thread.
It never used `AvSetMmThreadPriority`, `Thread.Priority` or process priority.
Lifecycle/failure mocks passed and actual owner thread/process binding was
verified. PAL A/B/B/A120, normal versus Games, passed all four short gates, but
showed no robust throughput/tail improvement: median field durations were
13.179/12.563/12.619/12.528 ms; the last normal run matched Games. The entire
prototype and its 32-group test source remain archived; **none of its policy,
factory, environment switch or native registration remains in product code**.
See the official [MMCSS](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service),
[registration](https://learn.microsoft.com/en-us/windows/win32/api/avrt/nf-avrt-avsetmmthreadcharacteristicsw)
and [same-thread revert](https://learn.microsoft.com/en-us/windows/win32/api/avrt/nf-avrt-avrevertmmthreadcharacteristics) contracts.

One final bounded code-generation series used the exact same isolated package,
PAL ROM/clocks, unchanged gates and existing user workload. A removed inherited
Tiered settings; B requested only `DOTNET_TieredPGO=0`; C requested only
`DOTNET_TieredCompilation=0`, in A/B/C/C/B/A order. Explicit soak-only Manager
launch isolation removes these variant flags and diagnostic suspension variables.
Neither shell-global variables nor shipping runtimeconfig was changed.

| Run | Requested mode | Ready seconds | Post-ready mute/drop | Maximum AV ms | Strict result |
|---|---|---:|---:|---:|---|
| A1 | Default | 7.315 | 0 / 0 | 16.834 | Pass |
| B1 | PGO off | 30.187 | 591 / 33 | 25.953 | Fail |
| C1 | Tiering off | 7.142 | 53 / 23 | 27.696 | Fail |
| C2 | Tiering off | 9.745 | 710 / 46 | 26.506 | Fail |
| B2 | PGO off | 45.806 | 1,427 / 93 | 27.589 | Fail |
| A2 | Default | 9.345 | 0 / 0 | 9.423 | Pass |

All six completed at least 120 actual soak seconds, exited without forced
termination, retained unchanged source/package identities and loaded the pinned
CLR/JIT in App and Manager. Manager exited zero in every run. The first supervisor
stopped after completed red B1 because of `check=True`; its original failure log
is retained and a separate supervisor completed the remaining four regardless
of quality outcome. No run was silently repeated or replaced.

The bounded EventPipe collector emitted **no settings events**, including its
three controlled default/B/C selftests. Failed collector builds/attempts remain
preserved. Requested flags are therefore not independently confirmed effective
settings; absent events, particularly for C, prove nothing about effective
configuration. The [documented settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation)
are candidates, not evidence that they caused a result here. Both variants are
rejected; no JIT setting ships. Default short passes do not erase the longer PAL
failures or establish strict native acceptance. Input is sampled asynchronously
by render callbacks, so end states across separate native runs are not replay
goldens. Sampled median/P99 owner statistics in `codegen-analysis.json` cover
retained unique fields only, not all fields.

## Verification and provenance

The final publication-cleanup runtime passes all 30 runtime groups, including
exact direct Core/video/PCM/state replay with instrumentation enabled. The
relevant phase matrix passes portable network 23, persistence 15, compatibility
state 27, Windows network 24 and Windows integration 11. The unchanged frozen
oracle comparison passes 14 whole-console outputs, 18,952 SPC suspensions and
128 APU horizons. Its first run failed on an AtomicFile.Move access denial;
original exception/partial reports remain under `frozen/` and
`frozen-first-command-crash.json`. One fresh `frozen-retry/` passed against the
original fixtures. No cause for the access denial is asserted.

An independent final cost probe preserves its own driver binary and hashes,
loads the same PAL checkpoint, and alternates diagnostics off/on for 16 AB/BA
pairs of 16 fields each. All 16 corresponding complete states match exactly.
Normal/observed totals were 4.2923812/4.2677903 seconds; median paired ratio was
0.995293. This narrow paused-owner comparison detects no large instrumentation
cost and is not a native performance gain or release gate. The earlier overhead
probe driver was inadvertently overwritten during the first overflow build;
`probe-output-provenance.json` explicitly discloses that limitation. Its original
report/source and independently hashed measured Runtime DLL remain retained.
The independent confirmation uses a separate directory and is not relabeled
as the earlier measurement. The default `artifacts/command-crash.json` was also
written by the first frozen failure; no committed historical manifest references
that default path, and the phase-specific original copy is retained.

All 42 canonical Core files match both prior Windows raw bytes and baseline
Git LF bytes; normalization replaces CRLF bytes without decoding the CP1252
comment in Apu.cs. Hardware clocks, raw PCM, network wire format, State9/State10,
public immutable planes and all historical negative oracles remain unchanged.
Source/package seals distinguish base revision `68bf59d`, dirty implementation
bytes and compiled binaries. The six-run package predates only the final explicit
recorder `Volatile.Write` publication cleanup; its exact implementation snapshot
is preserved in `codegen-source/`. Final regression binaries are stored separately.
Temporary RID lockfiles were restored; all original sealed lockfile bytes were
recovered by exact SHA-matched regeneration, with the recovery recorded explicitly.
No canonical package activation or push occurs in this phase.

The isolated SDK is 10.0.402 with runtime 10.0.13. Actual native CLR SHA256 is
`18eed8c40d05fc455be95d12cc77eacf379265063b15ca84bd7a11cf29078a22`,
and JIT SHA256 is
`2a2332c3c0cb023e71deb356b5fb85ce9045fb8434734dcfe9a7529e427dc858`.
The [LF-pinned evidence manifest](r7-realtime-runtime-evidence-manifest.json)
records local raw artifacts, source byte domains and exact package identities.
Local diagnostic packages are not distributed in Git.

Representative reproduction commands, using the private SDK on process-local PATH:

```powershell
dotnet build SNESEmulator.Core.Tests -c Release
$env:SNES_LAB_CHILD_REPORT='artifacts/r7-realtime-runtime/new-runtime'
dotnet SNESEmulator.Core.Tests/bin/Release/net10.0/SNESEmulator.Core.Tests.dll --runtime-only
# Use fresh labels to preserve every existing native report.
python artifacts/r7-realtime-runtime/seal-native.py fresh-a codegen-package
python artifacts/r7-realtime-runtime/run-native-codegen.py fresh-a 120 codegen-package pal A
```

These commands illustrate the retained test harness, not an instruction to repeat
long acceptance without a new justified remedy. Full strict native acceptance
and original-hardware accuracy remain open. The subsequent integration must
report this limitation without presenting the deadline boundary fix as a cure.
