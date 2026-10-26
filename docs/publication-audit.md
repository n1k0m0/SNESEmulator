# Publication and redistribution review

Reviewed October 26, 2026 before changing the source repository from private to
public. The owner authorized public visibility conditional on this review.
The scope is the reachable source history and proposed Windows preview assets,
not ignored local research files or user-owned media. This record documents the
technical/license evidence reviewed; it is not a legal guarantee about every
possible right or future contribution.

## Source and history

The initial clean baseline `02f67b84f395e6a15c35dd7d34231c9401465f62` has 118
reachable commits, 3,111 objects and 2,393 blobs. The byte-based
`tools/audit-publication.py` scan examined 56,386,715 raw bytes and 270,471,493
bytes including gzip/ZIP contents. Its 35 binary inventory entries consist of
the licensed/authored laboratory programs/data and three own diagnostic UI PNGs.
No executable, library, private-key container or commercial `.smc` image occurred
in that inventory. Current source and the final release commit are scanned again
before publication; the initial counts describe that historical baseline only.

The local 64-byte original IPL and user-local Super Mario World image were used
as exclusion probes, without copying them into the audit report or repository.
Neither occurred in Git blobs, decoded gzip/ZIP data or source hex-array bytes.
The scan found no specified GitHub/AWS credential or private-key signatures.
File/content inventory and provenance were reviewed in addition to scanning.
This is bounded pattern/exact-byte evidence, not exhaustive secret detection.

The commercial game and original firmware remain ignored, local inputs.
No original Nintendo/Commodore firmware, firmware dump archive, external emulator
executable or commercial game is part of the proposed release. The C# core and
own IPL/diagnostic generators remain independent. The screenshots show our own
diagnostic/menu output, not a commercial game.

## License boundaries reviewed

| Material | Distribution basis and retained evidence |
| --- | --- |
| Own C# core, frontend adaptations and authored tests | Apache-2.0; LICENSE and NOTICE |
| C64-derived SharpPixels/font/audio/transport and update interaction | Nils Kopal, Apache-2.0; original notices and source attribution retained; sibling repo unchanged |
| SingleStepTests 65816/SPC700 vectors | MIT; upstream license copies and pinned source/hash manifests |
| gilyon v1.4 programs and historical release ZIP | MIT, Copyright 2023 gilyon; LICENSE, source and release manifest retained |
| Marcus Rowe SNES test ROMs | Zlib; original license/source and pinned unmodified build provenance retained |
| Unmodified unnamed-snes-game laboratory demo | MIT code, Zlib driver, CC BY-SA 4.0 art/music/effects, CC0/public-domain/Apache samples; original LICENSE/CREDITS/THIRD_PARTY_LICENSES retained |
| Demo-derived laboratory media | Original art/music license boundaries continue to apply; not relicensed Apache and not installed by the emulator |
| Numeric hardware/reference traces | Authored synthetic/licensed inputs and numeric outputs, not reference source or executable; source/adapter/reference identities retained |
| External GPL/LGPL reference programs | Separately downloaded and executed local laboratory tools, not linked into or shipped with the C# product; notices retained with reference evidence |
| Gaussian coefficients | Numeric hardware data with retained ares ISC attribution; full notice also added to installed package |

Primary licenses/provenance checked include the
[Apache 2.0 redistribution conditions](https://www.apache.org/licenses/LICENSE-2.0),
[gilyon MIT grant](https://github.com/gilyon/snes-tests/blob/main/LICENSE),
[pinned demo credits](https://github.com/undisbeliever/unnamed-snes-engine/blob/1c06fee8fbaffbc720ca5fee3dd8c5f2f5838254/CREDITS.md),
[CC BY-SA 4.0 conditions](https://creativecommons.org/licenses/by-sa/4.0/) and
[FSF's explanation of program-output licensing](https://www.gnu.org/licenses/gpl-faq.html#WhatCaseIsOutputGPL).
Reference output is not automatically free of the input artwork's rights;
the explicit demo credits above remain applicable.

## Windows runtime/package notices

NAudio and OpenTK upstream MIT license texts, OpenTK's OpenEXR BSD notice, actual
GLFW COPYING, SQLite's public-domain statement, SQLitePCLRaw Apache attribution,
Microsoft runtime MIT licenses and complete .NET third-party notices accompany
the package. NuGet package metadata and dependency lockfiles preserve exact
versions and copyright holders. See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

The review corrected omissions in the older packaging: COPYING was not selected
by its filename filter, expression-only NuGet metadata did not supply every full
license text, and .NET framework packs were outside the ordinary library loop.
The new release collects these notices explicitly and checks required notices
inside the finished ZIP. Historical local preview packages are not relabeled
as having contained the new notices.

The source notices for NAudio/OpenTK are pinned to their NuGet release source
revisions. Inno Setup's retained license permits redistribution subject to its
copyright/origin conditions; its compiler is a build tool, not bundled as an
application dependency. The installer keeps its Inno copyright/resources and
the corresponding notice accompanies the release. Neither project branding nor
documentation represents Nintendo, Microsoft or other dependency authors as
endorsing this emulator.

On the reviewed evidence, no concrete redistribution blocker was identified for
the repository contents and proposed preview package after those notice fixes.
Future files/dependencies need their own review; publishing the preview does not
claim hardware accuracy or completed realtime acceptance.
