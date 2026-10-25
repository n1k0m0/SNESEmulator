# R7 bounded final integration

This source/package delivery is functionally checked within the scope below.
**Strict native realtime acceptance remains open.** No new owner remedy was
established, and this phase runs no long Core gate, native hour, raw accuracy
inventory or performance prototype. It changes documentation only.

## Current progress and remaining problem

[R5 exact-tail Core](r5-tail-core.md) passed 37,000 NTSC fields and 31,000 PAL
fields on its retained exact `6db7c8a` package: all 61/51 complete 600-field
windows passed, minima 1.22052989x/1.49630461x. Both subsequent actual R5 native
hours remained red: NTSC retained 19,242 post-ready muted fields and 2,562 drops;
PAL retained 103 and 17,846. Those outcomes do not transfer to a new binary.

[R6 presentation recovery](r6-realtime-runtime.md), `b6ba6ad`, corrects final-device
buffer recovery and video scheduling against the real driver playhead, preserves
source timestamps and raw Core PCM, and removes demonstrated private allocation
costs. Its actual normal NTSC300 session passed; PAL300 retained six muted fields
and eight timeline-reset drops. These are bounded observations, not new hours.
[R6 Core investigation](r6-realtime-core.md), `68bf59d`, rejected four optimization
prototypes, restored Core, and retained the CPU callback contracts/Core144 tests.

[R7 owner timing](r7-realtime-runtime.md), `1431b93`, independently corrects
extreme-deadline subtraction overflow and adds bounded opt-in owner evidence.
Measured owner CPU demand can exceed the native field budget. Wait precision
alone did not explain the recorded deficit; GC or JIT causality was not
established. Six PAL120 A/B/C/C/B/A codegen probes passed the two defaults; both
repetitions of each requested PGO-off/tiering-off variant failed. Effective CLR
settings were unobserved. All experimental
JIT/MMCSS policy was removed. Further owner work remains; no general guarantee
under arbitrary concurrent host load is made.

## Exact isolated package and functional checks

Package source is clean commit `1431b930020fcb9a0ee6840899fc1d5ac13b19ad` in a
fresh local clone. `scripts/publish-win-x64.ps1` produced the new self-contained
972-file Windows x64 package under `artifacts/r7-final-integration/package/`,
including App, Manager, Updater, dependency locks and licenses. Its manifest
records that exact implementation, `sourceDirty=false`, SDK10.0.402/runtime10.0.13.
This report and the current pointers are a documentation successor; they do not
cause a republish or change the package's implementation identity.

| Check | Result and scope |
| --- | --- |
| Runtime | 30 groups, exact published Runtime/Core assemblies |
| State and persistence | 27/15 groups, exact published Core |
| Portable network | 23 groups; NetworkAudioBuffer/NetworkTimeline source-linked into the clean1431 driver, not loaded from App |
| Windows integration/network | 11/24 groups with exact published App and shared product assemblies |
| Updater | Native packaged `verify` accepts its complete manifest |
| Generated own diagnostic/headless | 120 frames, 24,952 instructions, PC00:8091; repeated PPM and execution stats byte-identical |
| Self-contained runtime | Owned headless App image/creation verified; actual packaged CLR/JIT modules observed with SDK hidden from child PATH |
| Distribution | SDK-hidden launcher, native UI/runtime, disabled update, controlled localhost update, Manager, settings/SRAM preservation pass; package files unchanged |

Driver copies retain their original project/bin/Release/framework depth so
fixture paths remain valid. Their metadata reports the clean implementation
commit and empty working-tree status. Driver DLLs are separate test artifacts;
`exact-driver-bindings.json` records the published product assemblies substituted
beside them. Package and report identity maps are retained with the command logs.
The isolated source is clean after tests. No RID lockfile churn occurred, so no
lockfile restoration was required. The original workspace build outputs were
not used or replaced.

Core identity: `32c30fcc8a8ef58bac60576424b3afd41755afe486d5a0a522e95ebacb7f3e08`.
Runtime identity: `056dc71db57add950f24f8753ab9dd077c936fb9714f407e009d9656e3d332b9`.
App identity: `4f51a3d17707f11d68ceafc3441436edc8dd615015ae8bacd4de32419c2ab53d`.
Actual packaged CLR/JIT SHA256:
`18eed8c40d05fc455be95d12cc77eacf379265063b15ca84bd7a11cf29078a22` /
`2a2332c3c0cb023e71deb356b5fb85ce9045fb8434734dcfe9a7529e427dc858`.

All 42 Core files (39 C# plus project/two locks) have byte-identical Git blobs from
`6db7c8a`. The new Core DLL has different compilation metadata/path identity;
source continuity does not transfer the old exact-binary timing acceptance.
A supplementary identity helper completed the headless/module/binding checks,
then failed an incorrect count assumption (42 C# rather than 42 total files).
The retained count-recovery audit compared 42 exported archive files. The
byte-safe Git-blob proof below now verifies actual committed Core bytes with zero
content differences; neither audit issue was a failed product contract.

## Pre-push hash-domain correction

The independent audit of documentation commit `d80ccab` rejected 167 of its
233 canonical-source hashes (334 mismatches when checked against both the
implementation and documentation revisions). The earlier `git archive` helpers
emitted Windows export/CRLF bytes and incorrectly labeled them canonical Git
blob/LF bytes. The raw artifact, package and functional-report hashes passed.

The replacement `hash-domain-correction/git-blob-proof.json` uses binary
`subprocess.check_output(["git", "show", "<commit>:<path>"])` for every content
payload. It verifies all 233 source entries and all 42 Core files without text
decoding or newline conversion, preserving the CP1252 bytes in `Apu.cs`.
The current evidence manifest uses these actual Git-blob SHA256 values and
references this new continuity proof. It makes no blanket UTF-8/LF assumption
about committed source blobs.

The original `d80ccab` report/manifest are retained byte-for-byte under local
`artifacts/r7-final-integration/hash-domain-correction/`; the original archive
helpers, `core-source-continuity.json`, handoff and failed root audit remain
unchanged. They are historical export-byte evidence, not the corrected Git-blob
proof. This narrow evidence correction rebuilds/retests no product, transfers no
timing acceptance and changes no original package or functional result.

## Delivery boundary and limitations

The desktop launcher still selects historical `publish/` implementation
`affae58`; its Core remains
`b02494f5c5cea9c4790d8e84d0e97eeaf7e9a43c948a1ff2be9523015f4debd1`.
The new package stays isolated. No canonical activation, public release or push
is performed by this phase; the original package and user ROM are preserved.
Actual native UI tests are functional probes, not physical display/DAC/gamepad
certification. The generated diagnostic is not commercial-game coverage.

The earlier full suites and raw CPU/SPC/DSP/chip inventories retain their original
scope, including 246/264 CPU raw differences, 7,000 SPC differences, the original
18 whole-console audio failures, GSU FMULT-R4 and seven SA-1 timing-row differences.
No hardware clock, state format, threshold, historical fixture or original
report was changed. No foreign workload or whole-host setting was interrupted.
The browser-crash workaround remains in force; no browser automation was used.

Exact-head hosted Windows/Ubuntu/full external CPU/SPC/package CI is a separate
post-push check. No future run is advertised as passed here. See the
[evidence manifest](r7-final-integration-evidence-manifest.json) for separate
canonical Git-source, raw local artifact and stable LF document hash domains.
Local artifacts are ignored and are not supplied by a fresh source checkout.
