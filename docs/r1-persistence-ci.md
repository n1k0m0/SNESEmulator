# R1: persistence and CI follow-up (2026-10-24)

This is the first step of the requested sequential follow-up. Performance/soak,
accuracy, broader readability/documentation and expansion chips are later steps.
No emulation clock, save format, atomic replacement policy or settings lock
budget changes in this step.

## What was observed

The unchanged local Windows persistence suite passed all 14 groups. The old
one-second running-CPU assertion also passed in an isolated cold process:
47 fields and 13 writes in 1.030s, with 13 different captured SRAM digests, no
storage error and no owner fault. A process-local MinOpts JIT diagnostic produced
16 fields and 3 different written captures in 1.054s. Neither reproduces the
remote failure. The old remote report lacks its field/write/error values, so its
exact cause remains unproven; it is not retroactively labelled a throughput bug.

The assertion mixed two contracts: actual persistence and greater than ten
hardware fields within a fixed second. It could not distinguish a stopped owner,
a save error, an unchanged capture or a slower runner. The revised test keeps
more than ten fields and a running fault-free owner, measures actual elapsed time
for autosave ticks and independently validates at least two different complete
SRAM files against immutable owner captures while execution continues. It checks
a readable previous generation and an exact final owner-to-disk flush. A separate
15-second liveness watchdog bounds completion; this is not realtime acceptance.
Failure output retains owner status, elapsed time, attempts, writes, errors and
up to 64 snapshot digests.

An additional real file-lock conflict requires a visible autosave error, unchanged
committed bytes, continuing CPU execution and a successful exact final retry.
A separate real-process settings test holds the actual named mutex beyond its
unchanged two-second budget, requires an explicit failure without changing
primary/backup generations, then releases the holder and validates a successful
retry with the original primary retained as backup.

## Historical limits that remain open

Older Linux settings attempts reported a two-second busy error and once an
ENOENT from the .NET named-mutex backend under `/run/user/1000/.dotnet/shm`.
The unchanged targeted Ubuntu manager suite currently passes, including two
concurrent settings writers. That does not establish the cause of the old errors
or fix the runtime backend. The controlled held-mutex test proves the intended
contention/error/retry contract only. No global runtime, session directory, XDG
setting, priority or affinity is changed and there is no speculative lock rewrite.

Older native persistence windows closed before their expected final stage. Their
cause was not recorded and remains unknown. New runs must be reported separately;
a new passing probe does not explain the old closure.

## Validation

The full portable gates on clean implementation commit `83138b6` passed on both
Windows and actual Ubuntu: **287/287 positive functional groups per OS**.
Windows took 717.83s; Ubuntu took 1164.94s of real wrapper wall time. Each run
retained 248 fresh report/diagnostic files (about 2.54MB on Windows), and all
copied file SHA256 values were reread. Both starting source inventories match.
The count comprises core 129, video 35, M8-state 27, DSP 17 plus long 1, manager 13,
persistence 15, runtime 16, interaction 9, network 16 and distribution 9.
Intentional failing mutation/watchdog controls and the existing 18 red M8 audio
accuracy comparisons remain separately reported. A green functional gate is not
full commercial-game compatibility, full external CPU coverage or hardware proof.

A later test-only review moved the settings observation clock before child
creation so delayed parent scheduling cannot shorten the observed mutex wait.
The affected manager suite was rerun on each OS: **13/13 passed per OS**. The
complete gates above remain attributed to `83138b6`; their source and module
identities were not rewritten to the later test observation change. Production
code, deadlines and persistence policy did not change for this follow-up.

The actual native persistence probe passed all four modes (normal,
settings-failure, battery-failure and log-failure) twice. The second run explicitly
used the private runtime and its running process's `coreclr.dll` path/version
proved 10.0.13. It completed in 71.305s; all 1379 native dependency-file hashes
were identical immediately before and after. The normal probe first observed
periodic commits at 30.055s / observed field 1803 and 60.063s / observed field 3607, with different CPU-written
SRAM. Those field numbers are observed status after completion, not atomic SRAM
capture timestamps. Recovery/preview/load/delete/close and all negative final
save policies passed. The first native run took 70.414s and passed, but its actual
CLR module was not captured and DOTNET_ROOT was not explicitly set; it is not
claimed as pinned-runtime evidence. Native binaries were built from the preceding
HEAD with the documented R1 diagnostic/comment patch before commit; their assembly
version metadata is not relabelled as the later clean commit.

The exact remote follow-up
[40042721554](https://github.com/n1k0m0/SNESEmulator/actions/runs/40042721554)
passed on `b840f105bd61b54572246a397f4d476182b569cc`. Root downloaded 500 files
and checked 287 groups per OS; readback is recorded in
`artifacts/r1-persistence-ci/root-remote-ci-acceptance.json`. This acceptance
belongs to that commit, not to subsequent R2/R3/R4 or future M10 changes.
The earlier failed run remains failed.

Commands use SDK 10.0.402/runtime 10.0.13, with process-local DOTNET_ROOT/PATH:

```text
dotnet SNESEmulator.Core.Tests/bin/Release/net10.0/SNESEmulator.Core.Tests.dll --persistence-only
dotnet SNESEmulator.Manager.Tests/bin/Release/net10.0/SNESEmulator.Manager.Tests.dll
python tools/test-lab.py
python tools/test-ui-persistence.py --output artifacts/r1-persistence-ci/<fresh-native-directory>
```

New local evidence is under `artifacts/r1-persistence-ci/`; old frozen evidence is
retained. The workflow now disables portable matrix fail-fast so one failure
cannot cancel the other OS, and uploads supervised stdout/stderr diagnostics.
Remote run 39997641997 remains failed: external CPU/SPC and package passed,
Windows portable persistence failed, Linux was cancelled, native was unexecuted.
