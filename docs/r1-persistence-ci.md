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

Targeted Windows and actual Ubuntu persistence: 15/15 groups each.
Targeted Windows manager, including the new budget test: 13/13 groups.
Targeted Ubuntu manager before that new group: 12/12 groups.
Full portable gates, new Ubuntu budget test and native persistence modes: pending.
Remote follow-up: pending a coordinated commit/push and the resulting exact run.

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
