# Final integration: tested implementation and remaining release limits

The latest [exact-tail Core follow-up](r5-tail-core.md), implementation
`6db7c8a`, now passes fresh 37,000-field NTSC and 31,000-field PAL sustained
gates with every complete 600-field window above real time. Actual native
hour acceptance remains pending for that package. The intervening f64 PAL
hour failed and its NTSC attempt was user-interrupted; both are retained in
the new report. The affae package and outcomes below are historical evidence.

The earlier [R5 core follow-up](r5-realtime-core.md) reduces allocation and
empty-observer overhead. Its bounded tests do not replace the historical
package/long/native outcomes recorded below.

This report records the actual final implementation at
`affae58f1b4ca127b2d14865569615334f6f8856`, rather than transferring acceptance
from earlier binaries. The final documentation commit is a successor of that
implementation. The delivered package preserves the tested implementation's
identity; a documentation-only commit does not trigger another publish.

The functional follow-ups, comments, documentation and cartridge implementations
are present. At that historical implementation, the strict sustained-performance
and native-hour release gates had **not** earned acceptance. A green functional
suite or hosted workflow does
not override these independent timing failures, nor establish complete SNES
hardware accuracy.

## Functional and distribution evidence

* The clean final Ubuntu default laboratory completed in 779.209 seconds. Windows
  retained the earlier complete prefix and reran the corrected final tail,
  negative controls and all frozen comparisons; this segmented local execution
  is distinguished from the exact-head complete hosted runs on both systems.
* Each system additionally executed 5,080,400 CPU semantic cases, 5,080,400 CPU bus
  cases, 256,000 SPC vectors and 256,000 mixed continuation cases. The frozen
  checks retain 14 outputs, 18,952 resumptions and 128 horizons.
* Current SA-1, SuperFX and DSP1 functional suites contain 32, 34 and 30 public
  groups respectively. Local-firmware DSP1 checks contain 35 groups. Local
  unmodified SuperFX programs retain 30 passes and the known FMULT-R4 red;
  the original SA-1 program retains all seven photographed-rate differences.
* The exact self-contained Windows package passed 11 distribution, native,
  quoted-launcher, update and negative-shutdown probes with the SDK hidden.
  Isolated TEST-AppId install, repair/update and uninstall preserved SRAM,
  State9, settings and Manager metadata. This is an isolated test installation.
  An initial 268-character installation path failed and remains documented;
  the identical installer passed in a shorter directory containing spaces.
