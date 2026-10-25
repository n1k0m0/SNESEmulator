# R6 remote presentation clock recovery

This phase fixes two reproducible remote presentation failures: a fixed 3:2
resampler retains clock drift or padding latency, and a wall-clock video deadline
can subsequently hold pictures behind the real audio driver. It also removes two
measured allocation sources. Complete native-hour acceptance remains open.

## Demonstrated mechanisms

The previous R5 final package completed both strict long Core gates, but its
actual 3,600-second NTSC and PAL sessions failed. NTSC readiness was 43.277 seconds
and retained 19,242 post-ready muted fields and 2,562 drops. PAL readiness was
7.465 seconds, with 103 muted fields and 17,846 drops, including 17,797
DistantVideo drops. Old rejected source/driver coordinates were not retained.
One delivered historical maximum was source-ahead by 39.968 ms; that single point
does not establish the direction or cause of every historical rejection. All
original red reports remain unchanged under `artifacts/r5-final-acceptance/`.

New deterministic 400-second fake-driver comparisons reproduce persistent
latency with a driver 500 ppm slower, a driver 500 ppm faster, and one 139 ms
transport interruption followed by exactly nominal clocks. The fixed slower
case retained 5,004 DistantVideo drops and eventually overflowed; the fixed
faster case accumulated padding. At equal nominal clocks, interruption latency
remained about 50 ms higher. Adaptive presentation recovers without discarding
PCM, rebasing timestamps, concealing padding, or changing a hardware clock.

An explicit actual WinMM fixture then injects a 32,016-Hz source (+500 ppm) and
160 ms prefill. It is a presentation diagnosis with zero gain, not a SNES
hardware profile or a full TLS session. The fixed provider first rejected at
147.978 seconds: source 147.96, driver 147.918923177, signed +41.077 ms. Its
8,000-native-frame source budget overflowed, ending the process after 167.703
seconds. No final result exists for that aborted run; the original exception,
owned-process record and first rejected coordinates are preserved.

ASRC alone avoided overflow but exposed a second error: the old host due time
held video while the real source playhead advanced. That 320-second trial was
quality-red, with 5,225 DistantVideo drops and a 40.134 ms original AV maximum.
Its first rejected point was source 200.26 versus driver 200.300039573, signed
-40.040 ms. Its old exit 0 meant only that no fault was returned; it is not a
quality pass. These controlled signs must not be assigned to historical drops.

After the video-clock correction, the same 320-second adaptive fixture completed
in 320.391 process seconds, exit 0, explicit `QualityAccepted=true`, 16,002 videos
and zero drops. The original AV metric was 8.750406 ms; the additional durable
observer maximum was 8.896168 ms. Final padding was 86 actual driver frames,
and final source buffering was 812 native frames. The original 40 ms policy
was unchanged. `native-clock-processes.json` and `native-clock2-processes.json`
retain the red and corrected trials separately.

## Implementation and unchanged contracts

`NetworkAudioOutput` now enables a bounded final-provider rate controller.
`NetworkAudioBuffer()` still exposes its original parameterless constructor and
fixed 3:2 behavior. Adaptive mode targets 640 native frames (20 ms) after the
current driver block, filters queue error over two seconds, and applies
`filteredError / 64000`, limited to +/-0.5%. The phase step is quantized against
3,000,000 and records the actual interpolated stereo position. Zero error
produces exactly the legacy waveform and source mapping. Padding has no source
coordinate; clearing resets maps/controller and retains cumulative padding.

A valid driver source playhead is now the authoritative video clock. Audio and
clockless video retain the original 40 ms host due time. Video admission remains
source <= playhead + 8 ms; commit distance >=40 ms is still rejected. The
250 ms unknown/future-clock wait, capacity 32, generation invalidation,
100 ms owner catchup/muting boundary and 30-second readiness limit are unchanged.
Signed wall scheduling error can be negative and remains visible; its original
maximum metric is still for audio or clockless video. Both commit and completed
actual driver comparisons remain unmodified, including callback/scheduling cost.

Private audio mapping spans are value records rather than a newly allocated
class record for every output frame. Lookup uses a bounded binary search over
8,192 retained spans; lost maps remain unknown. No public PPU plane is pooled or
recycled. Server video packing borrows an internal read-only span for encoding,
then produces one owned byte array. Public null/invalid-geometry behavior and
VID3 bytes remain exact; 256/512 by 224/239 are supported and 448/478 are rejected.
The UI upload-array lifetime is unchanged. State 9/10, hardware timing, source
PCM, AUD2, VID3 and immutable PPU plane ownership are unchanged.

