# R5: exact slow-tail replay and sustained Core headroom

Both fresh sustained Core gates now pass at implementation
`6db7c8aecd2022bcfbf6074d7b8b58b3b7fdd1ca`. This is separate from native
3,600-second regional acceptance, which remains pending for this package.
No new native hour was run in this Core phase. The canonical `publish/` was
not replaced, and no push was performed.

## Preserved failure and workload

The preceding clean `f64ccab` package retained two slow NTSC 600-field windows
at performance fields 35,410 and 36,010 (0.87322x and 0.84717x). Its PAL run
retained four slow windows. Its actual PAL hour also failed with 2,482
post-readiness muted fields and 301 steady/cutoff drops; its NTSC hour attempt
was interrupted by the user after 383.661 seconds. Neither result is replaced
or relabeled by the Core measurements below.

The new diagnostic checkpoint starts from that exact NTSC field9 state
(machine Frames=10), advances 34,800 fields with the original replay to
Frames=34,810, warms 600 fields and measures the exact failing 1,200-field
suffix, ending at Frames=36,610. Continuing the preparation to Frames=37,010
produces a byte-identical original field37009 state
(`3c21864096f0350ad4f1d72c07dd2172e0949a43c3e55fae974db6dce3f0d422`).
Preparation is outside performance timing. Input uses the pre-frame machine
Frames value, without shifting the replay or reducing the workload.

## Finding and implementation

Diagnostic counters found 225,057,940 APU RunUntil calls in those 1,200 fields.
203,786,119 ended before both the pending SPC/hold endpoint and the next DSP
boundary. Their old AdvanceCrystal call executed no DSP loop body. The small
common path now moves only Crystal directly; exact DSP endpoints retain the
original slow path, notification ordering, hold completion and timers.
Backward-time validation and isolated execution are unchanged.

PPU counters found 275,251,200 background samples, 137,625,600 screen selections
and 68,812,800 display conversions. Background sampling folds the selected bit
column of each fetched word using 0x0101, reading only the planes selected by
the live mode. The low byte holds planes 0/2/4/6 and the high byte holds
planes 1/3/5/7; shifting the high byte down by seven joins the eight bits
without allowing neighboring columns into the result. Render-position increments, stale fetched upper planes, mode
changes, mosaic and every hardware phase still execute. Screen selection
avoids pure array reads for disabled layers, retaining the existing strict
priority comparison and BG/OBJ tie order. Backdrop palette access still occurs
outside that selector. A 512-byte channel table preserves both integer rounding
stages of RGB555 brightness and ARGB expansion. No framebuffer is pooled,
no state schema changes, and no oscillator, bus effect or PPU phase is skipped.

Scoped sampled counters explain call frequency, not additive CPU percentages:
instrumentation changes inlining and adds timing overhead. An uninstrumented,
owner-TID-only EventPipe trace with full rundown still attributed implausible
stacks to PollGC/List.Remove under code that does not call List.Remove. That
trace is retained as a diagnostic limitation, not a GC or CPU hotspot claim.
Windows WPR CPU/DotNET recording was refused by OS profiling policy
(0xc5585011); no session started, elevation or host tuning was attempted.

## Paired evidence

Two AssemblyLoadContexts in one process execute equal 600-field warmups and
alternating 50-field AB/BA batches. Restore, setup and output hashing are
outside timed RunFrame plus DSP drain. Each variant performs the same 1,200
fields; instruction counts, clocks, complete state, video and PCM must match.
All comparisons use the private 10.0.402 SDK/runtime 10.0.13, whose actual
loaded CLR/JIT paths and hashes are recorded.

| Comparison | Total timed speed change | Median paired change | Faster pairs |
| --- | ---: | ---: | ---: |
| APU common path vs published f64 NTSC tail | +3.50% | +4.50% | 21/24 |
| Rejected APU carry lookup vs published f64 NTSC tail | -2.18% | -1.28% | 7/24 |
| Combined change vs published f64 NTSC tail | +2.94% | +4.03% | 22/24 |
| Combined change vs APU-only NTSC tail | +6.21% | +6.59% | 24/24 |
| Combined change vs published f64 PAL checkpoint | +11.57% | +11.49% | 24/24 |

The direct PPU isolation reduced thread cycles from 55.061B to 51.932B; the
PAL comparison reduced them from 64.985B to 58.223B. QueryThreadCycleTime is
not a retired-instruction counter. Different comparison series and regional
starting states cannot be combined into a universal effect size. Large changes
between separate long runs are not assigned wholly to these optimizations.
The rejected lookup and all earlier failed measurements remain retained.

## Fresh published strict gates

The isolated clean package and retained Lab load exactly the same Core DLL:
`e3274e8065f6d1f460367f53620c424e85c324ea684edacaee31e5bb1e57d1ba`.
Both original full workloads ran serially, once each, without instrumentation,
host tuning, threshold changes, source/dependency changes or omitted fields.

| Region | Measured fields | Emulated seconds | Overall ratio | Complete 600-field windows | Minimum window | Managed maximum | Drops |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| NTSC | 37,000 | 615.653 | 1.630535x | 61/61 pass | 1.220530x | 66,947,392 B | 0 |
| PAL | 31,000 | 619.913 | 1.641083x | 51/51 pass | 1.496305x | 65,242,872 B | 0 |

Every complete 600-field window meets the unchanged >=1.0x gate; the trailing
400 fields are reported separately. Largest drains are 533/640 samples,
below capacity 8,192. Video, PCM, compressed full state, SRAM, ROM, replay,
firmware, master clocks and DSP sample counts match the retained regional
baseline exactly. Actual CLR/JIT runtime13 identities, all 2,856 source/helper
files and 984 binary/dependency files were sealed before and checked after.
Both processes exited normally. These are measured Core gates, not guarantees
against future host scheduling or native presentation deadline failures.

## Tests and evidence identity

Core regression passes 139 groups, including the new two APU endpoint/hold
cases and four PPU groups: 1,048,576 color/brightness combinations, 12,288
actual random-neighbor pixel samples, 448 live-mode/stale-plane cases and 1,792
main/sub enable-mask and priority-tie cases. The PPU-only suite passes 49.
An initial new-test setup omitted PPU Reset and was rejected as an invalid
snapshot; the corrected setup keeps the same assertions and is retained.
SPC continuation and entry guards pass 9+9, full vectors and mixed continuation
pass 256,000 each; DSP1, GSU and SA-1 functional groups pass 30/34/32. All 14
frozen outputs, 18,952 suspension cases and 128 horizons remain exact.

Those focused tests used pre-commit Core
`62d845444bfd03787594db1c65961fc53377c891f58de6bc66bda4dde6c40dec`;
the clean publish changes generated assembly identity to implementation 6db,
whose exact e327 Core passed both long gates. A documentation successor does
not rebuild or replace that tested package. Final integration still owns full
supported regressions, fresh actual NTSC/PAL native hours and pushed-head CI.
Known raw hardware accuracy differences remain separately visible.

[r5-tail-core-evidence-manifest.json](r5-tail-core-evidence-manifest.json)
records source Git-LF and actual working-byte domains separately, commands,
reports, helper/runtime identities and retained local paths. Ignored evidence
under `artifacts/r5-tail-core/` stays local; it is not a bundled commercial ROM
or a downloadable CI artifact. Earlier `r5-final-integration/`, runtime/Core
phases and canonical packages remain intact.