* Exact implementation-head hosted CI
  [40100657828](https://github.com/n1k0m0/SNESEmulator/actions/runs/40100657828)
  completed successfully. All four required jobs passed. The opt-in self-hosted
  native job was skipped and supplies no native GUI evidence. Root separately
  classified all 223 suite reports, retaining declared mutation/watchdog/raw
  accuracy reds instead of counting every report as a distinct acceptance group.

## Sustained performance

The exact package Core SHA256 is
`b02494f5c5cea9c4790d8e84d0e97eeaf7e9a43c948a1ff2be9523015f4debd1`.
The retained Lab loaded those exact Core bytes using the private SDK 10.0.402
and observed runtime/JIT 10.0.13 modules. Source, helper, firmware, ROM and
binary identities were checked; no host power, affinity or priority changes
were made.

| Initial final-source test | Fields | Overall ratio | Minimum complete 600-field window | Red complete windows |
|---|---:|---:|---:|---:|
| NTSC | 37,000 | 1.074435 | 0.868849 | 23/61 |
| PAL | 31,000 | 1.044662 | 0.931661 | 15/51 |

Both runs exceed 600 emulated seconds. Their average being above real time is
insufficient: every complete 600-field window must reach 1.0. The final
400-field tails are reported separately. Presentation drops remained zero and
managed memory remained bounded, but the strict timing gate is red.

After a user-reported PC crash interrupted an old-core comparison, fresh serial
current/old/current runs used byte-identical Performance.cs, OwnIPL, ROM and
private runtime. Their final video, PCM and state hashes agreed. Current first
measured 1.361050 overall (minimum 0.983484, 2/61 red windows), historical
`97a05a7` measured 1.008178 (minimum 0.599846, 24/61 red), and repeated current
measured 0.949522 (minimum 0.793185, 44/61 red). This wide variation does not
identify a reproducible code regression or a crash cause. It also does not clear
the release gate. No speculative timing optimization or weakened predicate was
used to obtain a pass.

## Actual native hours

Both regions completed their actual 3,600-second measurement cutoff. PAL passed;
NTSC did not pass because probe readiness exceeded 30 seconds and actual
post-ready catchup muted audio and discarded remote presentation. Probe
readiness describes the measurement's stable unmuted observation contract, not
game loading or the time before the application first shows a picture.

| Region | Strict result | Probe readiness (s) | Post-ready muted fields | Steady / cutoff remote drops | Maximum software A/V (ms) | Native reported wall (s) |
|---|---|---:|---:|---:|---:|---:|
| NTSC | red | 40.044942 | 288 | 127 / 127 | 18.549757 | 3600.958026 |
| PAL | pass | 8.054571 | 0 | 0 / 0 | 16.801781 | 3600.427170 |

Native reported wall includes cleanup; it is not a pure active-body duration.
The full-hour proof is the requested 3,600 seconds plus the completed source
cutoff (`now >= seconds`), no forced stop, and retained complete result. The
separate process walls were 3,601.532 seconds NTSC and 3,601.359 seconds PAL.
Actual App and Manager runtime/JIT modules were observed near the end at
3,595.500 / 3,595.328 seconds respectively. Both owned Managers exited normally,
with no wrapper errors, changed diagnostic sources or changed dependencies.

NTSC retained 288 post-ready muted fields and 127 dropped remote fields, also
visible in the later cutoff snapshot. Detail-event storage explicitly omitted
2,062 older change events; cumulative counters remain complete, so retained
event detail is not used to erase the failures. PAL retained zero post-ready
muted fields and zero remote drops, including that later snapshot. Both regions
had zero nonzero source PCM pairs and zero different-left/right source pairs;
this is silent PCM count/hash evidence, not audible stereo evidence.


The native probes use the actual published application, its packaged WPF
Manager, real local TLS transport and WinMM. The software A/V contract is at
most 40 ms, with the original 100 ms catchup and 30-second readiness limits.
Acceptance additionally requires zero post-ready muted catchup, zero remote
presentation drops including the later cutoff snapshot, zero observation gaps,
no output mismatch or device overflow, bounded memory, unchanged source and
package hashes, and actual App/Manager runtime module observations near the
end (at least 3,585 seconds). Each region must actually measure 3,600 seconds;
a short diagnostic cannot substitute for this.

These checks measure software timing. The supplied OwnIPL demonstration can
produce silent PCM. Matching PCM counts/hashes do not demonstrate audible stereo,
physical DAC output, original-console timing or commercial-game compatibility.

## Preserved harness failures and recovery

The earlier CPU child-proof output-prefix mismatch, optional full-cache
assumption, and sample-SPC exit-code expectation were repaired without weakening
existing raw contracts. The offline CPU input is an exact licensed 632-byte
upstream object with provenance and mutation rejection. Original failure logs
remain available alongside the corrected runs.

Publishing initially failed in an inherited Windows PowerShell module search
path after compilation; only the owned child environment was corrected. The
first partial package and hashes remain retained. The interrupted pre-crash
comparison is labelled interrupted, not passed or failed. A recovery comparison
producer completed before its wrapper mistakenly queried process image identity
after exit; that producer and wrapper red are separate. The completed producer
report and CPU samples survive, but its final exit code and live runtime-module
observations were not saved before that wrapper exception. It is diagnostic
producer evidence, not a passed wrapper gate. The corrected wrapper captures
identity while the retained process handle is live; the later baseline and
current runs retain exit codes and actual module observations.

The first native wrapper rejected before application launch because its manifest
used POSIX path keys and the Windows dictionary used backslashes. All 983 file
hashes were unchanged and all keys unique and relative. The recovery runner
canonicalizes only that separator representation and uses a new source seal
containing its own hash; the original runner/rejection are retained. No product
source or native acceptance predicate changed.

## Local package activation

The workspace launcher now selects `publish/`, an exact copy of the 972-file
tested package with matching file-set and SHA256 identities. The complete
previous 972-file package is preserved at
`artifacts/final-integration/previous-publish-preserved/` with its original
inventory unchanged. The original test-evidence package is also retained.
User ROMs and external SRAM/state/settings were not replaced. This activation
does not claim release acceptance for the timing failures above.

## Accuracy limits

The 246 raw CPU semantic differences, 264 raw CPU bus differences, 7,000
adjudicated SPC bus differences, 49 raw DSP1 hidden-DRS differences, 112 raw GSU
R14/busy-hook differences, unresolved source-author FMULT-R4 case, seven original
SA-1 rate differences and all 18 original full-sequence audio reds remain
separately visible. Manufacturer/source conflicts and latency assumptions are
explicitly scoped in the chip reports. No original console was measured by this
project. Unsupported enhancement chips and peripherals remain unsupported.

See [R2](r2-performance-soak.md), [R3](r3-accuracy.md),
[R4](r4-readability.md), [DSP1](m10a-dsp1.md),
[SuperFX](m10b-superfx.md), [SA-1](m10c-sa1.md), and the
[test laboratory](test-laboratory.md).

[The final evidence manifest](final-integration-evidence-manifest.json) binds the measured source, package and local reports to exact hashes. Ignored `artifacts/` paths describe retained local evidence; they are not bundled fixtures or downloadable CI artifacts. The final documentation successor preserves these historical identities.