## Bounded diagnostics and tests

Diagnostics are opt-in. The first rejected point and then at most one per ten
seconds are durably exported, capped at 2,048 rows. Each row contains original
absolute Stopwatch-domain arrival/due/sample/commit/completion coordinates,
signed source-minus-playhead, rejection reason, actual driver cursor/status,
rendered/padding/native-buffer frames and mapping/controller details. Cadence
and capacity omissions are counted separately; every rejected export decision
is accounted. The existing cumulative classified drop counters remain complete.
Ten-second samples add monotone total GC pause, collection index/generation,
last pause durations and total allocation. These measurements do not attribute
a historical failure to GC. Export is never per-frame JSON at 50 Hz failure rates.

The diagnostic fixture retains the global maximum observation and first 40 ms
violation beyond the bounded final ring. Normal soak retains cold/steady maximum
observations and observation-gap counts. Explicit fixture quality acceptance
now controls exit status; earlier misleading exit0 evidence remains unchanged.

Final focused verification passes: portable network 23, Windows network 24,
integration 11, runtime 27, state 27 and persistence 15. Regression tests cover both
drift signs, nominal padding recovery, exact 3:2 output, adaptive interpolation
and stereo order, null padding, reset, driver wrap, source/capacity budgets,
both driver/host-deadline directions, original 8/40 ms boundaries, missing clocks,
stale generations, video-only/mute/turbo fallback, exception reasons and export
accounting. The frozen Core comparison matches 14 complete-machine cases,
18,952 SPC continuation cases and 128 deadline horizons. These are regression
oracles, not additional original-console accuracy evidence.

An exact final-package microbenchmark separates allocation improvements from
clock recovery: over 250 represented device seconds, old mapping records allocate
about 576 MB; fixed and adaptive value spans allocate 40 measurement bytes after
warmup. Final-packaged adaptive runs take 0.323/0.118 seconds versus fixed
0.102/0.109 seconds; JIT/order/host work vary, so this is a bounded cost observation,
not a broad speedup claim. Packing 1,200 fields falls from 550,560,088 to 275,280,088
allocated bytes (50%). Packing CPU timings vary and do not prove a speedup.
These local allocation effects do not by themselves establish a historical
native-hour cause. The benchmark uses the exact final App DLL.

## Final normal-profile evidence

The unchanged-clock full-pipeline NTSC repeat completed successfully: readiness
10.7960442 s, App exit 0, Manager exit 0, process duration 301.329 s, native report
duration 301.016 s, zero post-ready muted fields/drops and maximum AV distance
12.644020 ms. Source/dependency hashes were unchanged, wrappers reported no
errors and early/near-end App/Manager runtime identities matched the private
runtime. Its package App SHA256 is
`80ba9280a0ef81a1cb53dca155b55aa2527afcfc5c1f1871c76fd5d21b1846a0`.

The preceding NTSC attempt ended after 241.100 native seconds. The user explicitly
confirmed closing that current window; its original failed reports and a
separate trusted-answer provenance file remain preserved. It is an incomplete
non-pass, not evidence of a software lifecycle defect.

PAL remains red. The process ran 301.312 s and its Manager exited normally, but
an invalid Bitmap parameter in the final screenshot prevented the old code from
writing result.json. The retained last sample at 291.126 s already proves six
post-ready owner-muted fields and eight TimelineReset drops. Readiness was
11.6025843 s; the last retained original AV maximum was 29.882411 ms. No final
whole-run maximum is asserted without a final report. There were no sampled
DistantVideo/LateAudio/MissingAudioClock drops, source-drift increments, source
ARGB mismatches, observation gaps or owner-output drops after readiness. The
owner failure remains open; this phase does not relabel PAL as accepted.

The episode is bounded by samples 191.0666014--201.0671962 s: cumulative GC pause
increases by 75.749 ms over that ten-second interval and allocation by
946,789,600 bytes. Collection index increases 2111 to 2231; the last observed
GC pauses are 0.230/0.351 ms. Maximum UI interval remains 131.1514 ms, while
endpoint sampled pacing is 0.525/0.258 ms. These samples cannot attribute the
brief owner catchup to one long GC pause or external work. Original source rows
and checkpoint-0.state are retained for the subsequent Core investigation.
The asynchronous render input history is not a deterministic replay oracle.
An unproven profiling lead is remaining PendingCpuCycle/read-copy/journal
allocation; existing cached pins, interrupt samples and register/idle caches
must not be claimed as fresh allocation sources or removed carelessly.

