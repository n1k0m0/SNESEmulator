# R5: native realtime investigation and owner checkpoint pauses

The subsequent [exact-tail Core phase](r5-tail-core.md) clears both sustained
regional Core gates. Native hours remain pending for its package. The later
f64 PAL hour failed and its NTSC attempt was user-interrupted; the bounded
native results below remain historical evidence, not hour acceptance.

The historical bounded native checks pass: NTSC for 360 seconds and PAL for
180 seconds, with the original 30-second readiness, 100-ms catchup/mute and
40-ms software A/V limits. Both have zero actual post-readiness muted catchup
fields, zero steady remote drops and zero later measurement-cutoff drops.
These are bounded checks, not the final 3,600-second regional acceptance.

## Finding and implementation

The earlier NTSC hour's 127 steady remote drops were all timeline-reset
omissions; late-audio, distant-video, capacity and delivery-failure counters
were zero. Owner catchup/mute transitions were upstream of those resets.
There is no evidence from that run of an ordinary playout-distance failure.

A fresh 180-second NTSC baseline using the preceding R5 core already passed
its bounded strict native checks. This does not retrospectively clear the
[earlier sustained and native-hour failures](final-integration.md). The new
capture optimization below is not claimed to explain those minute-scale
failures or improve steady emulation throughput.

The concrete owner-thread cost found here was `Ppu.CapturePipeline`: it encoded
and decoded its complete snapshot with JSON, including completed picture planes,
then the savestate writer immediately serialized that detached result again.
It now copies mutable arrays and scalar tile/fetch/descriptor objects directly.
Nested video samples and completed picture planes remain detached; immutable
geometry may be shared. This reduces the work that holds up the next hardware
field when the host captures a checkpoint. Public snapshot values, serialized
State9/State10 representation and hardware timing are unchanged.

The implementation comments identify this ownership boundary and the scalar
copy helpers. Tests traverse the complete mutable snapshot graph, check that
independent captures share no mutable nodes, retain snapshots across live latch
changes, and mutate captured arrays/objects to verify that the owner is intact.
Eighteen frozen JSON hashes were captured from the previous implementation at
arbitrary NTSC/PAL midline and field cuts before changing it. All still match,
as do the original checkpoint's JSON and full uncompressed state hashes.

## Matched capture measurement

The same probe and field-6600 game snapshot were used in serial B/A/A/B fresh
processes: candidate, baseline, baseline, candidate. Each process warmed each
mode with 20 captures, then measured 300 captures. Actual loaded CoreCLR/JIT
paths and hashes identify the private Windows runtime 10.0.13. No host priority,
affinity, power, timer or global runtime setting was changed for these checks.

| Observation | Baseline, two processes | Direct copy, two processes |
| --- | ---: | ---: |
| Pipeline capture median | 4.5028 / 4.4659 ms | 0.0315 / 0.0304 ms |
| Complete state capture median | 5.0898 / 5.3086 ms | 2.5560 / 2.5696 ms |
| Complete state allocated bytes/capture | 11,746,849 / 11,746,544 | 8,503,616 / 8,503,370 |

Complete capture allocation falls approximately 27.61%. These are capture
costs, not field-throughput ratios. Every measured process produced identical
pipeline JSON and complete raw-state hashes. The checkpoint SHA256 is
`76f62bde5fbe4edb70d1c9181fb64ec5c41cafce990f6b5d4699a7ec99df9b0d`.
The probe and checkpoint are retained local evidence, not downloadable fixtures.

## Native and functional verification

| Native run | Requested cutoff | Probe readiness | Post-ready mute/drop | Maximum software A/V |
| --- | ---: | ---: | ---: | ---: |
| Previous-core NTSC baseline | 180 s | 7.2902 s | 0 / 0 | 7.936 ms |
| Current NTSC candidate | 360 s | 10.9473 s | 0 / 0 | 7.937 ms |
| Current PAL candidate | 180 s | 6.4295 s | 0 / 0 | 7.937 ms |

Both current runs used a fresh isolated self-contained package, actual normal
hardware pacing, localhost TLS observer, WinMM providers and owned WPF Manager.
Native NTSC reported 360.1427 seconds including cleanup; its owned process ran
360.3900 seconds. PAL reported 180.2805 seconds including cleanup; its process
ran 181.2190 seconds. The requested cutoff and completed runner establish the
measurement duration; these wall totals do not isolate cleanup time.

App and Manager actual runtime modules were observed at 355.359 seconds for
NTSC and 175.188 seconds for PAL. Source/helper and package dependency identities
matched before/after. Both applications and owned Managers exited normally;
there were no wrapper errors, forced cleanup, observation gaps, pixel
mismatches or owner-output/device-overflow losses. NTSC completed checkpoints
at 15.197 and 315.064 seconds with no subsequent quality-counter increase.

Cold intervals are retained: candidate NTSC had 539 muted fields and 127 drops
before readiness; PAL had 254 muted fields and 69 cold drops. Those counters were not reset or hidden.
The candidate does not establish faster cold readiness than the baseline.
NTSC's source PCM was silent; PAL counted 6,273 nonzero pairs and 6,260 differing
stereo pairs. Device gain was zero in this workload. These software observations
do not certify audible output, physical DAC/display latency or real SNES hardware.

Focused Windows checks passed 133 core groups, 27 runtime groups, 27 state
compatibility groups and 16 portable network groups. The unchanged frozen
regression contract passed 18,952 SPC resumptions, 128 horizons and 14 complete
console outputs; its command took 112.641 seconds. The 45-group PPU subset also
passed. No raw or golden fixture was rewritten. All measured current package,
Lab, tests and capture-probe sibling Core DLLs had SHA256
`9fd84f45413159b12af00d248ce1469bc5670e823e3bd02a42776937b7c97a61`.

## Evidence boundaries and remaining acceptance

The [evidence manifest](r5-realtime-runtime-evidence-manifest.json) records exact
local report/helper/binary byte hashes and LF-normalized source identities.
The implementation was measured on top of
`9db216a1a35d1ae2a8cf0e68c44a2be5594e4cc7`, before this bounded commit. Its
compiled informational commit therefore remains that base, not a later commit.
The package was marked source-dirty with the five measured implementation/test
files. A clean final integration package needs its own identities and checks.

The initial direct baseline publish temporarily added RID entries to six
portable lockfiles. Its native baseline source seal truthfully records that
dirty boundary. Those generated changes were restored afterward; exact original
bytes match the tracked RID locks and are retained separately with an audit.
The candidate used `scripts/publish-win-x64.ps1` with the locked RID files.
An initial targeted PPU command wrote its default `artifacts/m5-targeted` report;
the new result is preserved in this phase directory. An audit of all 26 then
committed manifests found no historical reference to that overwritten default
path. Later checks explicitly used separate report destinations.

Local helpers, source/dependency seals and original reports are under
`artifacts/r5-realtime-runtime/`; ignored artifacts are retained on this machine,
not distributed in the repository. Reproduction commands are recorded in the
manifest and local command reports. The canonical `publish/` package was not
replaced in this phase.

Final integration still must build a clean package, run full supported
regressions, the unchanged 37,000-field NTSC and 31,000-field PAL sustained
checks, and actual 3,600-second NTSC and PAL sessions, then audit the pushed-head
CI. Earlier red evidence remains historical and must not be relabeled green.
Known raw CPU/SPC/chip and full-sequence audio accuracy differences also remain;
this performance work does not establish complete hardware accuracy.
