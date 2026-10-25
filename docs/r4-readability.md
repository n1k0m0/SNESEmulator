# R4 readability and current documentation

R4 adds comments explaining timing, ownership and failure invariants, and updates
current status without rewriting historical measurements. It does not implement
new hardware behavior or close the known accuracy failures.

## Changes and scope

The CPU comments explain decoder replay, saved interrupt samples and exactly-once
MMIO effects. Clock comments distinguish stable event ordering, individual
`EventObserved` callbacks and the later `PeripheralAdvanced` completed-timestamp
boundary. They also explain why rational APU remainder carry is serialized.
PPU comments identify the next-line OBJ buffers and native pixel/brightness
commit. APU/SPC/DSP comments explain half-cycle port holds, private journal
reconstruction, Own-IPL timing limits, phase clocks and echo's shared-RAM effects.
State/persistence comments explain independent temporary machines, capture/write
ordering and preservation of a valid backup. Runtime/network comments distinguish
completed hardware work from omitted presentation and disjoint drop categories.
The generated SPC decoder is unchanged.

The only changed executable token is one SoakSmoke diagnostic string. Its
predicate requires received audio pairs, not nonzero sample amplitude; the
message now states that distinction. Predicate, counters, limits and behavior
are unchanged. The documentation also corrects the session handshake to the
implemented `0x534E0004`, current state format to 9, full-height FIT/STRETCH,
R1 remote CI status, R3 raw CPU differences and the remaining final gates.

## Actual validation

The implementation/documentation commit is `d3627d6`; the completed-timestamp
comment precision is `5f6b866`. Both solution builds passed using the private
SDK 10.0.402 (runtime 10.0.13). Each build reported six NU1900 warnings because
NuGet vulnerability metadata could not be reached; package restore/build did
not fail. This is not a successful online vulnerability audit.

SDK Roslyn compares every non-trivia C# token's kind and exact text in all twelve
changed C# files against Git blobs from
`34620bfd3ddbdb4473ad10d24f93c9ee63673ba2`. It permits exactly the one diagnostic
literal described above; all other executable tokens match. The comparison
passed before and after the final comment precision. This establishes the scope
of the source change, not binary identity across rebuilt assemblies.

Fourteen serial checks on clean `d3627d6` passed: solution build, token proof,
M8/M9/R3/PPU/video/Gaussian immutable-input verification, exact SPC generator
check, and these targeted suites:

| Suite | Passing groups |
|---|---:|
| Runtime/controlled deadline and ownership | 24 |
| Portable network/queue contracts | 16 |
| Persistence/failure/recovery | 15 |
| CPU pointer and old-state compatibility | 4 |
| Isolated DSP timeline and negative controls | 6 |

The five suites total 65 groups. On clean `5f6b866`, the token proof and complete
solution build passed again; the five suites remain attributed to `d3627d6`.
No full million-vector, native visual, Ubuntu or hour-long run was repeated for
these comment/documentation changes. Final integration will test its own source
and binaries after M10. New report hashes and source identities are in
[the local evidence manifest](r4-evidence-manifest.json); large local reports
remain under `artifacts/r4-readability/`.

The first attempt to build the isolated Roslyn helper used an incorrect relative
assembly path and failed before token analysis. Its log/report are retained.
Using the SDK's `MSBuildSDKsPath` resolved the helper references; no product code
or fixture was changed to obtain the passing comparison.

## Ordered work after this historical stage

M10 proceeds DSP1, then SuperFX, then SA1 with source-supported behavior, mapping,
timing, states and tests. Remaining peripherals/revisions need explicit scope.
Final-source core duration checks, strict native 3600-second NTSC then PAL,
Windows/Ubuntu regression, native published-package verification, exact remote
CI and the final commit/push are still due. The 18 original full-sequence audio
rows remain red; reset alignment and unmeasured physical hardware remain open.
Historical R2 passes and older `publish/` identities do not certify future builds.

The ordered chip stages and final validation have since run. See [final integration](final-integration.md) for functional/package/CI acceptance, the passing PAL native hour, and the independent NTSC native/core timing failures.
