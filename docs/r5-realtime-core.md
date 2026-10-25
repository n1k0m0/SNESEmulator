# R5: core allocation and observer overhead

This follow-up reduces measured core overhead without changing hardware clocks,
CPU/SPC instruction semantics, bus effects, pixel phases or savestate formats.
It is the core portion of the NTSC/realtime investigation. Actual final-package
sustained and native-hour acceptance still requires the subsequent runtime and
integration work; the earlier [final integration](final-integration.md) failures
are retained as historical evidence.

## Changes retained

* DMA keeps its pending notification as a private immutable value. It captures
  the same latch before the state transition and creates a public `DmaTrace`
  only when the current completion observer receives it. Subscription changes
  during the transfer retain their original behavior.
* The SPC replay decoder already had a value journal. It now creates public
  `SpcCycle` records only for the public hooks that need them. Electrical and
  legacy completion hooks still both execute after the journal commit. A legacy
  completion hook and subsequent observer share the same immutable public row.
  Private speculative suspension registers also use a value; snapshots retain
  the original public State9 entry and completed-cycle representation.
* Empty PPU notification publication takes a small count-checked path. A queued
  notice still uses the current subscriber, including replacement or removal;
  the optimization does not skip a PPU sampling phase or gate on subscriber null.

The default core regression suite adds actual DMA observer replacement/removal
at bus effects and queued PPU subscriber changes. The SPC guard suite adds
legacy postlatch veto/save/restore without repeated MMIO and a BeforeCycle hook
installing completion/observation hooks. Existing capture and resume contracts
remain the acceptance authority.

## Measurement and rejected alternatives

A fresh EventPipe allocation profile over 600 warmup plus 1,200 measured NTSC
fields observed substantial public observation churn: approximately 306 MB of
CpuCycle, 174 MB of DmaTrace and 114 MB of SpcCycle allocations. Frame-plane
arrays were also prominent; immutable frame ownership and retention were kept.
Actual GC suspension total was 535 ms over the 21.649-second profiled process.
Sampling ticks are estimates of allocation attribution, not exact type totals.

Two EventPipe CPU views attributed unexpectedly large costs to PollGC and
finalizer frames. That attribution remains unresolved and does not establish
that GC caused the realtime failures. A separate ignored diagnostic instrumented
14 actual methods using a hashed 1/1,024 call selection. It recorded about
562.6 million PPU/APU synchronization calls and 163.2 million CPU entry calls over
3,000 fields, mostly WAI idle oscillator entries. Inclusive sampled durations
contain instrumentation and nested-call overhead; they are not additive CPU
percentages or realtime acceptance.

All comparisons used fresh processes, the same private SDK 10.0.402/runtime
10.0.13, 600 warmup fields followed by reloading the identical snapshot, and
serial opposite-order runs. The actual loaded CoreCLR/JIT paths and SHA256
identities were recorded. No priority, affinity, power or global runtime setting
was changed. Original failures and rejected candidates remain local evidence.

The retained candidate's B/A/A/B NTSC comparison measured 2,400 fields per
process from the field-6600 checkpoint:

| Observation | Original core | Retained candidate |
| --- | ---: | ---: |
| Individual realtime ratios | 1.439392 / 1.459945 | 1.465812 / 1.564404 |
| Combined ratio, summed clocks / timed seconds | 1.449596 | 1.513504 |
| Allocated bytes, both measured runs | 4,503,841,328 | 3,661,721,800 |
| Complete 600-field windows | 8 | 8 |
| Minimum complete-window ratio | 1.331650 | 1.425070 |
| Measured owner-thread cycles | 244,253,775,808 | 234,387,672,677 |

The bounded combined gain is **4.41%** and measured-loop allocation falls
**18.70%**. All video, PCM and complete final-state hashes match. This is a
specific own-core workload comparison, not a general speed guarantee or a
hardware accuracy result. It does not replace 37,000/31,000-field sustained
checks or actual 3,600-second native sessions.

An initial DMA prototype gated latch creation on observer presence. Review found
that this could lose a subscriber installed before boundary completion; it was
replaced by the always-latched private value before final functional acceptance.

An exact small-delta APU carry lookup, a bounded immutable CPU pending-read
cache and a shorter WAI decoder entry were rejected after they failed to show
a consistent whole-console improvement. None is included in the implementation.
The WAI six-master-clock oscillator and all original integer clock arithmetic
remain unchanged.

A further exact ARGB channel table and restricted bit-plane extraction improved
the NTSC snapshot comparison but were slower in the PAL comparison. They were
also rejected. The PAL snapshot is field 31,009 from its own regional run,
not the NTSC field-6600 workload. A later PAL guard-only/render isolation drifted
from 1.615220 to 1.094349 for the identical guard-only candidate. This demonstrates
measurement variability but identifies no host cause. **No PAL speed improvement
or final realtime acceptance is claimed.** The render candidates and their
candidate-specific tests are preserved only under ignored artifacts.

## Verification

The final tested Core SHA256 is
`35ff181b8ec2c9011e08547988add92236d2a46fa134823d7a44fc7f58f32821`.
It is byte-identical to the retained measured candidate. The current Lab and
Core.Tests loaded those same bytes with actual private CoreCLR/JIT observations.

The final focused sequence passed the 131 core groups, nine SPC continuation
guards, nine SPC entry lifecycle groups, 256,000 SPC semantic/electrical vectors,
256,000 mixed continuation vectors, 30 DSP1 groups, 34 SuperFX groups, 32 SA-1
groups, generator reproducibility, and the unchanged frozen 18,952 resumptions,
128 horizons and 14 complete-console outputs. Results and exact local evidence
identities are recorded in the [R5 manifest](r5-realtime-core-evidence-manifest.json).
Core groups took 152.734 seconds; the complete frozen sequence took 98.813
seconds. All owned command processes exited normally. This phase ran on Windows;
no new Ubuntu or native-hour result is attributed to it.

Known raw CPU/SPC/chip and full-sequence audio differences remain separate;
this follow-up does not update or rewrite any raw/golden fixture.

The tested working source was based on `9236e22ac543952f1165bebfcc090dd94162d0c9`.
Per-file source identities bind the implementation contents independently of
its later documentation/commit metadata. A later integration build must identify
its own binaries and run the actual release gates; these old DLL measurements
must not be transferred merely because the code has the same commit ancestry.

## Reproduction

Use the repository's private SDK in the child process environment, then build
Release and run `python tools/test-performance.py --output NEW_DIRECTORY` for
the frozen regression contract. Run `python tools/test-lab.py --full --windows`
for the complete supported laboratory, keeping its separate accuracy inventory.
The local bounded comparison scripts and all unmodified reports are under
`artifacts/r5-realtime-core/`; these ignored files are evidence on this machine,
not bundled or downloadable fixtures. No new package has been activated by this
core-only follow-up.