A diagnostic-only successor now records actual screenshot dimensions, counts
capture failures without throwing past cutoff, persists the first owner-catchup
point and bounded error rows immediately, and catches native-run exceptions so
reports still finish. Tests cover missing/zero/negative dimensions, normal
captures and actual capture exceptions. A controlled ten-second run minimized
only its owned window at 8.156 s: start capture 1024 x 576 succeeded; final client
size was actually 0 x 0. The full result was written with 569 fields and final
metrics, one counted screenshot error, Success=false and exit 1; Manager exit 0.
This demonstrates the reporting fix without assigning a minimization cause to
the earlier PAL failure, whose dimensions were not exported. The report-only
successor App SHA256 is
`272564499649d3ceef3a1ac23fd905424a8aba3664313c3d9f094ff16b60650d`.
The short injected failure is not a normal-profile acceptance run.

All 42 tracked Core files match the base commit after LF normalization. Both
final and report-only packages have Core SHA256
`f7671b455afb27f68cfafef099db35056e51d71103dfcbf7265a6b7734dfaf79`.
Its PE identity differs from the earlier historical Core because its compiled
informational commit is the newer base; source equality and the frozen state/
trace comparisons establish unchanged hardware implementation. The prior long
Core gates were green; no new sustained gate is claimed in this runtime phase.

## Reproduction and identity boundaries

All local reports, failed prototypes, helper sources, logs, sealed source/binary
identities and isolated packages remain under `artifacts/r6-realtime-runtime/`.
The [evidence manifest](r6-realtime-runtime-evidence-manifest.json) pins exact
retained artifact bytes and LF-normalized implementation sources. Ignored local
evidence is not bundled or redistributed in Git. No user ROM or canonical
`publish/` file was replaced.

The measured source boundary is dirty over base commit
`db47880186662494a37af1890e75731e5e0be0ca`, including newly staged diagnostic/tests.
Compiled informational revision remains that base, not the later implementation
or documentation commit. Original source seals retain raw Windows bytes;
LF-normalized identities distinguish Git checkouts. Final documentation was
written after measurements. Earlier diagnostic/candidate/candidate2 packages are
separate: candidate2 contains both functional clock fixes; the final package
adds explicit export omission accounting and delivery-failure reasons.
The report-only successor then changes only SoakSmoke diagnostics; its own
source/binary seals and injected-error test are separate from normal 300-second
evidence. The final test file also has an EOF-whitespace cleanup after those
normal measurements; it does not change tested semantics.
No earlier source seal is relabeled as the final source.

Use private SDK 10.0.402/runtime 10.0.13, process-local DOTNET_ROOT/PATH:

```powershell
dotnet build SNESEmulator.Core.Tests -c Release
dotnet SNESEmulator.Core.Tests/bin/Release/net10.0/SNESEmulator.Core.Tests.dll --network-only
dotnet build SNESEmulator.Tests -c Release
dotnet SNESEmulator.Tests/bin/Release/net10.0-windows/SNESEmulator.Tests.dll --network-only
python tools/test-performance.py --output artifacts/<fresh-output>
./scripts/publish-win-x64.ps1 -OutputDirectory artifacts/<fresh-package> -DotnetPath <private-dotnet.exe>
<fresh-package>/SNESEmulator.exe --audio-recovery-smoke <fresh-output> 320 adaptive
<fresh-package>/SNESEmulator.exe --ui-soak <fresh-output> <own-ROM> 300 <fresh-package>/Manager/SNESEmulatorManager.exe ntsc
```

The retained Python wrappers record exact original command arrays, owned image/
creation/handles, early and near-end loaded App/Manager CLR/JIT identities,
source/dependency before/after comparisons and actual process duration. Only
owned processes are cleaned up; concurrent unrelated work remains untouched.
No priority, affinity, power, timer, browser or host-idleness changes were made.

A clean final integration package, unchanged sustained Core gates, both actual
3,600-second native regional sessions and final pushed-head CI are still
required. Short native success or controlled fixture success cannot replace
those gates. Known raw hardware-accuracy and full-sequence audio reds remain
separate; no original-console certification is claimed.
